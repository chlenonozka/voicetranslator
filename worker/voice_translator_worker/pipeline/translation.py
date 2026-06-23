from typing import Any

from .languages import TARGET_LANGUAGES


class NllbTranslator:
    def __init__(self, translator: Any, tokenizer: Any) -> None:
        self.translator = translator
        self.tokenizer = tokenizer
        self.tokenizer.src_lang = "rus_Cyrl"
        self._unloaded = False

    def translate(
        self,
        text: str,
        target_code: str,
        *,
        unload_after: bool = False,
    ) -> str:
        target = TARGET_LANGUAGES[target_code]
        if self._unloaded:
            self.translator.load_model()
            self._unloaded = False
        source_ids = self.tokenizer.encode(text)
        source_tokens = self.tokenizer.convert_ids_to_tokens(source_ids)

        try:
            results = self.translator.translate_batch(
                [source_tokens],
                target_prefix=[[target.nllb]],
            )
            target_tokens = results[0].hypotheses[0][1:]
            target_ids = self.tokenizer.convert_tokens_to_ids(target_tokens)
            return self.tokenizer.decode(target_ids).strip()
        finally:
            if unload_after:
                self.translator.unload_model()
                self._unloaded = True
