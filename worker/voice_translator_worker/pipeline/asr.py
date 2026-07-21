import io
import wave
from dataclasses import dataclass
from typing import Any, Protocol

import numpy as np


class WhisperLike(Protocol):
    def transcribe(self, audio: object, **kwargs: object) -> tuple[Any, object]: ...


@dataclass(frozen=True)
class Recognition:
    text: str
    accepted: bool


class RussianAsr:
    def __init__(self, model: WhisperLike, *, beam_size: int = 5) -> None:
        self.model = model
        self.beam_size = beam_size

    def transcribe(self, audio: object) -> Recognition:
        normalized_audio = _decode_worker_wave(audio)
        segments, _ = self.model.transcribe(
            normalized_audio,
            language="ru",
            task="transcribe",
            vad_filter=True,
            beam_size=self.beam_size,
            condition_on_previous_text=False,
            no_repeat_ngram_size=3,
        )
        values = list(segments)
        text = " ".join(segment.text.strip() for segment in values).strip()
        accepted = bool(text) and all(
            segment.no_speech_prob < 0.6 and segment.avg_logprob > -1.0
            for segment in values
        )
        return Recognition(text=text, accepted=accepted)


def _decode_worker_wave(audio: object) -> object:
    if not isinstance(audio, bytes):
        return audio

    with wave.open(io.BytesIO(audio), "rb") as wav:
        channel_count = wav.getnchannels()
        sample_width = wav.getsampwidth()
        sample_rate = wav.getframerate()
        pcm = wav.readframes(wav.getnframes())

    if sample_width != 2:
        raise ValueError("ASR WAV must be 16-bit PCM")
    if sample_rate != 16_000:
        raise ValueError("ASR WAV must be 16 kHz")
    if channel_count < 1:
        raise ValueError("ASR WAV must contain at least one channel")
    if not pcm:
        raise ValueError("ASR WAV contains no samples")

    samples = np.frombuffer(pcm, dtype="<i2").astype(np.float32) / 32768.0
    if channel_count > 1:
        samples = samples.reshape(-1, channel_count).mean(axis=1)
    return samples
