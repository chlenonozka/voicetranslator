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
        self.oom_error_resolver = (
            oom_error_resolver or _torch_oom_error_type
        )

    def run(self, operation: Callable[[str], ResultT]) -> ResultT:
        try:
            return operation("balanced")
        except Exception as error:
            if not self.is_oom(error):
                raise
            self.registry.unload_all()
            self.release_memory()
            return operation("low-memory")

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
