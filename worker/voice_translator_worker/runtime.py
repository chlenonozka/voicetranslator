import io
import json
import os
import wave
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path
from typing import Protocol

from fastapi import FastAPI

from .api import create_app
from .models.gpu_profiles import CudaReport, GpuProfile, inspect_cuda
from .models.model_manager import ModelManager, ProfileModelResidency
from .pipeline.preflight import PreflightService
from .pipeline.asr import RussianAsr
from .pipeline.languages import TARGET_LANGUAGES
from .pipeline.service import (
    PhrasePipeline,
    PhraseRecognizer,
    SpeakerConditioner,
)
from .pipeline.synthesis import XttsEngine, XttsSynthesizer
from .pipeline.translation import NllbTranslator
from .privacy.session_store import SpeakerSessionStore


REQUIRED_MODEL_IDS = frozenset(
    {
        "whisper-medium",
        "whisper-small",
        "nllb-600m",
        "xtts-v2",
    }
)


@dataclass(frozen=True)
class RuntimeConfig:
    model_root: Path
    receipt_path: Path
    worker_root: Path
    app_root: Path

    @staticmethod
    def from_environment() -> "RuntimeConfig":
        model_root = Path(
            os.environ.get(
                "VOICE_TRANSLATOR_MODEL_ROOT",
                Path.home() / ".voice-translator" / "models",
            )
        )
        return RuntimeConfig(
            model_root=model_root,
            receipt_path=Path(
                os.environ.get(
                    "VOICE_TRANSLATOR_MODEL_RECEIPT",
                    model_root / "verified-models.json",
                )
            ),
            worker_root=Path(
                os.environ.get(
                    "VOICE_TRANSLATOR_WORKER_ROOT",
                    Path.home() / ".voice-translator" / "worker",
                )
            ),
            app_root=Path(
                os.environ.get(
                    "VOICE_TRANSLATOR_APP_ROOT",
                    Path.home() / ".voice-translator" / "app",
                )
            ),
        )


@dataclass(frozen=True)
class LoadedModelSet:
    conditioner: SpeakerConditioner
    asr: PhraseRecognizer
    translator: object
    xtts_engine: XttsEngine
    unload: Callable[[], None]


class RuntimeModelLoader(Protocol):
    def load_profile(
        self,
        *,
        profile: str,
        whisper_model: str,
        model_root: Path,
    ) -> LoadedModelSet: ...


class RuntimeModelUnavailable(RuntimeError):
    pass


class ResidencyConditioner:
    def __init__(self, residency: ProfileModelResidency) -> None:
        self.residency = residency

    def create(self, reference_wav: bytes) -> object:
        return self.residency.require_loaded().conditioner.create(
            reference_wav
        )


class ResidencyAsr:
    def __init__(self, residency: ProfileModelResidency) -> None:
        self.residency = residency

    def transcribe(self, audio: bytes) -> object:
        return self.residency.require_loaded().asr.transcribe(audio)


class ResidencyTranslator:
    def __init__(self, residency: ProfileModelResidency) -> None:
        self.residency = residency

    def translate(
        self,
        text: str,
        target_code: str,
        *,
        unload_after: bool = False,
    ) -> str:
        translator = self.residency.require_loaded().translator
        return translator.translate(
            text,
            target_code,
            unload_after=unload_after,
        )


class ResidencyXttsEngine:
    def __init__(self, residency: ProfileModelResidency) -> None:
        self.residency = residency

    def synthesize(
        self,
        *,
        text: str,
        language: str,
        conditioning: object,
    ) -> bytes:
        return self.residency.require_loaded().xtts_engine.synthesize(
            text=text,
            language=language,
            conditioning=conditioning,
        )


class LazyLocalModelLoader:
    def load_profile(
        self,
        *,
        profile: str,
        whisper_model: str,
        model_root: Path,
    ) -> LoadedModelSet:
        from ctranslate2 import Translator
        from faster_whisper import WhisperModel
        from transformers import AutoTokenizer

        whisper = WhisperModel(
            str(model_root / f"whisper-{whisper_model}"),
            device="cuda",
            compute_type="int8",
        )
        nllb_path = model_root / "nllb-600m"
        nllb_engine = Translator(
            str(nllb_path),
            device="cuda",
            compute_type="int8_float16",
        )
        tokenizer = AutoTokenizer.from_pretrained(
            str(nllb_path),
            local_files_only=True,
        )
        xtts = InMemoryCoquiXttsAdapter.load(
            model_root / "xtts-v2"
        )

        def unload() -> None:
            unload_whisper = getattr(whisper, "unload_model", None)
            if unload_whisper is not None:
                unload_whisper()
            nllb_engine.unload_model()
            xtts.unload()

        return LoadedModelSet(
            conditioner=xtts,
            asr=RussianAsr(whisper),
            translator=NllbTranslator(nllb_engine, tokenizer),
            xtts_engine=xtts,
            unload=unload,
        )


class InMemoryCoquiXttsAdapter:
    def __init__(self, model: object) -> None:
        self.model = model

    @staticmethod
    def load(model_root: Path) -> "InMemoryCoquiXttsAdapter":
        from TTS.api import TTS

        config_path = model_root / "config.json"
        checkpoint_path = model_root / "model.pth"
        if not config_path.is_file() or not checkpoint_path.is_file():
            raise RuntimeModelUnavailable(
                "XTTS in-memory adapter requires config.json and model.pth."
            )

        tts = TTS(
            model_path=str(checkpoint_path),
            config_path=str(config_path),
            progress_bar=False,
        ).to("cuda")
        return InMemoryCoquiXttsAdapter(tts.synthesizer.tts_model)

    def create(self, reference_wav: bytes) -> object:
        import torchaudio

        waveform, sample_rate = torchaudio.load(
            io.BytesIO(reference_wav)
        )
        if sample_rate != 24_000:
            waveform = torchaudio.functional.resample(
                waveform,
                sample_rate,
                24_000,
            )
        return (
            self.model.get_gpt_cond_latents(
                waveform,
                sr=24_000,
            ),
            self.model.get_speaker_embedding(
                waveform,
                sr=24_000,
            ),
        )

    def synthesize(
        self,
        *,
        text: str,
        language: str,
        conditioning: object,
    ) -> bytes:
        gpt_cond_latent, speaker_embedding = conditioning
        result = self.model.inference(
            text,
            language,
            gpt_cond_latent,
            speaker_embedding,
        )
        samples = result["wav"]
        pcm = bytearray()
        for sample in samples:
            value = max(-1.0, min(1.0, float(sample)))
            pcm.extend(int(value * 32767).to_bytes(2, "little", signed=True))

        output = io.BytesIO()
        with wave.open(output, "wb") as wav:
            wav.setnchannels(1)
            wav.setsampwidth(2)
            wav.setframerate(24_000)
            wav.writeframes(pcm)
        return output.getvalue()

    def unload(self) -> None:
        self.model = None


def create_runtime_pipeline(
    *,
    config: RuntimeConfig | None = None,
    loader: RuntimeModelLoader | None = None,
) -> PhrasePipeline | None:
    active_config = config or RuntimeConfig.from_environment()
    if not _has_verified_models(
        active_config.receipt_path,
        active_config.model_root,
    ):
        return None

    residency = ProfileModelResidency(
        loader or LazyLocalModelLoader(),
        active_config.model_root,
    )
    sessions = SpeakerSessionStore()
    return PhrasePipeline(
        conditioner=ResidencyConditioner(residency),
        asr=ResidencyAsr(residency),
        translator=ResidencyTranslator(residency),
        synthesizer=XttsSynthesizer(
            ResidencyXttsEngine(residency),
            sessions,
        ),
        sessions=sessions,
        performance_profile="balanced",
        profile_controller=residency,
    )


def create_runtime_app(
    launch_token: str,
    pipeline_factory: Callable[[], PhrasePipeline | None] | None = None,
) -> FastAPI:
    factory = pipeline_factory or create_runtime_pipeline
    pipeline = factory()
    config = RuntimeConfig.from_environment()
    preflight = PreflightService(
        model_inventory=ModelManager(config.model_root),
        cuda_inspector=_inspect_runtime_cuda,
        language_probe=lambda target_code: (
            pipeline is not None
            and target_code in TARGET_LANGUAGES
        ),
    )
    return create_app(
        launch_token,
        pipeline,
        preflight_service=preflight,
    )


def _has_verified_models(
    receipt_path: Path,
    model_root: Path,
) -> bool:
    if not receipt_path.is_file():
        return False
    try:
        payload = json.loads(receipt_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return False
    verified = set(payload.get("verified", []))
    if not REQUIRED_MODEL_IDS.issubset(verified):
        return False

    manager = ModelManager(model_root)
    return all(
        manager.verify_installed(model_id)
        for model_id in REQUIRED_MODEL_IDS
    )


def _inspect_runtime_cuda() -> CudaReport:
    try:
        return inspect_cuda()
    except (ImportError, ModuleNotFoundError):
        return CudaReport(
            available=False,
            device_name=None,
            total_bytes=0,
            free_bytes=0,
            profile=GpuProfile(
                "cuda-runtime-unavailable",
                "small",
                "int8",
                "int8_float16",
            ),
        )
