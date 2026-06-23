from voice_translator_worker.pipeline.languages import TARGET_LANGUAGES


def test_language_catalog_contains_exactly_16_targets() -> None:
    assert list(TARGET_LANGUAGES.keys()) == [
        "ar",
        "zh",
        "cs",
        "nl",
        "en",
        "fr",
        "de",
        "hi",
        "hu",
        "it",
        "ja",
        "ko",
        "pl",
        "pt",
        "es",
        "tr",
    ]


def test_every_target_has_nllb_and_xtts_codes() -> None:
    for target in TARGET_LANGUAGES.values():
        assert target.nllb
        assert target.xtts
