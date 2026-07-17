from voice_translator_worker.models.gpu_profiles import (
    choose_profile,
    inspect_cuda,
    release_torch_memory,
)


def test_8gb_gpu_uses_balanced_profile_when_memory_is_available() -> None:
    profile = choose_profile(total_bytes=8 * 1024**3, free_bytes=7 * 1024**3)

    assert profile.name == "balanced"
    assert profile.whisper_model == "large-v3-turbo"
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


def test_cuda_inspection_reports_device_and_selected_profile() -> None:
    report = inspect_cuda(FakeTorchForInspection())

    assert report.available is True
    assert report.device_name == "RTX 3070"
    assert report.total_bytes == 8 * 1024**3
    assert report.free_bytes == 6 * 1024**3
    assert report.profile.name == "balanced"


def test_cuda_inspection_reports_unavailable_without_device() -> None:
    report = inspect_cuda(FakeTorchUnavailable())

    assert report.available is False
    assert report.profile.name == "cpu-unavailable"


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


class FakeDeviceProperties:
    name = "RTX 3070"
    total_memory = 8 * 1024**3


class FakeCudaForInspection:
    @staticmethod
    def is_available() -> bool:
        return True

    @staticmethod
    def current_device() -> int:
        return 0

    @staticmethod
    def get_device_properties(index: int) -> FakeDeviceProperties:
        assert index == 0
        return FakeDeviceProperties()

    @staticmethod
    def mem_get_info(index: int) -> tuple[int, int]:
        assert index == 0
        return 6 * 1024**3, 8 * 1024**3


class FakeTorchForInspection:
    cuda = FakeCudaForInspection()


class FakeCudaUnavailable:
    @staticmethod
    def is_available() -> bool:
        return False


class FakeTorchUnavailable:
    cuda = FakeCudaUnavailable()
