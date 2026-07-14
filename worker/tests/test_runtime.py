import io
import shutil
import sys
from typing import Any, cast
import wave
from pathlib import Path
from types import SimpleNamespace

import pytest
from fastapi.testclient import TestClient

from voice_translator_worker.models.model_manager import (
    ModelManager,
    ModelManifest,
)
from voice_translator_worker.models.gpu_profiles import CudaReport, GpuProfile
from voice_translator_worker.pipeline.asr import Recognition
from voice_translator_worker.runtime import (
    InMemoryCoquiXttsAdapter,
    LazyLocalModelLoader,
    LoadedModelSet,
    RuntimeConfig,
    create_runtime_app,
    create_runtime_pipeline,
    _load_pcm16_wave,
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
    balanced_asr = cast(FakeAsr, loader.loaded[0].asr)
    pipeline.unload_all()
    pipeline.translate_phrase(
        session_id,
        "en",
        b"low-memory",
        performance_profile="low-memory",
    )
    low_memory_asr = cast(FakeAsr, loader.loaded[1].asr)

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
            pipeline_factory=lambda: cast(Any, object()),
        )
    )

    response = client.post(
        "/v1/preflight",
        headers={"X-Worker-Token": "token"},
    )

    assert response.status_code == 200
    assert response.json()["ready"] is True
    assert len(response.json()["availableLanguages"]) == 16


def test_lazy_loader_configures_cuda_runtime_and_keeps_nllb_on_cpu(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    cuda_runtime_configured: list[bool] = []
    translator_calls: list[dict[str, Any]] = []

    monkeypatch.setattr(
        "voice_translator_worker.runtime._configure_windows_cuda_runtime",
        lambda: cuda_runtime_configured.append(True),
    )

    class RecordingTranslator:
        def __init__(
            self,
            model_path: str,
            *,
            device: str,
            compute_type: str,
        ) -> None:
            translator_calls.append(
                {
                    "model_path": model_path,
                    "device": device,
                    "compute_type": compute_type,
                }
            )

    class RecordingWhisperModel:
        def __init__(
            self,
            model_path: str,
            *,
            device: str,
            compute_type: str,
        ) -> None:
            pass

    class RecordingTokenizer:
        @staticmethod
        def from_pretrained(path: str, *, local_files_only: bool) -> Any:
            return SimpleNamespace()

    monkeypatch.setitem(
        sys.modules,
        "ctranslate2",
        SimpleNamespace(Translator=RecordingTranslator),
    )
    monkeypatch.setitem(sys.modules, "torch", SimpleNamespace())
    monkeypatch.setitem(
        sys.modules,
        "faster_whisper",
        SimpleNamespace(WhisperModel=RecordingWhisperModel),
    )
    monkeypatch.setitem(
        sys.modules,
        "transformers",
        SimpleNamespace(AutoTokenizer=RecordingTokenizer),
    )
    monkeypatch.setattr(
        InMemoryCoquiXttsAdapter,
        "load",
        staticmethod(lambda model_root: FakeXttsEngine()),
    )

    LazyLocalModelLoader().load_profile(
        profile="balanced",
        whisper_model="medium",
        model_root=tmp_path,
    )

    assert translator_calls == [
        {
            "model_path": str(tmp_path / "nllb-600m"),
            "device": "cpu",
            "compute_type": "int8",
        }
    ]
    assert cuda_runtime_configured == [True]


def test_reference_wave_loader_reads_pcm16_without_torchaudio(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    real_import = __import__

    def guarded_import(
        name: str,
        *args: Any,
        **kwargs: Any,
    ) -> Any:
        if name == "torchaudio":
            raise AssertionError("reference WAV loading must not use torchaudio")
        return real_import(name, *args, **kwargs)

    monkeypatch.setattr("builtins.__import__", guarded_import)
    reference_wav = create_pcm16_wave(
        sample_rate=16_000,
        channel_count=1,
        samples=[0, 32767, -32768],
    )

    waveform, sample_rate = _load_pcm16_wave(reference_wav)

    assert sample_rate == 16_000
    assert tuple(waveform.shape) == (1, 3)
    assert waveform.tolist()[0] == pytest.approx(
        [0, 32767 / 32768, -1],
    )


def test_reference_wave_loader_rejects_non_pcm16() -> None:
    reference_wav = create_pcm8_wave(
        sample_rate=16_000,
        samples=[128, 255],
    )

    with pytest.raises(ValueError, match="16-bit PCM"):
        _load_pcm16_wave(reference_wav)


def create_pcm16_wave(
    *,
    sample_rate: int,
    channel_count: int,
    samples: list[int],
) -> bytes:
    output = io.BytesIO()
    with wave.open(output, "wb") as wav:
        wav.setnchannels(channel_count)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        wav.writeframes(
            b"".join(sample.to_bytes(2, "little", signed=True) for sample in samples)
        )
    return output.getvalue()


def create_pcm8_wave(
    *,
    sample_rate: int,
    samples: list[int],
) -> bytes:
    output = io.BytesIO()
    with wave.open(output, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(1)
        wav.setframerate(sample_rate)
        wav.writeframes(bytes(samples))
    return output.getvalue()


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
    def create(reference_wav: bytes) -> Any:
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
        conditioning: Any,
    ) -> bytes:
        return b"wav"
