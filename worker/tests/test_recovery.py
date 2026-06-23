import builtins
from pathlib import Path
from uuid import UUID, uuid4

import pytest
from fastapi.testclient import TestClient

from voice_translator_worker.api import create_app
from voice_translator_worker.pipeline.recovery import OomRecovery
from voice_translator_worker.pipeline.service import (
    PhrasePipeline,
    PhraseResult,
)
from voice_translator_worker.pipeline.asr import Recognition
from voice_translator_worker.privacy.session_store import SpeakerSessionStore
from voice_translator_worker.runtime import RuntimeConfig


class FakeCudaOutOfMemoryError(RuntimeError):
    pass


def test_balanced_oom_releases_models_and_retries_low_memory_once() -> None:
    calls: list[str] = []
    registry = FakeRegistry()
    releases: list[bool] = []
    recovery = OomRecovery(
        registry=registry,
        release_memory=lambda: releases.append(True),
        oom_error_type=FakeCudaOutOfMemoryError,
    )

    def operation(profile: str) -> str:
        calls.append(profile)
        if profile == "balanced":
            raise FakeCudaOutOfMemoryError("balanced OOM")
        return "translated"

    result = recovery.run(operation)

    assert result == "translated"
    assert calls == ["balanced", "low-memory"]
    assert registry.unload_count == 1
    assert releases == [True]


def test_second_oom_is_not_caught_as_success() -> None:
    calls: list[str] = []
    registry = FakeRegistry()
    recovery = OomRecovery(
        registry=registry,
        release_memory=lambda: None,
        oom_error_type=FakeCudaOutOfMemoryError,
    )

    def operation(profile: str) -> str:
        calls.append(profile)
        raise FakeCudaOutOfMemoryError(profile)

    with pytest.raises(FakeCudaOutOfMemoryError, match="low-memory"):
        recovery.run(operation)

    assert calls == ["balanced", "low-memory"]
    assert registry.unload_count == 1


def test_second_oom_maps_to_http_507_and_clears_session() -> None:
    pipeline = AlwaysOomPipeline()
    pipeline.oom_error_type = FakeCudaOutOfMemoryError
    headers = {"X-Worker-Token": "expected-token"}

    with TestClient(create_app("expected-token", pipeline)) as client:
        response = client.post(
            "/v1/translate-phrase",
            data={
                "sessionId": str(uuid4()),
                "targetLanguage": "en",
            },
            files={"audio": ("phrase.wav", b"phrase-wav", "audio/wav")},
            headers=headers,
        )

        assert response.status_code == 507
        assert response.json()["detail"] == "GPU memory exhausted"
        assert pipeline.profiles == ["balanced", "low-memory"]
        assert pipeline.clear_count == 1


def test_pipeline_activates_balanced_then_low_memory_before_asr() -> None:
    controller = RecordingProfileController()
    sessions = SpeakerSessionStore()
    pipeline = PhrasePipeline(
        conditioner=FakeConditioner(),
        asr=ProfileAwareAsr(controller),
        translator=FakeTranslator(),
        synthesizer=FakeSynthesizer(),
        sessions=sessions,
        performance_profile="balanced",
        profile_controller=controller,
    )
    session_id = pipeline.create_speaker_session(b"reference")
    controller.activations.clear()
    recovery = OomRecovery(
        registry=pipeline,
        release_memory=lambda: None,
        oom_error_type=FakeCudaOutOfMemoryError,
    )

    result = recovery.run(
        lambda profile: pipeline.translate_phrase(
            session_id,
            "en",
            b"phrase",
            performance_profile=profile,
        )
    )

    assert controller.activations == ["balanced", "low-memory"]
    assert controller.unload_count == 1
    assert result.performance_profile == "low-memory"


def test_default_recovery_construction_and_health_do_not_import_torch(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    imported: list[str] = []
    real_import = builtins.__import__

    def guarded_import(name: str, *args: object, **kwargs: object) -> object:
        if name == "torch":
            imported.append(name)
            raise AssertionError("torch imported during app construction")
        return real_import(name, *args, **kwargs)

    monkeypatch.setattr(builtins, "__import__", guarded_import)
    pipeline = NonOomPipeline()

    with TestClient(create_app("expected-token", pipeline)) as client:
        response = client.get(
            "/v1/health",
            headers={"X-Worker-Token": "expected-token"},
        )

    assert response.status_code == 200
    assert imported == []


def test_second_oom_does_not_persist_speech_or_text(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    worker_root = tmp_path / "worker-root"
    app_root = tmp_path / "app-root"
    model_root = tmp_path / "models"
    receipt_path = tmp_path / "verified-models.json"
    for path in (worker_root, app_root, model_root):
        path.mkdir()
    monkeypatch.setenv(
        "VOICE_TRANSLATOR_WORKER_ROOT",
        str(worker_root),
    )
    monkeypatch.setenv("VOICE_TRANSLATOR_APP_ROOT", str(app_root))
    monkeypatch.setenv("VOICE_TRANSLATOR_MODEL_ROOT", str(model_root))
    monkeypatch.setenv(
        "VOICE_TRANSLATOR_MODEL_RECEIPT",
        str(receipt_path),
    )
    config = RuntimeConfig.from_environment()
    sessions = SpeakerSessionStore()
    controller = RecordingProfileController()
    pipeline = PhrasePipeline(
        conditioner=FakeConditioner(),
        asr=AlwaysOomAsr(),
        translator=FakeTranslator(),
        synthesizer=FakeSynthesizer(),
        sessions=sessions,
        performance_profile="balanced",
        profile_controller=controller,
    )
    recovery = OomRecovery(
        registry=pipeline,
        release_memory=lambda: None,
        oom_error_type=FakeCudaOutOfMemoryError,
    )

    with TestClient(
        create_app("expected-token", pipeline, recovery)
    ) as client:
        create_response = client.post(
            "/v1/speaker-sessions",
            content=b"reference-wav",
            headers={
                "X-Worker-Token": "expected-token",
                "Content-Type": "audio/wav",
            },
        )
        session_id = create_response.json()["sessionId"]
        response = client.post(
            "/v1/translate-phrase",
            data={
                "sessionId": session_id,
                "targetLanguage": "en",
            },
            files={"audio": ("phrase.wav", b"phrase-wav", "audio/wav")},
            headers={"X-Worker-Token": "expected-token"},
        )

        assert response.status_code == 507
        assert not sessions.contains(UUID(session_id))

    forbidden_terms = (
        ".wav",
        ".pcm",
        "transcript",
        "translation",
        "embedding",
    )
    for root in (config.worker_root, config.app_root):
        artifacts = [
            path
            for path in root.rglob("*")
            if path.is_file()
            and any(
                term in path.name.lower()
                for term in forbidden_terms
            )
        ]
        assert artifacts == []


class FakeRegistry:
    def __init__(self) -> None:
        self.unload_count = 0

    def unload_all(self) -> None:
        self.unload_count += 1


class AlwaysOomPipeline:
    def __init__(self) -> None:
        self.profiles: list[str] = []
        self.clear_count = 0
        self.unload_count = 0
        self.oom_error_type: type[Exception] = RuntimeError

    def translate_phrase(
        self,
        session_id: object,
        target_language: str,
        audio_wav: bytes,
        *,
        performance_profile: str | None = None,
    ) -> PhraseResult:
        assert session_id
        assert target_language == "en"
        assert audio_wav == b"phrase-wav"
        assert performance_profile is not None
        self.profiles.append(performance_profile)
        raise FakeCudaOutOfMemoryError(performance_profile)

    def clear(self) -> None:
        self.clear_count += 1

    def unload_all(self) -> None:
        self.unload_count += 1

    def resolve_oom_error_type(self) -> type[Exception]:
        return self.oom_error_type

    @staticmethod
    def release_memory() -> None:
        pass


class NonOomPipeline(AlwaysOomPipeline):
    def translate_phrase(
        self,
        session_id: object,
        target_language: str,
        audio_wav: bytes,
        *,
        performance_profile: str | None = None,
    ) -> PhraseResult:
        raise AssertionError("health must not run inference")


class RecordingProfileController:
    def __init__(self) -> None:
        self.activations: list[str] = []
        self.unload_count = 0

    def activate_profile(self, profile: str) -> None:
        self.activations.append(profile)

    def unload_all(self) -> None:
        self.unload_count += 1


class FakeConditioner:
    @staticmethod
    def create(reference_wav: bytes) -> object:
        return object()


class ProfileAwareAsr:
    def __init__(self, controller: RecordingProfileController) -> None:
        self.controller = controller
        self.calls = 0

    def transcribe(self, audio: bytes) -> Recognition:
        self.calls += 1
        assert self.controller.activations[-1] in {"balanced", "low-memory"}
        if self.calls == 1:
            raise FakeCudaOutOfMemoryError("balanced")
        return Recognition("Привет", accepted=True)


class AlwaysOomAsr:
    @staticmethod
    def transcribe(audio: bytes) -> Recognition:
        raise FakeCudaOutOfMemoryError("terminal OOM")


class FakeTranslator:
    @staticmethod
    def translate(
        text: str,
        target_code: str,
        *,
        unload_after: bool = False,
    ) -> str:
        return "Hello"


class FakeSynthesizer:
    @staticmethod
    def synthesize(
        session_id: object,
        text: str,
        target_code: str,
    ) -> bytes:
        return b"wav"
