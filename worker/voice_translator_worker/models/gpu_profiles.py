import gc
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class GpuProfile:
    name: str
    whisper_model: str
    whisper_compute_type: str
    nllb_compute_type: str


def choose_profile(total_bytes: int, free_bytes: int) -> GpuProfile:
    gib = 1024**3
    if total_bytes >= 7 * gib and free_bytes >= 5 * gib:
        return GpuProfile("balanced", "medium", "int8", "int8_float16")
    return GpuProfile("low-memory", "small", "int8", "int8_float16")


def release_torch_memory(torch_module: Any | None = None) -> None:
    gc.collect()
    if torch_module is None:
        import torch

        torch_module = torch

    if torch_module.cuda.is_available():
        torch_module.cuda.synchronize()
        torch_module.cuda.empty_cache()
