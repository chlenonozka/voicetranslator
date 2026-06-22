from dataclasses import dataclass
from typing import Any, Protocol


class WhisperLike(Protocol):
    def transcribe(self, audio: object, **kwargs: object) -> tuple[Any, object]: ...


@dataclass(frozen=True)
class Recognition:
    text: str
    accepted: bool


class RussianAsr:
    def __init__(self, model: WhisperLike) -> None:
        self.model = model

    def transcribe(self, audio: object) -> Recognition:
        segments, _ = self.model.transcribe(
            audio,
            language="ru",
            task="transcribe",
            vad_filter=True,
            beam_size=5,
            condition_on_previous_text=False,
        )
        values = list(segments)
        text = " ".join(segment.text.strip() for segment in values).strip()
        accepted = bool(text) and all(
            segment.no_speech_prob < 0.6 and segment.avg_logprob > -1.0
            for segment in values
        )
        return Recognition(text=text, accepted=accepted)
