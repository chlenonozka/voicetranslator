using FluentAssertions;
using VoiceTranslator.Infrastructure.LocalWorker.Models;

namespace VoiceTranslator.UnitTests.Models;

public sealed class ModelInventoryClientTests
{
    [Fact]
    public async Task InventoryReadsPinnedManifestsAndDetectsInstalledReceipts()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        string manifests = Path.Combine(root, "models", "manifests");
        string cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(manifests);
        Directory.CreateDirectory(Path.Combine(cache, "nllb-600m"));
        await File.WriteAllTextAsync(
            Path.Combine(manifests, "nllb-600m.json"),
            """
            {
              "id": "nllb-600m",
              "repo_id": "facebook/nllb-200-distilled-600M",
              "revision": "f8d333a098d19b4fd9a8b18f94170487ad3f821d",
              "license": "CC-BY-NC-4.0",
              "commercial_use_allowed": false,
              "files": []
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(cache, "nllb-600m", "receipt.json"),
            "{}");
        var client = new ManifestModelInventoryClient(manifests, cache);

        var inventory = await client.GetInventoryAsync(
            CancellationToken.None);

        var item = inventory.Models.Should().ContainSingle().Subject;
        item.Manifest.ModelId.Should().Be("nllb-600m");
        item.Manifest.Revision.Should().HaveLength(40);
        item.Manifest.RequiresNoncommercialAcknowledgement.Should().BeTrue();
        item.Installed.Should().BeTrue();
        inventory.MissingModelIds.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadRequiresNoncommercialAcknowledgement()
    {
        var client = new ManifestModelInventoryClient(
            Path.GetTempPath(),
            Path.GetTempPath());

        Func<Task> download = async () =>
        {
            await foreach (var _ in client.DownloadModelsAsync(
                acceptNoncommercial: false,
                CancellationToken.None))
            {
            }
        };

        await download.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*acknowledgement*");
    }
}
