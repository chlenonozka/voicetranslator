from threading import Event, Thread
from fastapi.testclient import TestClient
from uuid import uuid4

from voice_translator_worker.api import create_app
from voice_translator_worker.pipeline.asr import Recognition
from voice_translator_worker.pipeline.service import PhrasePipeline
from voice_translator_worker.pipeline.synthesis import XttsSynthesizer
from voice_translator_worker.privacy.session_store import SpeakerSessionStore


def test_speaker_session_translate_delete_lifecycle() -> None:
    sessions = SpeakerSessionStore()
    pipeline = PhrasePipeline(
        conditioner=FakeConditioner(),
        asr=FakeAsr(),
        translator=FakeTextTranslator(),
        synthesizer=XttsSynthesizer(FakeXttsEngine(), sessions),
        sessions=sessions,
        performance_profile="balanced",
    )
    headers = {"X-Worker-Token": "expected-token"}

    with TestClient(create_app("expected-token", pipeline)) as client:
        create_response = client.post(
            "/v1/speaker-sessions",
            content=b"reference-wav",
            headers={**headers, "Content-Type": "audio/wav"},
        )

        assert create_response.status_code == 201
        session_id = create_response.json()["sessionId"]

        translation_response = client.post(
            "/v1/translate-phrase",
            data={
                "sessionId": session_id,
                "targetLanguage": "en",
            },
            files={"audio": ("phrase.wav", b"phrase-wav", "audio/wav")},
            headers=headers,
        )

        assert translation_response.status_code == 200
        assert translation_response.content == b"wav"
        assert translation_response.headers["content-type"] == "audio/wav"
        assert translation_response.headers["x-performance-profile"] == "balanced"
        assert translation_response.headers["x-request-id"]

        delete_response = client.delete(
            f"/v1/speaker-sessions/{session_id}",
            headers=headers,
        )

        assert delete_response.status_code == 204

        missing_response = client.post(
            "/v1/translate-phrase",
            data={
                "sessionId": session_id,
                "targetLanguage": "en",
            },
            files={"audio": ("phrase.wav", b"phrase-wav", "audio/wav")},
            headers=headers,
        )

        assert missing_response.status_code == 404


class FakeConditioner:
    @staticmethod
    def create(reference_wav: bytes) -> object:
        assert reference_wav == b"reference-wav"
        return object()


class BlockingConditioner:
    def __init__(self) -> None:
        self.started = Event()
        self.release = Event()

    def create(self, reference_wav: bytes) -> object:
        assert reference_wav == b"reference-wav"
        self.started.set()
        assert self.release.wait(timeout=2)
        return object()


class FakeAsr:
    @staticmethod
    def transcribe(audio: bytes) -> Recognition:
        assert audio == b"phrase-wav"
        return Recognition(text="Привет", accepted=True)


class FakeTextTranslator:
    @staticmethod
    def translate(
        text: str,
        target_code: str,
        *,
        unload_after: bool = False,
    ) -> str:
        assert text == "Привет"
        assert target_code == "en"
        assert unload_after is True
        return "Hello"


class FakeXttsEngine:
    @staticmethod
    def synthesize(
        *,
        text: str,
        language: str,
        conditioning: object,
    ) -> bytes:
        assert text == "Hello"
        assert language == "en"
        assert conditioning is not None
        return b"wav"


def test_cancelled_request_id_is_rejected_before_inference() -> None:
    sessions = SpeakerSessionStore()
    pipeline = PhrasePipeline(
        conditioner=FakeConditioner(),
        asr=FakeAsr(),
        translator=FakeTextTranslator(),
        synthesizer=XttsSynthesizer(FakeXttsEngine(), sessions),
        sessions=sessions,
        performance_profile="balanced",
    )
    request_id = uuid4()
    headers = {"X-Worker-Token": "expected-token"}

    with TestClient(create_app("expected-token", pipeline)) as client:
        create_response = client.post(
            "/v1/speaker-sessions",
            content=b"reference-wav",
            headers={**headers, "Content-Type": "audio/wav"},
        )
        session_id = create_response.json()["sessionId"]
        cancel_response = client.post(
            f"/v1/cancel/{request_id}",
            headers=headers,
        )
        response = client.post(
            "/v1/translate-phrase",
            data={
                "sessionId": session_id,
                "targetLanguage": "en",
            },
            files={"audio": ("phrase.wav", b"phrase-wav", "audio/wav")},
            headers={
                **headers,
                "X-Request-Id": str(request_id),
            },
        )

    assert cancel_response.status_code == 202
    assert response.status_code == 409
    assert response.json()["detail"] == "request canceled"


def test_health_responds_while_speaker_session_conditioning_runs() -> None:
    sessions = SpeakerSessionStore()
    conditioner = BlockingConditioner()
    pipeline = PhrasePipeline(
        conditioner=conditioner,
        asr=FakeAsr(),
        translator=FakeTextTranslator(),
        synthesizer=XttsSynthesizer(FakeXttsEngine(), sessions),
        sessions=sessions,
        performance_profile="balanced",
    )
    headers = {"X-Worker-Token": "expected-token"}

    with TestClient(create_app("expected-token", pipeline)) as client:
        statuses: list[int] = []

        def create_session() -> None:
            response = client.post(
                "/v1/speaker-sessions",
                content=b"reference-wav",
                headers={**headers, "Content-Type": "audio/wav"},
            )
            statuses.append(response.status_code)

        thread = Thread(target=create_session)
        thread.start()
        assert conditioner.started.wait(timeout=2)

        health_response = client.get(
            "/v1/health",
            headers=headers,
        )

        conditioner.release.set()
        thread.join(timeout=2)

        assert health_response.status_code == 200
        assert statuses == [201]


def test_shutdown_clears_sessions_and_unloads_models() -> None:
    sessions = SpeakerSessionStore()
    controller = RecordingModelResidency()
    pipeline = PhrasePipeline(
        conditioner=FakeConditioner(),
        asr=FakeAsr(),
        translator=FakeTextTranslator(),
        synthesizer=XttsSynthesizer(FakeXttsEngine(), sessions),
        sessions=sessions,
        performance_profile="balanced",
        profile_controller=controller,
    )

    with TestClient(create_app("expected-token", pipeline)) as client:
        response = client.get(
            "/v1/health",
            headers={"X-Worker-Token": "expected-token"},
        )
        assert response.status_code == 200

    assert controller.unload_count == 1


class RecordingModelResidency:
    def __init__(self) -> None:
        self.unload_count = 0

    @staticmethod
    def activate_profile(profile: str) -> None:
        pass

    def unload_all(self) -> None:
        self.unload_count += 1
