from voice_translator_worker.pipeline.languages import TARGET_LANGUAGES
from voice_translator_worker.pipeline.translation import NllbTranslator


def test_target_catalog_has_16_languages() -> None:
    assert len(TARGET_LANGUAGES) == 16
    assert TARGET_LANGUAGES["en"].nllb == "eng_Latn"
    assert TARGET_LANGUAGES["ar"].nllb == "arb_Arab"
    assert TARGET_LANGUAGES["zh"].xtts == "zh-cn"
    assert "ru" not in TARGET_LANGUAGES


def test_translation_forces_target_prefix_and_decodes_text() -> None:
    engine = FakeTranslator()
    tokenizer = FakeTokenizer()
    translator = NllbTranslator(engine, tokenizer)

    result = translator.translate("Привет", "en")

    assert result == "Hello"
    assert tokenizer.encoded_text == "Привет"
    assert tokenizer.src_lang == "rus_Cyrl"
    assert engine.source_batch == [["src-token"]]
    assert engine.target_prefix == [["eng_Latn"]]


def test_translation_can_unload_model_after_request() -> None:
    engine = FakeTranslator()
    translator = NllbTranslator(engine, FakeTokenizer())

    translator.translate("Привет", "en", unload_after=True)

    assert engine.unloaded is True


class FakeResult:
    hypotheses = [["eng_Latn", "translated-token"]]


class FakeTranslator:
    def __init__(self) -> None:
        self.source_batch: list[list[str]] = []
        self.target_prefix: list[list[str]] = []
        self.unloaded = False

    def translate_batch(
        self,
        source_batch: list[list[str]],
        *,
        target_prefix: list[list[str]],
    ) -> list[FakeResult]:
        self.source_batch = source_batch
        self.target_prefix = target_prefix
        return [FakeResult()]

    def unload_model(self) -> None:
        self.unloaded = True


class FakeTokenizer:
    def __init__(self) -> None:
        self.encoded_text = ""
        self.src_lang = ""

    def encode(self, text: str) -> list[int]:
        self.encoded_text = text
        return [1]

    @staticmethod
    def convert_ids_to_tokens(token_ids: list[int]) -> list[str]:
        return ["src-token"]

    @staticmethod
    def convert_tokens_to_ids(tokens: list[str]) -> list[int]:
        assert tokens == ["translated-token"]
        return [2]

    @staticmethod
    def decode(token_ids: list[int]) -> str:
        assert token_ids == [2]
        return "Hello"
