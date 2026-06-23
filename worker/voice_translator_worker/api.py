from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Annotated
from uuid import UUID

from fastapi import (
    Depends,
    FastAPI,
    File,
    Form,
    HTTPException,
    Request,
    Response,
    UploadFile,
    status,
)

from .auth import token_dependency
from .pipeline.service import (
    InvalidTargetLanguage,
    LowConfidenceRecognition,
    PhrasePipeline,
    SpeakerSessionNotFound,
)
from .pipeline.recovery import OomRecovery


MAX_WAV_BYTES = 44 + (30 * 16_000 * 2)


def create_app(
    launch_token: str,
    pipeline: PhrasePipeline | None = None,
    recovery: OomRecovery | None = None,
) -> FastAPI:
    require_token = token_dependency(launch_token)
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
        session_id = active_pipeline.create_speaker_session(reference_wav)
        return {"sessionId": str(session_id)}

    @app.delete(
        "/v1/speaker-sessions/{session_id}",
        status_code=status.HTTP_204_NO_CONTENT,
        dependencies=[Depends(require_token)],
    )
    async def delete_speaker_session(session_id: UUID) -> Response:
        active_pipeline = _require_pipeline(pipeline)
        active_pipeline.delete_speaker_session(session_id)
        return Response(status_code=status.HTTP_204_NO_CONTENT)

    @app.post(
        "/v1/translate-phrase",
        dependencies=[Depends(require_token)],
    )
    async def translate_phrase(
        session_id: Annotated[UUID, Form(alias="sessionId")],
        target_language: Annotated[str, Form(alias="targetLanguage")],
        audio: Annotated[UploadFile, File()],
    ) -> Response:
        active_pipeline = _require_pipeline(pipeline)
        if audio.content_type != "audio/wav":
            raise HTTPException(
                status_code=status.HTTP_415_UNSUPPORTED_MEDIA_TYPE,
                detail="audio/wav is required",
            )
        audio_wav = await audio.read(MAX_WAV_BYTES + 1)
        await audio.close()
        _validate_upload_size(audio_wav)

        try:
            result = active_recovery.run(
                lambda profile: active_pipeline.translate_phrase(
                    session_id,
                    target_language,
                    audio_wav,
                    performance_profile=profile,
                )
            )
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
                raise
            active_pipeline.clear()
            raise HTTPException(
                status_code=status.HTTP_507_INSUFFICIENT_STORAGE,
                detail="GPU memory exhausted",
            ) from error

        return Response(
            content=result.audio_wav,
            media_type="audio/wav",
            headers={
                "X-Request-Id": str(result.request_id),
                "X-Asr-Ms": f"{result.asr_ms:.3f}",
                "X-Translate-Ms": f"{result.translate_ms:.3f}",
                "X-Synthesize-Ms": f"{result.synthesize_ms:.3f}",
                "X-Performance-Profile": result.performance_profile,
            },
        )

    return app


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
