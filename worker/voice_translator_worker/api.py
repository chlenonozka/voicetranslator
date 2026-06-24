from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
import logging
import sys
from threading import Lock
from typing import Annotated
from uuid import UUID, uuid4

from fastapi import (
    Depends,
    FastAPI,
    File,
    Form,
    Header,
    HTTPException,
    Request,
    Response,
    UploadFile,
    status,
)
from starlette.concurrency import run_in_threadpool

from .auth import token_dependency
from .pipeline.service import (
    InvalidTargetLanguage,
    LowConfidenceRecognition,
    PhrasePipeline,
    SpeakerSessionNotFound,
)
from .pipeline.recovery import OomRecovery
from .pipeline.preflight import PreflightService
from .models.gpu_profiles import release_torch_memory


MAX_WAV_BYTES = 44 + (30 * 16_000 * 2)
LOGGER = logging.getLogger(__name__)


class RequestCancellationRegistry:
    def __init__(self) -> None:
        self._request_ids: set[UUID] = set()
        self._lock = Lock()

    def cancel(self, request_id: UUID) -> None:
        with self._lock:
            self._request_ids.add(request_id)

    def is_cancelled(self, request_id: UUID) -> bool:
        with self._lock:
            return request_id in self._request_ids

    def clear(self) -> None:
        with self._lock:
            self._request_ids.clear()


def create_app(
    launch_token: str,
    pipeline: PhrasePipeline | None = None,
    recovery: OomRecovery | None = None,
    preflight_service: PreflightService | None = None,
) -> FastAPI:
    require_token = token_dependency(launch_token)
    cancellations = RequestCancellationRegistry()
    active_recovery = recovery
    if pipeline is not None and active_recovery is None:
        resolver = getattr(
            pipeline,
            "resolve_oom_error_type",
            None,
        )
        release_memory = getattr(
            pipeline,
            "release_memory",
            None,
        )
        recovery_options = {"registry": pipeline}
        if resolver is not None:
            recovery_options["oom_error_resolver"] = resolver
        if release_memory is not None:
            recovery_options["release_memory"] = release_memory
        active_recovery = OomRecovery(
            **recovery_options,
        )

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        yield
        if pipeline is not None:
            pipeline.clear()
            pipeline.unload_all()
        torch_module = sys.modules.get("torch")
        if torch_module is not None:
            release_torch_memory(torch_module)
        cancellations.clear()

    app = FastAPI(
        title="Local Voice Translation Worker",
        version="0.2.0",
        lifespan=lifespan,
    )

    @app.get(
        "/v1/health",
        dependencies=[Depends(require_token)],
    )
    async def health() -> dict[str, str]:
        return {"status": "ready"}

    @app.post(
        "/v1/preflight",
        dependencies=[Depends(require_token)],
    )
    async def preflight() -> dict[str, object]:
        if preflight_service is None:
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="preflight service is not ready",
            )
        return preflight_service.run().to_api()

    @app.post(
        "/v1/speaker-sessions",
        status_code=status.HTTP_201_CREATED,
        dependencies=[Depends(require_token)],
    )
    async def create_speaker_session(request: Request) -> dict[str, str]:
        active_pipeline = _require_pipeline(pipeline)
        if request.headers.get("content-type", "").split(";")[0] != "audio/wav":
            raise HTTPException(
                status_code=status.HTTP_415_UNSUPPORTED_MEDIA_TYPE,
                detail="audio/wav is required",
            )
        reference_wav = await request.body()
        _validate_upload_size(reference_wav)
        try:
            session_id = await run_in_threadpool(
                active_pipeline.create_speaker_session,
                reference_wav,
            )
        except Exception as error:
            raise _worker_stage_error(
                "speaker conditioning",
                error,
            ) from error
        return {"sessionId": str(session_id)}

    @app.delete(
        "/v1/speaker-sessions/{session_id}",
        status_code=status.HTTP_204_NO_CONTENT,
        dependencies=[Depends(require_token)],
    )
    async def delete_speaker_session(session_id: UUID) -> Response:
        active_pipeline = _require_pipeline(pipeline)
        await run_in_threadpool(
            active_pipeline.delete_speaker_session,
            session_id,
        )
        return Response(status_code=status.HTTP_204_NO_CONTENT)

    @app.post(
        "/v1/translate-phrase",
        dependencies=[Depends(require_token)],
    )
    async def translate_phrase(
        session_id: Annotated[UUID, Form(alias="sessionId")],
        target_language: Annotated[str, Form(alias="targetLanguage")],
        audio: Annotated[UploadFile, File()],
        x_request_id: Annotated[
            UUID | None,
            Header(alias="X-Request-Id"),
        ] = None,
    ) -> Response:
        active_pipeline = _require_pipeline(pipeline)
        request_id = x_request_id or uuid4()
        _reject_cancelled(cancellations, request_id)
        if audio.content_type != "audio/wav":
            raise HTTPException(
                status_code=status.HTTP_415_UNSUPPORTED_MEDIA_TYPE,
                detail="audio/wav is required",
            )
        audio_wav = await audio.read(MAX_WAV_BYTES + 1)
        await audio.close()
        _validate_upload_size(audio_wav)

        try:
            result = await run_in_threadpool(
                active_recovery.run,
                lambda profile: active_pipeline.translate_phrase(
                    session_id,
                    target_language,
                    audio_wav,
                    performance_profile=profile,
                ),
            )
            _reject_cancelled(cancellations, request_id)
        except SpeakerSessionNotFound as error:
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail="speaker session not found",
            ) from error
        except (InvalidTargetLanguage, LowConfidenceRecognition) as error:
            raise HTTPException(
                status_code=status.HTTP_422_UNPROCESSABLE_CONTENT,
                detail=str(error),
            ) from error
        except Exception as error:
            if active_recovery is None or not active_recovery.is_oom(error):
                raise _worker_stage_error(
                    "translation pipeline",
                    error,
                ) from error
            active_pipeline.clear()
            raise HTTPException(
                status_code=status.HTTP_507_INSUFFICIENT_STORAGE,
                detail="GPU memory exhausted",
            ) from error

        return Response(
            content=result.audio_wav,
            media_type="audio/wav",
            headers={
                "X-Request-Id": str(request_id),
                "X-Asr-Ms": f"{result.asr_ms:.3f}",
                "X-Translate-Ms": f"{result.translate_ms:.3f}",
                "X-Synthesize-Ms": f"{result.synthesize_ms:.3f}",
                "X-Performance-Profile": result.performance_profile,
            },
        )

    @app.post(
        "/v1/cancel/{request_id}",
        status_code=status.HTTP_202_ACCEPTED,
        dependencies=[Depends(require_token)],
    )
    async def cancel_request(request_id: UUID) -> dict[str, str]:
        cancellations.cancel(request_id)
        return {"status": "cancellation-requested"}

    return app


def _worker_stage_error(stage: str, error: Exception) -> HTTPException:
    LOGGER.exception("%s failed", stage)
    message = str(error).strip()
    detail = f"{stage} failed: {type(error).__name__}"
    if message:
        detail += f": {message}"
    return HTTPException(
        status_code=status.HTTP_502_BAD_GATEWAY,
        detail=detail,
    )


def _require_pipeline(
    pipeline: PhrasePipeline | None,
) -> PhrasePipeline:
    if pipeline is None:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="translation pipeline is not ready",
        )
    return pipeline


def _validate_upload_size(audio_wav: bytes) -> None:
    if not audio_wav:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_CONTENT,
            detail="audio is empty",
        )
    if len(audio_wav) > MAX_WAV_BYTES:
        raise HTTPException(
            status_code=status.HTTP_413_CONTENT_TOO_LARGE,
            detail="audio exceeds 30 seconds of mono 16 kHz PCM",
        )


def _reject_cancelled(
    cancellations: RequestCancellationRegistry,
    request_id: UUID,
) -> None:
    if cancellations.is_cancelled(request_id):
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="request canceled",
        )
