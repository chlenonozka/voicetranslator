from collections.abc import Callable
from dataclasses import dataclass
from typing import Protocol

from voice_translator_worker.models.gpu_profiles import CudaReport

from .languages import TARGET_LANGUAGES


REQUIRED_MODEL_IDS = (
    "whisper-medium",
    "whisper-small",
    "nllb-600m",
    "xtts-v2",
)


class ModelInventory(Protocol):
    def verify_installed(self, model_id: str) -> bool: ...


@dataclass(frozen=True)
class PreflightReport:
    ready: bool
    cuda_available: bool
    device_name: str | None
    total_vram_bytes: int
    free_vram_bytes: int
    performance_profile: str
    missing_models: tuple[str, ...]
    available_languages: tuple[str, ...]

    def to_api(self) -> dict[str, object]:
        return {
            "ready": self.ready,
            "cudaAvailable": self.cuda_available,
            "deviceName": self.device_name,
            "totalVramBytes": self.total_vram_bytes,
            "freeVramBytes": self.free_vram_bytes,
            "performanceProfile": self.performance_profile,
            "missingModels": list(self.missing_models),
            "availableLanguages": list(self.available_languages),
        }


class PreflightService:
    def __init__(
        self,
        *,
        model_inventory: ModelInventory,
        cuda_inspector: Callable[[], CudaReport],
        language_probe: Callable[[str], bool] | None = None,
    ) -> None:
        self.model_inventory = model_inventory
        self.cuda_inspector = cuda_inspector
        self.language_probe = language_probe

    def run(self) -> PreflightReport:
        missing_models = tuple(
            model_id
            for model_id in REQUIRED_MODEL_IDS
            if not self.model_inventory.verify_installed(model_id)
        )
        cuda = self.cuda_inspector()
        available_languages: tuple[str, ...] = ()
        if not missing_models and cuda.available and self.language_probe is not None:
            available_languages = tuple(
                target_code
                for target_code in TARGET_LANGUAGES
                if self._probe(target_code)
            )

        return PreflightReport(
            ready=(not missing_models and cuda.available and bool(available_languages)),
            cuda_available=cuda.available,
            device_name=cuda.device_name,
            total_vram_bytes=cuda.total_bytes,
            free_vram_bytes=cuda.free_bytes,
            performance_profile=cuda.profile.name,
            missing_models=missing_models,
            available_languages=available_languages,
        )

    def _probe(self, target_code: str) -> bool:
        if self.language_probe is None:
            return False
        try:
            return bool(self.language_probe(target_code))
        except Exception:
            return False
