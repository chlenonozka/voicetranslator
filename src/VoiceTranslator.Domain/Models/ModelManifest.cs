namespace VoiceTranslator.Domain.Models;

public sealed record ModelManifest(
    string ModelId,
    string RepositoryId,
    string Revision,
    string License,
    bool CommercialUseAllowed,
    IReadOnlyList<string> ExpectedFiles)
{
    public bool RequiresNoncommercialAcknowledgement =>
        !CommercialUseAllowed;

    public void Validate()
    {
        RequireValue(ModelId, nameof(ModelId));
        RequireValue(RepositoryId, nameof(RepositoryId));
        RequireValue(Revision, nameof(Revision));
        RequireValue(License, nameof(License));
    }

    private static void RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Model manifest field {name} is required.");
        }
    }
}
