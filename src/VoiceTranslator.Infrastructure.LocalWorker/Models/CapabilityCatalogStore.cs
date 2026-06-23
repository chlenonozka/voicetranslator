using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Models;

namespace VoiceTranslator.Infrastructure.LocalWorker.Models;

public sealed class CapabilityCatalogStore : ICapabilityCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
    private readonly string path;

    public CapabilityCatalogStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public async Task SaveAsync(
        CapabilityCatalog catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new CatalogPayload(
            catalog.Entries
                .Select(entry => new CatalogEntryPayload(
                    entry.TargetLanguage.Code,
                    entry.TranslationReady,
                    entry.SynthesisReady,
                    entry.P90Latency?.TotalMilliseconds))
                .ToArray());
        await using var stream = File.Create(path);
        await JsonSerializer
            .SerializeAsync(stream, payload, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CapabilityCatalog?> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var payload = await JsonSerializer
            .DeserializeAsync<CatalogPayload>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            return null;
        }

        var byCode = TargetLanguage.All.ToDictionary(
            language => language.Code,
            StringComparer.Ordinal);
        return new CapabilityCatalog(
            payload.Languages
                .Where(entry => byCode.ContainsKey(entry.Code))
                .Select(entry => new CapabilityCatalogEntry(
                    byCode[entry.Code],
                    entry.TranslationReady,
                    entry.SynthesisReady,
                    entry.P90LatencyMilliseconds is double milliseconds
                        ? TimeSpan.FromMilliseconds(milliseconds)
                        : null)));
    }

    private sealed record CatalogPayload(
        [property: JsonPropertyName("languages")] IReadOnlyList<CatalogEntryPayload> Languages);

    private sealed record CatalogEntryPayload(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("translationReady")] bool TranslationReady,
        [property: JsonPropertyName("synthesisReady")] bool SynthesisReady,
        [property: JsonPropertyName("p90LatencyMilliseconds")] double? P90LatencyMilliseconds);
}
