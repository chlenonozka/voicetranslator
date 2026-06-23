using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Models;

namespace VoiceTranslator.Application.Ports;

public sealed record ModelInventoryItem(
    ModelManifest Manifest,
    bool Installed,
    DateTimeOffset? InstalledAt);

public sealed record ModelInventoryReport(
    IReadOnlyList<ModelInventoryItem> Models)
{
    public IReadOnlyList<string> MissingModelIds =>
        Models
            .Where(model => !model.Installed)
            .Select(model => model.Manifest.ModelId)
            .ToArray();
}

public sealed record ModelDownloadProgress(
    string Stage,
    string? ModelId,
    double? Fraction);

public interface IModelInventoryClient
{
    Task<ModelInventoryReport> GetInventoryAsync(
        CancellationToken cancellationToken);

    IAsyncEnumerable<ModelDownloadProgress> DownloadModelsAsync(
        bool acceptNoncommercial,
        CancellationToken cancellationToken);
}

public interface ICapabilityCatalogStore
{
    Task SaveAsync(
        CapabilityCatalog catalog,
        CancellationToken cancellationToken);

    Task<CapabilityCatalog?> LoadAsync(CancellationToken cancellationToken);
}

public sealed record OutputChannelTestResult(
    bool Passed,
    string? Warning);
