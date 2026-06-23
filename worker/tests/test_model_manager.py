import json
from pathlib import Path

import pytest

from voice_translator_worker.models.model_manager import (
    LicenseNotAccepted,
    ManifestError,
    ModelManifest,
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


def test_manifest_requires_pinned_commit_revision(tmp_path: Path) -> None:
    manifest_path = tmp_path / "model.json"
    manifest_path.write_text(
        json.dumps(
            {
                "id": "model",
                "repo_id": "owner/model",
                "revision": "main",
                "license": "MIT",
                "commercial_use_allowed": True,
                "files": [],
            }
        ),
        encoding="utf-8",
    )

    with pytest.raises(ManifestError, match="40-character"):
        ModelManifest.load(manifest_path)


def test_download_uses_pinned_revision_and_writes_hashed_receipt(
    tmp_path: Path,
) -> None:
    calls: list[dict[str, object]] = []

    def fake_snapshot_download(**kwargs: object) -> str:
        calls.append(kwargs)
        local_dir = Path(str(kwargs["local_dir"]))
        local_dir.mkdir(parents=True)
        (local_dir / "config.json").write_bytes(b"config")
        (local_dir / "model.bin").write_bytes(b"model")
        return str(local_dir)

    manager = ModelManager(
        tmp_path / "models",
        snapshot_downloader=fake_snapshot_download,
    )
    manifest = ModelManifest(
        id="whisper-small",
        repo_id="Systran/faster-whisper-small",
        revision="536b0662742c02347bc0e980a01041f333bce120",
        license="MIT",
        commercial_use_allowed=True,
        files=(),
    )

    receipt_path = manager.download(manifest, license_accepted=True)

    assert calls == [
        {
            "repo_id": manifest.repo_id,
            "revision": manifest.revision,
            "local_dir": tmp_path / "models" / manifest.id,
        }
    ]
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    assert receipt["revision"] == manifest.revision
    assert {entry["path"] for entry in receipt["files"]} == {
        "config.json",
        "model.bin",
    }
    assert all(len(entry["sha256"]) == 64 for entry in receipt["files"])


def test_offline_verification_uses_receipt_and_rejects_corruption(
    tmp_path: Path,
) -> None:
    def fail_if_network_called(**kwargs: object) -> str:
        raise AssertionError("offline verification must not use the network")

    model_root = tmp_path / "models"
    model_dir = model_root / "whisper-small"
    model_dir.mkdir(parents=True)
    model_file = model_dir / "model.bin"
    model_file.write_bytes(b"model")
    manager = ModelManager(
        model_root,
        snapshot_downloader=fail_if_network_called,
    )
    manager.write_receipt(
        ModelManifest(
            id="whisper-small",
            repo_id="Systran/faster-whisper-small",
            revision="536b0662742c02347bc0e980a01041f333bce120",
            license="MIT",
            commercial_use_allowed=True,
            files=(),
        )
    )

    assert manager.verify_installed("whisper-small") is True

    model_file.write_bytes(b"corrupt")

    assert manager.verify_installed("whisper-small") is False


def test_nllb_download_prepares_ctranslate2_artifacts_before_receipt(
    tmp_path: Path,
) -> None:
    prepared: list[tuple[Path, Path]] = []

    def fake_snapshot_download(**kwargs: object) -> str:
        source_dir = Path(str(kwargs["local_dir"]))
        source_dir.mkdir(parents=True)
        (source_dir / "pytorch_model.bin").write_bytes(b"raw")
        return str(source_dir)

    def fake_preparer(
        manifest: ModelManifest,
        source_dir: Path,
        output_dir: Path,
    ) -> None:
        assert manifest.id == "nllb-600m"
        prepared.append((source_dir, output_dir))
        output_dir.mkdir(parents=True)
        (output_dir / "model.bin").write_bytes(b"converted")
        (output_dir / "sentencepiece.bpe.model").write_bytes(b"tokenizer")

    manager = ModelManager(
        tmp_path / "models",
        snapshot_downloader=fake_snapshot_download,
        artifact_preparer=fake_preparer,
    )
    manifest = ModelManifest(
        id="nllb-600m",
        repo_id="facebook/nllb-200-distilled-600M",
        revision="f8d333a098d19b4fd9a8b18f94170487ad3f821d",
        license="CC-BY-NC-4.0",
        commercial_use_allowed=False,
        files=(),
    )

    receipt = manager.download(manifest, license_accepted=True)

    source_dir = (
        tmp_path
        / "models"
        / ".downloads"
        / f"{manifest.id}-{manifest.revision}"
    )
    output_dir = tmp_path / "models" / manifest.id
    assert prepared == [(source_dir, output_dir)]
    assert receipt == output_dir / "install-receipt.json"
    assert manager.verify_installed(manifest.id) is True
