using VoiceTranslator.Domain.Languages;

namespace VoiceTranslator.Domain.Models;

public sealed record CapabilityCatalogEntry(
    TargetLanguage TargetLanguage,
    bool TranslationReady,
    bool SynthesisReady,
    TimeSpan? P90Latency)
{
    public bool Available => TranslationReady && SynthesisReady;
}

public sealed class CapabilityCatalog
{
    private readonly IReadOnlyList<CapabilityCatalogEntry> entries;

    public CapabilityCatalog(IEnumerable<CapabilityCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        this.entries = entries.ToArray();
    }

    public IReadOnlyList<CapabilityCatalogEntry> Entries => entries;

    public IReadOnlyList<TargetLanguage> AvailableLanguages =>
        entries
            .Where(entry => entry.Available)
            .Select(entry => entry.TargetLanguage)
            .ToArray();
}
