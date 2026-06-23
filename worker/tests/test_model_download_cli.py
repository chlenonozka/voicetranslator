from pathlib import Path

import pytest

from voice_translator_worker.models.download import download_models
from voice_translator_worker.models.model_manager import LicenseNotAccepted


def test_download_models_requires_noncommercial_acknowledgement(
    tmp_path: Path,
) -> None:
    manager = FakeModelManager()

    with pytest.raises(LicenseNotAccepted):
        download_models(
            manifest_dir=Path("models/manifests"),
            manager=manager,
            model_ids=["nllb-600m"],
            accept_noncommercial=False,
        )


def test_download_models_loads_selected_manifests(
    tmp_path: Path,
) -> None:
    manifest_dir = tmp_path / "manifests"
    manifest_dir.mkdir()
    (manifest_dir / "whisper-small.json").write_text(
        """
        {
          "id": "whisper-small",
          "repo_id": "owner/model",
          "revision": "0000000000000000000000000000000000000000",
          "license": "MIT",
          "commercial_use_allowed": true,
          "files": []
        }
        """,
        encoding="utf-8",
    )
    manager = FakeModelManager()

    receipts = download_models(
        manifest_dir=manifest_dir,
        manager=manager,
        model_ids=["whisper-small"],
        accept_noncommercial=False,
    )

    assert manager.downloaded == [("whisper-small", False)]
    assert receipts == [Path("whisper-small-receipt.json")]


class FakeModelManager:
    def __init__(self) -> None:
        self.downloaded: list[tuple[str, bool]] = []

    @staticmethod
    def ensure_license(model_id: str, accepted: bool) -> None:
        if model_id == "nllb-600m" and not accepted:
            raise LicenseNotAccepted(model_id)

    def download(
        self,
        manifest: object,
        *,
        license_accepted: bool,
    ) -> Path:
        model_id = str(getattr(manifest, "id"))
        self.downloaded.append((model_id, license_accepted))
        return Path(f"{model_id}-receipt.json")
