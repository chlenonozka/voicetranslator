import hashlib
from pathlib import Path


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
