using FluentAssertions;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Models;
using VoiceTranslator.Infrastructure.LocalWorker.Models;

namespace VoiceTranslator.UnitTests.Models;

public sealed class CapabilityCatalogStoreTests
{
    [Fact]
    public async Task StorePersistsOnlyCapabilityMetadata()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            "capabilities.json");
        var store = new CapabilityCatalogStore(path);
        var catalog = new CapabilityCatalog(
            [
                new CapabilityCatalogEntry(
                    TargetLanguage.English,
                    TranslationReady: true,
                    SynthesisReady: true,
                    P90Latency: TimeSpan.FromMilliseconds(2400)),
            ]);

        await store.SaveAsync(catalog, CancellationToken.None);

        string json = await File.ReadAllTextAsync(path);
        json.Should().Contain("\"code\": \"en\"");
        json.Should().NotContain("speech");
        json.Should().NotContain("audio");
        json.Should().NotContain("text");

        CapabilityCatalog? loaded = await store.LoadAsync(
            CancellationToken.None);
        loaded.Should().NotBeNull();
        loaded!.AvailableLanguages.Should().ContainSingle()
            .Which.Code.Should().Be("en");
    }
}
