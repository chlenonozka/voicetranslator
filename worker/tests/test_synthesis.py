from uuid import uuid4

from voice_translator_worker.pipeline.synthesis import XttsSynthesizer
from voice_translator_worker.privacy.session_store import SpeakerSessionStore


def test_delete_removes_reference_and_conditioning() -> None:
    store = SpeakerSessionStore()
    session_id = uuid4()
    reference_pcm = bytearray(b"pcm")
    store.put(session_id, reference_pcm, object())

    store.delete(session_id)

    assert store.contains(session_id) is False
    assert reference_pcm == bytearray()


def test_synthesis_uses_memory_conditioning_and_target_language() -> None:
    store = SpeakerSessionStore()
    session_id = uuid4()
    conditioning = object()
    store.put(session_id, bytearray(b"pcm"), conditioning)
    engine = FakeXttsEngine()
    synthesizer = XttsSynthesizer(engine, store)

    result = synthesizer.synthesize(session_id, "你好", "zh")

    assert result == b"wav"
    assert engine.text == "你好"
    assert engine.language == "zh-cn"
    assert engine.conditioning is conditioning


class FakeXttsEngine:
    def __init__(self) -> None:
        self.text = ""
        self.language = ""
        self.conditioning: object | None = None

    def synthesize(
        self,
        *,
        text: str,
        language: str,
        conditioning: object,
    ) -> bytes:
        self.text = text
        self.language = language
        self.conditioning = conditioning
        return b"wav"
