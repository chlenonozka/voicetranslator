from voice_translator_worker.models.gpu_profiles import (
    choose_profile,
    release_torch_memory,
)


def test_8gb_gpu_uses_balanced_profile_when_memory_is_available() -> None:
    profile = choose_profile(total_bytes=8 * 1024**3, free_bytes=7 * 1024**3)

    assert profile.name == "balanced"
    assert profile.whisper_model == "medium"
    assert profile.whisper_compute_type == "int8"


def test_low_free_memory_uses_small_whisper() -> None:
    profile = choose_profile(total_bytes=8 * 1024**3, free_bytes=3 * 1024**3)

    assert profile.name == "low-memory"
    assert profile.whisper_model == "small"


def test_release_torch_memory_clears_available_cuda_cache() -> None:
    cuda = FakeCuda()

    release_torch_memory(FakeTorch(cuda))

    assert cuda.synchronized is True
    assert cuda.cache_cleared is True


class FakeCuda:
    def __init__(self) -> None:
        self.synchronized = False
        self.cache_cleared = False

    @staticmethod
    def is_available() -> bool:
        return True

    def synchronize(self) -> None:
        self.synchronized = True

    def empty_cache(self) -> None:
        self.cache_cleared = True


class FakeTorch:
    def __init__(self, cuda: FakeCuda) -> None:
        self.cuda = cuda
