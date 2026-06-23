import hashlib
from pathlib import Path
from typing import Any, Protocol


class LicenseNotAccepted(RuntimeError):
    """Raised when a restricted model license has not been accepted."""


class ModelManager:
    def __init__(self, model_root: Path) -> None:
        self.model_root = model_root

    def ensure_license(self, model_id: str, accepted: bool) -> None:
        if model_id == "nllb-600m" and not accepted:
            raise LicenseNotAccepted(
                "NLLB is restricted to personal noncommercial use."
            )

    @staticmethod
    def verify_sha256(path: Path, expected: str) -> bool:
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        return digest == expected.lower()


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
