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
        self.options: dict[str, object] = {}

    def transcribe(self, audio: object, **kwargs: object):
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


def test_asr_rejects_low_confidence_segment() -> None:
    model = FakeModel([FakeSegment(avg_logprob=-1.1)])
    asr = RussianAsr(model)

    result = asr.transcribe([0.0, 0.1])

    assert result.text == "Привет"
    assert result.accepted is False
