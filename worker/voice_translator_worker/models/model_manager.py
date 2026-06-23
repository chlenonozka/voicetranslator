import hashlib
import json
import re
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Protocol


class LicenseNotAccepted(RuntimeError):
    """Raised when a restricted model license has not been accepted."""


class ManifestError(ValueError):
    """Raised when a source manifest or install receipt is invalid."""


SnapshotDownloader = Callable[..., str]
ArtifactPreparer = Callable[["ModelManifest", Path, Path], None]
PINNED_REVISION = re.compile(r"^[0-9a-f]{40}$")


@dataclass(frozen=True)
class ModelManifest:
    id: str
    repo_id: str
    revision: str
    license: str
    commercial_use_allowed: bool
    files: tuple[dict[str, str], ...]

    @staticmethod
    def load(path: Path) -> "ModelManifest":
        try:
            payload = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise ManifestError(f"Invalid model manifest: {path}") from error

        manifest = ModelManifest(
            id=str(payload["id"]),
            repo_id=str(payload["repo_id"]),
            revision=str(payload["revision"]),
            license=str(payload["license"]),
            commercial_use_allowed=bool(
                payload["commercial_use_allowed"]
            ),
            files=tuple(payload.get("files", [])),
        )
        manifest.validate()
        return manifest

    def validate(self) -> None:
        if not PINNED_REVISION.fullmatch(self.revision):
            raise ManifestError(
                "Model revision must be a pinned 40-character commit SHA."
            )
        if not self.id or not self.repo_id or not self.license:
            raise ManifestError("Model manifest fields must not be empty.")


class ModelManager:
    def __init__(
        self,
        model_root: Path,
        *,
        snapshot_downloader: SnapshotDownloader | None = None,
        artifact_preparer: ArtifactPreparer | None = None,
    ) -> None:
        self.model_root = model_root
        self.snapshot_downloader = (
            snapshot_downloader or _snapshot_download
        )
        self.artifact_preparer = (
            artifact_preparer or _prepare_ctranslate2_artifacts
        )

    def ensure_license(self, model_id: str, accepted: bool) -> None:
        if model_id in {"nllb-600m", "xtts-v2"} and not accepted:
            raise LicenseNotAccepted(
                f"{model_id} requires explicit personal-use license "
                "acknowledgement."
            )

    @staticmethod
    def verify_sha256(path: Path, expected: str) -> bool:
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        return digest == expected.lower()

    def download(
        self,
        manifest: ModelManifest,
        *,
        license_accepted: bool,
    ) -> Path:
        manifest.validate()
        if not manifest.commercial_use_allowed:
            self.ensure_license(manifest.id, license_accepted)

        model_dir = self.model_root / manifest.id
        download_dir = model_dir
        if manifest.id == "nllb-600m":
            download_dir = (
                self.model_root
                / ".downloads"
                / f"{manifest.id}-{manifest.revision}"
            )
        self.snapshot_downloader(
            repo_id=manifest.repo_id,
            revision=manifest.revision,
            local_dir=download_dir,
        )
        if manifest.id == "nllb-600m":
            self.artifact_preparer(
                manifest,
                download_dir,
                model_dir,
            )
        return self.write_receipt(manifest)

    def write_receipt(self, manifest: ModelManifest) -> Path:
        manifest.validate()
        model_dir = self.model_root / manifest.id
        receipt_path = model_dir / "install-receipt.json"
        files = [
            {
                "path": path.relative_to(model_dir).as_posix(),
                "sha256": _sha256(path),
            }
            for path in sorted(model_dir.rglob("*"))
            if path.is_file()
            and path != receipt_path
            and ".cache" not in path.relative_to(model_dir).parts
        ]
        if not files:
            raise ManifestError(
                "Installed model receipt cannot have an empty files list."
            )

        receipt_path.write_text(
            json.dumps(
                {
                    "id": manifest.id,
                    "repo_id": manifest.repo_id,
                    "revision": manifest.revision,
                    "license": manifest.license,
                    "commercial_use_allowed":
                        manifest.commercial_use_allowed,
                    "files": files,
                },
                indent=2,
                sort_keys=True,
            ),
            encoding="utf-8",
        )
        self._update_verified_inventory(manifest.id)
        return receipt_path

    def verify_installed(self, model_id: str) -> bool:
        model_dir = self.model_root / model_id
        receipt_path = model_dir / "install-receipt.json"
        try:
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            files = receipt["files"]
        except (OSError, json.JSONDecodeError, KeyError, TypeError):
            return False
        if not files:
            return False

        return all(
            self.verify_sha256(
                model_dir / str(entry["path"]),
                str(entry["sha256"]),
            )
            for entry in files
            if (model_dir / str(entry["path"])).is_file()
        ) and all(
            (model_dir / str(entry["path"])).is_file()
            for entry in files
        )

    def _update_verified_inventory(self, model_id: str) -> None:
        self.model_root.mkdir(parents=True, exist_ok=True)
        inventory_path = self.model_root / "verified-models.json"
        try:
            inventory = json.loads(
                inventory_path.read_text(encoding="utf-8")
            )
        except (OSError, json.JSONDecodeError):
            inventory = {"verified": []}

        verified = set(inventory.get("verified", []))
        verified.add(model_id)
        inventory_path.write_text(
            json.dumps(
                {"verified": sorted(verified)},
                indent=2,
            ),
            encoding="utf-8",
        )


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _snapshot_download(**kwargs: object) -> str:
    from huggingface_hub import snapshot_download

    return snapshot_download(**kwargs)


def _prepare_ctranslate2_artifacts(
    manifest: ModelManifest,
    source_dir: Path,
    output_dir: Path,
) -> None:
    if manifest.id != "nllb-600m":
        raise ManifestError(
            f"No artifact preparer is registered for {manifest.id}."
        )

    from ctranslate2.converters import TransformersConverter

    tokenizer_files = [
        name
        for name in (
            "sentencepiece.bpe.model",
            "special_tokens_map.json",
            "tokenizer.json",
            "tokenizer_config.json",
        )
        if (source_dir / name).is_file()
    ]
    converter = TransformersConverter(
        str(source_dir),
        copy_files=tokenizer_files,
    )
    converter.convert(
        str(output_dir),
        quantization="int8_float16",
        force=True,
    )


class ProfileLoader(Protocol):
    def load_profile(
        self,
        *,
        profile: str,
        whisper_model: str,
        model_root: Path,
    ) -> Any: ...


class ProfileModelResidency:
    def __init__(
        self,
        loader: ProfileLoader,
        model_root: Path,
    ) -> None:
        self.loader = loader
        self.model_root = model_root
        self.loaded: Any | None = None
        self.active_profile: str | None = None

    def activate_profile(self, profile: str) -> None:
        if profile == self.active_profile and self.loaded is not None:
            return

        whisper_model = {
            "balanced": "medium",
            "low-memory": "small",
        }.get(profile)
        if whisper_model is None:
            raise ValueError(f"Unknown performance profile: {profile}")

        self.unload_all()
        self.loaded = self.loader.load_profile(
            profile=profile,
            whisper_model=whisper_model,
            model_root=self.model_root,
        )
        self.active_profile = profile

    def unload_all(self) -> None:
        if self.loaded is not None:
            self.loaded.unload()
        self.loaded = None
        self.active_profile = None

    def require_loaded(self) -> Any:
        if self.loaded is None:
            raise RuntimeError("No model profile is active.")
        return self.loaded
