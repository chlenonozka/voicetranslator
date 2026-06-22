from dataclasses import dataclass
from types import MappingProxyType
from typing import Mapping


@dataclass(frozen=True)
class Target:
    nllb: str
    xtts: str


TARGET_LANGUAGES: Mapping[str, Target] = MappingProxyType(
    {
        "ar": Target("arb_Arab", "ar"),
        "zh": Target("zho_Hans", "zh-cn"),
        "cs": Target("ces_Latn", "cs"),
        "nl": Target("nld_Latn", "nl"),
        "en": Target("eng_Latn", "en"),
        "fr": Target("fra_Latn", "fr"),
        "de": Target("deu_Latn", "de"),
        "hi": Target("hin_Deva", "hi"),
        "hu": Target("hun_Latn", "hu"),
        "it": Target("ita_Latn", "it"),
        "ja": Target("jpn_Jpan", "ja"),
        "ko": Target("kor_Hang", "ko"),
        "pl": Target("pol_Latn", "pl"),
        "pt": Target("por_Latn", "pt"),
        "es": Target("spa_Latn", "es"),
        "tr": Target("tur_Latn", "tr"),
    }
)
