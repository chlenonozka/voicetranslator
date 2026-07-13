from pathlib import Path

from fastapi.testclient import TestClient

from voice_translator_worker.api import create_app
from voice_translator_worker.models.gpu_profiles import (
    CudaReport,
    GpuProfile,
)
from voice_translator_worker.pipeline.languages import TARGET_LANGUAGES
from voice_translator_worker.pipeline.preflight import PreflightService


def test_preflight_checks_all_languages_and_returns_only_passing_targets(
    tmp_path: Path,
) -> None:
    probed: list[str] = []

    def probe(target_code: str) -> bool:
        probed.append(target_code)
        return target_code != "ja"

    service = PreflightService(
        model_inventory=AlwaysInstalledModels(),
        cuda_inspector=available_cuda,
        language_probe=probe,
    )

    report = service.run()

    assert set(probed) == set(TARGET_LANGUAGES)
    assert len(report.available_languages) == 15
    assert "ja" not in report.available_languages
    assert report.ready is True


def test_preflight_is_not_ready_when_models_are_missing() -> None:
    service = PreflightService(
        model_inventory=MissingModels(),
        cuda_inspector=available_cuda,
    )

    report = service.run()

    assert report.ready is False
    assert report.missing_models == ("xtts-v2",)
    assert report.available_languages == ()


def test_authenticated_preflight_endpoint_returns_hardware_report() -> None:
    service = PreflightService(
        model_inventory=AlwaysInstalledModels(),
        cuda_inspector=available_cuda,
        language_probe=lambda _: True,
    )
    client = TestClient(create_app("expected-token", preflight_service=service))

    response = client.post(
        "/v1/preflight",
        headers={"X-Worker-Token": "expected-token"},
    )

    assert response.status_code == 200
    assert response.json()["performanceProfile"] == "balanced"
    assert len(response.json()["availableLanguages"]) == 16


class AlwaysInstalledModels:
    @staticmethod
    def verify_installed(model_id: str) -> bool:
        return True


class MissingModels:
    @staticmethod
    def verify_installed(model_id: str) -> bool:
        return model_id != "xtts-v2"


def available_cuda() -> CudaReport:
    return CudaReport(
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
    )
