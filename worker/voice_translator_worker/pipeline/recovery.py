from collections.abc import Callable
from typing import Protocol, TypeVar

from voice_translator_worker.models.gpu_profiles import release_torch_memory


ResultT = TypeVar("ResultT")


class ModelRegistry(Protocol):
    def unload_all(self) -> None: ...


class OomRecovery:
    def __init__(
        self,
        *,
        registry: ModelRegistry,
        release_memory: Callable[[], None] = release_torch_memory,
        oom_error_type: type[Exception] | None = None,
        oom_error_resolver: Callable[[], type[Exception]] | None = None,
    ) -> None:
        self.registry = registry
        self.release_memory = release_memory
        self.oom_error_type = oom_error_type
        self.oom_error_resolver = oom_error_resolver or _torch_oom_error_type

    def run(
        self,
        operation: Callable[[str], ResultT],
        initial_profile: str = "balanced",
    ) -> ResultT:
        profile_chains = {
            "performance": ("performance", "balanced", "low-memory"),
            "balanced": ("balanced", "low-memory"),
            "low-memory": ("low-memory",),
        }
        profiles = profile_chains.get(initial_profile, (initial_profile,))
        for index, profile in enumerate(profiles):
            try:
                return operation(profile)
            except Exception as error:
                is_last_attempt = index == len(profiles) - 1
                if not self.is_oom(error) or is_last_attempt:
                    raise
                self.registry.unload_all()
                self.release_memory()

        raise RuntimeError("performance profile recovery exhausted")

    def is_oom(self, error: Exception) -> bool:
        error_type = self.oom_error_type
        if error_type is None:
            try:
                error_type = self.oom_error_resolver()
            except ModuleNotFoundError:
                return False
        return isinstance(error, error_type)


def _torch_oom_error_type() -> type[Exception]:
    import torch

    return torch.cuda.OutOfMemoryError
