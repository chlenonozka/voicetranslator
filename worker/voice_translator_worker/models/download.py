import argparse
from collections.abc import Sequence
from pathlib import Path
from typing import Protocol

from .model_manager import ModelManager, ModelManifest


DEFAULT_MODEL_IDS = (
    "whisper-medium",
    "whisper-small",
    "nllb-600m",
    "xtts-v2",
)


class ModelDownloader(Protocol):
    def ensure_license(self, model_id: str, accepted: bool) -> None: ...

    def download(
        self,
        manifest: ModelManifest,
        *,
        license_accepted: bool,
    ) -> Path: ...


def download_models(
    *,
    manifest_dir: Path,
    manager: ModelDownloader,
    model_ids: Sequence[str],
    accept_noncommercial: bool,
) -> list[Path]:
    receipts: list[Path] = []
    for model_id in model_ids:
        manifest = ModelManifest.load(manifest_dir / f"{model_id}.json")
        if not manifest.commercial_use_allowed:
            manager.ensure_license(model_id, accept_noncommercial)
        receipts.append(
            manager.download(
                manifest,
                license_accepted=accept_noncommercial,
            )
        )
    return receipts


def main(arguments: Sequence[str] | None = None) -> int:
    workspace_root = Path(__file__).resolve().parents[3]
    parser = argparse.ArgumentParser(
        description="Download verified local voice translator models.",
    )
    parser.add_argument(
        "model_ids",
        nargs="*",
        choices=DEFAULT_MODEL_IDS,
        default=DEFAULT_MODEL_IDS,
    )
    parser.add_argument(
        "--manifest-dir",
        type=Path,
        default=workspace_root / "models" / "manifests",
    )
    parser.add_argument(
        "--model-root",
        type=Path,
        default=Path.home() / ".voice-translator" / "models",
    )
    parser.add_argument(
        "--accept-noncommercial",
        action="store_true",
        help=(
            "Acknowledge that restricted models are for personal, "
            "noncommercial use."
        ),
    )
    options = parser.parse_args(arguments)

    receipts = download_models(
        manifest_dir=options.manifest_dir,
        manager=ModelManager(options.model_root),
        model_ids=options.model_ids,
        accept_noncommercial=options.accept_noncommercial,
    )
    for receipt in receipts:
        print(receipt)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
