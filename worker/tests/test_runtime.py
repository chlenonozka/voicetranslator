import shutil
from pathlib import Path

from fastapi.testclient import TestClient

from voice_translator_worker.models.model_manager import (
    ModelManager,
    ModelManifest,
)
from voice_translator_worker.models.gpu_profiles import CudaReport, GpuProfile
from voice_translator_worker.pipeline.asr import Recognition
from voice_translator_worker.runtime import (
    LoadedModelSet,
    RuntimeConfig,
    create_runtime_app,
    create_runtime_pipeline,
)


def test_default_composition_switches_actual_medium_to_small_models(
    tmp_path: Path,
) -> None:
    config = create_verified_config(tmp_path)
    loader = FakeRuntimeModelLoader()

    pipeline = create_runtime_pipeline(config=config, loader=loader)

    assert pipeline is not None
    session_id = pipeline.create_speaker_session(b"reference")
    pipeline.translate_phrase(
        session_id,
        "en",
        b"balanced",
        performance_profile="balanced",
    )
    balanced_asr = loader.loaded[0].asr
    pipeline.unload_all()
    pipeline.translate_phrase(
        session_id,
        "en",
        b"low-memory",
        performance_profile="low-memory",
    )
    low_memory_asr = loader.loaded[1].asr

    assert loader.requests == [
        ("balanced", "medium"),
        ("low-memory", "small"),
    ]
    assert balanced_asr is not low_memory_asr
    assert balanced_asr.calls == [b"balanced"]
    assert low_memory_asr.calls == [b"low-memory"]


def test_default_composition_returns_unavailable_without_verified_receipt(
    tmp_path: Path,
) -> None:
    config = RuntimeConfig(
        model_root=tmp_path / "models",
        receipt_path=tmp_path / "missing.json",
        worker_root=tmp_path / "worker-data",
        app_root=tmp_path / "app-data",
    )

    pipeline = create_runtime_pipeline(
        config=config,
        loader=FakeRuntimeModelLoader(),
    )

    assert pipeline is None


def test_default_composition_returns_unavailable_when_artifacts_are_missing(
    tmp_path: Path,
) -> None:
    config = create_verified_config(tmp_path)
    shutil.rmtree(config.model_root / "xtts-v2")

    pipeline = create_runtime_pipeline(
        config=config,
        loader=FakeRuntimeModelLoader(),
    )

    assert pipeline is None


def test_default_composition_rejects_corrupted_installed_model(
    tmp_path: Path,
) -> None:
    config = create_verified_config(tmp_path)
    corrupted = config.model_root / "whisper-small" / "model.bin"
    corrupted.write_bytes(b"corrupt")

    pipeline = create_runtime_pipeline(
        config=config,
        loader=FakeRuntimeModelLoader(),
    )

    assert pipeline is None


def test_runtime_preflight_exposes_languages_when_pipeline_is_available(
    tmp_path: Path,
    monkeypatch,
) -> None:
    config = create_verified_config(tmp_path)
    monkeypatch.setattr(
        RuntimeConfig,
        "from_environment",
        staticmethod(lambda: config),
    )
    monkeypatch.setattr(
        "voice_translator_worker.runtime._inspect_runtime_cuda",
        lambda: CudaReport(
            available=True,
            device_name="RTX 3070",
            total_bytes=8 * 1024**3,
            free_bytes=6 * 1024**3,
            profile=GpuProfile(
                "balanced",
                "medium",
                "int8",
                "int8_float16",
            ),
        ),
    )
    client = TestClient(
        create_runtime_app(
            "token",
            pipeline_factory=lambda: object(),
        )
    )

    response = client.post(
        "/v1/preflight",
        headers={"X-Worker-Token": "token"},
    )

    assert response.status_code == 200
    assert response.json()["ready"] is True
    assert len(response.json()["availableLanguages"]) == 16


def create_verified_config(tmp_path: Path) -> RuntimeConfig:
    model_root = tmp_path / "models"
    model_root.mkdir()
    for model_id in (
        "whisper-medium",
        "whisper-small",
        "nllb-600m",
        "xtts-v2",
    ):
        model_dir = model_root / model_id
        model_dir.mkdir()
        (model_dir / "model.bin").write_bytes(model_id.encode())
        ModelManager(model_root).write_receipt(
            ModelManifest(
                id=model_id,
                repo_id=f"owner/{model_id}",
                revision="0" * 40,
                license="test",
                commercial_use_allowed=True,
                files=(),
            )
        )
    receipt_path = model_root / "verified-models.json"
    return RuntimeConfig(
        model_root=model_root,
        receipt_path=receipt_path,
        worker_root=tmp_path / "worker-data",
        app_root=tmp_path / "app-data",
    )


class FakeRuntimeModelLoader:
    def __init__(self) -> None:
        self.requests: list[tuple[str, str]] = []
        self.loaded: list[LoadedModelSet] = []

    def load_profile(
        self,
        *,
        profile: str,
        whisper_model: str,
        model_root: Path,
    ) -> LoadedModelSet:
        self.requests.append((profile, whisper_model))
        loaded = LoadedModelSet(
            conditioner=FakeConditioner(),
            asr=FakeAsr(),
            translator=FakeTranslator(),
            xtts_engine=FakeXttsEngine(),
            unload=lambda: None,
        )
        self.loaded.append(loaded)
        return loaded


class FakeConditioner:
    @staticmethod
    def create(reference_wav: bytes) -> object:
        return {"conditioning": bytes(reference_wav)}


class FakeAsr:
    def __init__(self) -> None:
        self.calls: list[bytes] = []

    def transcribe(self, audio: bytes) -> Recognition:
        self.calls.append(audio)
        return Recognition("Привет", accepted=True)


class FakeTranslator:
    @staticmethod
    def translate(
        text: str,
        target_code: str,
        *,
        unload_after: bool = False,
    ) -> str:
        return "Hello"


class FakeXttsEngine:
    @staticmethod
    def synthesize(
        *,
        text: str,
        language: str,
        conditioning: object,
    ) -> bytes:
        return b"wav"
