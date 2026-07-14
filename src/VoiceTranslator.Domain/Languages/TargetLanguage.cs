namespace VoiceTranslator.Domain.Languages;

public sealed record TargetLanguage(
    string Code,
    string DisplayName,
    string NllbCode,
    string XttsCode)
{
    public static readonly TargetLanguage Arabic =
        new("ar", "Арабский", "arb_Arab", "ar");

    public static readonly TargetLanguage Chinese =
        new("zh", "Китайский", "zho_Hans", "zh-cn");

    public static readonly TargetLanguage Czech =
        new("cs", "Чешский", "ces_Latn", "cs");

    public static readonly TargetLanguage Dutch =
        new("nl", "Нидерландский", "nld_Latn", "nl");

    public static readonly TargetLanguage English =
        new("en", "Английский", "eng_Latn", "en");

    public static readonly TargetLanguage French =
        new("fr", "Французский", "fra_Latn", "fr");

    public static readonly TargetLanguage German =
        new("de", "Немецкий", "deu_Latn", "de");

    public static readonly TargetLanguage Hindi =
        new("hi", "Хинди", "hin_Deva", "hi");

    public static readonly TargetLanguage Hungarian =
        new("hu", "Венгерский", "hun_Latn", "hu");

    public static readonly TargetLanguage Italian =
        new("it", "Итальянский", "ita_Latn", "it");

    public static readonly TargetLanguage Japanese =
        new("ja", "Японский", "jpn_Jpan", "ja");

    public static readonly TargetLanguage Korean =
        new("ko", "Корейский", "kor_Hang", "ko");

    public static readonly TargetLanguage Polish =
        new("pl", "Польский", "pol_Latn", "pl");

    public static readonly TargetLanguage Portuguese =
        new("pt", "Португальский", "por_Latn", "pt");

    public static readonly TargetLanguage Spanish =
        new("es", "Испанский", "spa_Latn", "es");

    public static readonly TargetLanguage Turkish =
        new("tr", "Турецкий", "tur_Latn", "tr");

    public static IReadOnlyList<TargetLanguage> All { get; } =
    [
        Arabic,
        Chinese,
        Czech,
        Dutch,
        English,
        French,
        German,
        Hindi,
        Hungarian,
        Italian,
        Japanese,
        Korean,
        Polish,
        Portuguese,
        Spanish,
        Turkish,
    ];
}
