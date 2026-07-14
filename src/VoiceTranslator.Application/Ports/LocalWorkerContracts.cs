namespace VoiceTranslator.Application.Ports;

public sealed record WorkerPreflightReport(
    bool Ready,
    bool CudaAvailable,
    string? DeviceName,
    long TotalVramBytes,
    long FreeVramBytes,
    string PerformanceProfile,
    IReadOnlyList<string> MissingModels,
    IReadOnlyList<string> AvailableLanguages);

public sealed record PhraseTranslationResult(
    Guid RequestId,
    byte[] AudioWav,
    double AsrMilliseconds,
    double TranslateMilliseconds,
    double SynthesizeMilliseconds,
    string PerformanceProfile);

public interface ILocalTranslationWorker : ILocalWorker
{
    Task<WorkerPreflightReport> PreflightAsync(
        CancellationToken cancellationToken);

    Task<Guid> CreateSpeakerSessionAsync(
        byte[] referenceWav,
        CancellationToken cancellationToken);

    Task<Guid> CreateSpeakerSessionAsync(
        byte[] referenceWav,
        string performanceProfile,
        CancellationToken cancellationToken) =>
        CreateSpeakerSessionAsync(referenceWav, cancellationToken);

    Task DeleteSpeakerSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<PhraseTranslationResult> TranslatePhraseAsync(
        Guid sessionId,
        string targetLanguage,
        byte[] phraseWav,
        Guid requestId,
        CancellationToken cancellationToken);

    Task<PhraseTranslationResult> TranslatePhraseAsync(
        Guid sessionId,
        string targetLanguage,
        byte[] phraseWav,
        Guid requestId,
        string performanceProfile,
        CancellationToken cancellationToken) =>
        TranslatePhraseAsync(
            sessionId,
            targetLanguage,
            phraseWav,
            requestId,
            cancellationToken);

    Task CancelAsync(
        Guid requestId,
        CancellationToken cancellationToken);
}
