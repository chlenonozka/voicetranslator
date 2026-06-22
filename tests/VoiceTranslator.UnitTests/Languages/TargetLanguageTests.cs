using FluentAssertions;
using VoiceTranslator.Domain.Languages;

namespace VoiceTranslator.UnitTests.Languages;

public sealed class TargetLanguageTests
{
    [Fact]
    public void CatalogContainsExactlyTheApprovedTargets()
    {
        TargetLanguage.All.Should().HaveCount(16);
        TargetLanguage.All.Should().Contain(TargetLanguage.English);
        TargetLanguage.All.Should().Contain(TargetLanguage.Hindi);
        TargetLanguage.All.Should().NotContain(language => language.Code == "ru");
    }

    [Theory]
    [InlineData("en", "eng_Latn", "en")]
    [InlineData("ar", "arb_Arab", "ar")]
    [InlineData("zh", "zho_Hans", "zh-cn")]
    [InlineData("hi", "hin_Deva", "hi")]
    public void CatalogMapsTranslationAndSynthesisCodes(
        string code,
        string nllbCode,
        string xttsCode)
    {
        TargetLanguage language =
            TargetLanguage.All.Single(candidate => candidate.Code == code);

        language.NllbCode.Should().Be(nllbCode);
        language.XttsCode.Should().Be(xttsCode);
    }
}
