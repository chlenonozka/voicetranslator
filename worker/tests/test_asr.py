import io
import wave

import numpy as np
import pytest

from voice_translator_worker.pipeline.asr import RussianAsr


class FakeSegment:
    def __init__(
        self,
        text: str = " Привет ",
        avg_logprob: float = -0.2,
        no_speech_prob: float = 0.01,
    ) -> None:
        self.text = text
        self.avg_logprob = avg_logprob
        self.no_speech_prob = no_speech_prob


class FakeModel:
    def __init__(self, segments: list[FakeSegment]) -> None:
        self.segments = segments
        self.audio: object | None = None
        self.options: dict[str, object] = {}

    def transcribe(self, audio: object, **kwargs: object):
        self.audio = audio
        self.options = kwargs
        return iter(self.segments), object()


def test_asr_forces_russian_and_trims_text() -> None:
    model = FakeModel([FakeSegment()])
    asr = RussianAsr(model)

    result = asr.transcribe([0.0, 0.1])

    assert result.text == "Привет"
    assert result.accepted is True
    assert model.options["language"] == "ru"
    assert model.options["task"] == "transcribe"


def test_asr_decodes_worker_wave_bytes_before_transcribing() -> None:
    model = FakeModel([FakeSegment()])
    asr = RussianAsr(model)

    result = asr.transcribe(create_pcm16_wave([0, 32767, -32768]))

    assert result.accepted is True
    assert isinstance(model.audio, np.ndarray)
    np.testing.assert_allclose(
        model.audio,
        np.array([0, 32767 / 32768, -1], dtype=np.float32),
    )


def test_asr_rejects_non_worker_sample_rate() -> None:
    model = FakeModel([FakeSegment()])
    asr = RussianAsr(model)

    with pytest.raises(ValueError, match="16 kHz"):
        asr.transcribe(create_pcm16_wave([0, 1], sample_rate=24_000))


def test_asr_rejects_low_confidence_segment() -> None:
    model = FakeModel([FakeSegment(avg_logprob=-1.1)])
    asr = RussianAsr(model)

    result = asr.transcribe([0.0, 0.1])

    assert result.text == "Привет"
    assert result.accepted is False


def create_pcm16_wave(
    samples: list[int],
    *,
    sample_rate: int = 16_000,
) -> bytes:
    output = io.BytesIO()
    with wave.open(output, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        wav.writeframes(
            b"".join(
                sample.to_bytes(2, "little", signed=True)
                for sample in samples
            )
        )
    return output.getvalue()
