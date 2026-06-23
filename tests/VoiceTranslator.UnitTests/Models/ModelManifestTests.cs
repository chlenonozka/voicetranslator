using FluentAssertions;
using VoiceTranslator.Domain.Models;

namespace VoiceTranslator.UnitTests.Models;

public sealed class ModelManifestTests
{
    [Fact]
    public void NoncommercialManifestsRequireAcknowledgement()
    {
        var manifest = new ModelManifest(
            "nllb-600m",
            "facebook/nllb-200-distilled-600M",
            "f8d333a098d19b4fd9a8b18f94170487ad3f821d",
            "CC-BY-NC-4.0",
            CommercialUseAllowed: false,
            ExpectedFiles: []);

        manifest.RequiresNoncommercialAcknowledgement.Should().BeTrue();
    }

    [Fact]
    public void ManifestValidationRejectsMissingPinnedRevision()
    {
        var manifest = new ModelManifest(
            "xtts-v2",
            "coqui/XTTS-v2",
            "",
            "Coqui-Public-Model-License",
            CommercialUseAllowed: false,
            ExpectedFiles: []);

        Action validate = manifest.Validate;

        validate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Revision*");
    }
}
