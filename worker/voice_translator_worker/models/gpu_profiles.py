import gc
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class GpuProfile:
    name: str
    whisper_model: str
    whisper_compute_type: str
    nllb_compute_type: str


@dataclass(frozen=True)
class CudaReport:
    available: bool
    device_name: str | None
    total_bytes: int
    free_bytes: int
    profile: GpuProfile


def choose_profile(total_bytes: int, free_bytes: int) -> GpuProfile:
    gib = 1024**3
    if total_bytes >= 7 * gib and free_bytes >= 5 * gib:
        return GpuProfile("balanced", "medium", "int8", "int8_float16")
    return GpuProfile("low-memory", "small", "int8", "int8_float16")


def inspect_cuda(torch_module: Any | None = None) -> CudaReport:
    if torch_module is None:
        import torch

        torch_module = torch

    if not torch_module.cuda.is_available():
        return CudaReport(
            available=False,
            device_name=None,
            total_bytes=0,
            free_bytes=0,
            profile=GpuProfile(
                "cpu-unavailable",
                "small",
                "int8",
                "int8_float16",
            ),
        )

    device = torch_module.cuda.current_device()
    properties = torch_module.cuda.get_device_properties(device)
    free_bytes, total_bytes = torch_module.cuda.mem_get_info(device)
    return CudaReport(
        available=True,
        device_name=str(properties.name),
        total_bytes=int(total_bytes),
        free_bytes=int(free_bytes),
        profile=choose_profile(
            total_bytes=int(total_bytes),
            free_bytes=int(free_bytes),
        ),
    )


def release_torch_memory(torch_module: Any | None = None) -> None:
    gc.collect()
    if torch_module is None:
        import torch

        torch_module = torch

    if torch_module.cuda.is_available():
        torch_module.cuda.synchronize()
        torch_module.cuda.empty_cache()
