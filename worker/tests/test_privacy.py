from pathlib import Path
from uuid import uuid4

from voice_translator_worker.privacy.session_store import SpeakerSessionStore


def test_speaker_session_lifecycle_creates_no_files(
    tmp_path: Path,
    monkeypatch,
) -> None:
    monkeypatch.chdir(tmp_path)
    store = SpeakerSessionStore()
    session_id = uuid4()
    reference = bytearray(b"private pcm")

    store.put(session_id, reference, object())
    store.delete(session_id)

    assert reference == bytearray()
    assert list(tmp_path.rglob("*")) == []
