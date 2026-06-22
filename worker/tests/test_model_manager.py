from pathlib import Path

import pytest

from voice_translator_worker.models.model_manager import (
    LicenseNotAccepted,
    ModelManager,
)


def test_noncommercial_model_requires_acknowledgement(tmp_path: Path) -> None:
    manager = ModelManager(tmp_path)

    with pytest.raises(LicenseNotAccepted):
        manager.ensure_license("nllb-600m", accepted=False)


def test_hash_mismatch_rejects_model(tmp_path: Path) -> None:
    manager = ModelManager(tmp_path)
    file_path = tmp_path / "model.bin"
    file_path.write_bytes(b"corrupt")

    assert manager.verify_sha256(file_path, "00" * 32) is False
