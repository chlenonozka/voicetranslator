namespace VoiceTranslator.Domain.Languages;

public sealed record TargetLanguage(
    string Code,
    string DisplayName,
    string NllbCode,
    string XttsCode)
{
    public static readonly TargetLanguage Arabic =
        new("ar", "Arabic", "arb_Arab", "ar");

    public static readonly TargetLanguage Chinese =
        new("zh", "Chinese", "zho_Hans", "zh-cn");

    public static readonly TargetLanguage Czech =
        new("cs", "Czech", "ces_Latn", "cs");

    public static readonly TargetLanguage Dutch =
        new("nl", "Dutch", "nld_Latn", "nl");

    public static readonly TargetLanguage English =
        new("en", "English", "eng_Latn", "en");

    public static readonly TargetLanguage French =
        new("fr", "French", "fra_Latn", "fr");

    public static readonly TargetLanguage German =
        new("de", "German", "deu_Latn", "de");

    public static readonly TargetLanguage Hindi =
        new("hi", "Hindi", "hin_Deva", "hi");

    public static readonly TargetLanguage Hungarian =
        new("hu", "Hungarian", "hun_Latn", "hu");

    public static readonly TargetLanguage Italian =
        new("it", "Italian", "ita_Latn", "it");

    public static readonly TargetLanguage Japanese =
        new("ja", "Japanese", "jpn_Jpan", "ja");

    public static readonly TargetLanguage Korean =
        new("ko", "Korean", "kor_Hang", "ko");

    public static readonly TargetLanguage Polish =
        new("pl", "Polish", "pol_Latn", "pl");

    public static readonly TargetLanguage Portuguese =
        new("pt", "Portuguese", "por_Latn", "pt");

    public static readonly TargetLanguage Spanish =
        new("es", "Spanish", "spa_Latn", "es");

    public static readonly TargetLanguage Turkish =
        new("tr", "Turkish", "tur_Latn", "tr");

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
