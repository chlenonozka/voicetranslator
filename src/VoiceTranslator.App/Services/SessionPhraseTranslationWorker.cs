using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Infrastructure.Audio.Capture;

namespace VoiceTranslator.App.Services;

public sealed class SessionPhraseTranslationWorker
    : IPhraseTranslationWorker
{
    private readonly ILocalTranslationWorker worker;
    private readonly Guid sessionId;
    private readonly string targetLanguage;
    private readonly string performanceProfile;

    public SessionPhraseTranslationWorker(
        ILocalTranslationWorker worker,
        Guid sessionId,
        string targetLanguage,
        string performanceProfile = "balanced")
    {
        this.worker = worker;
        this.sessionId = sessionId;
        this.targetLanguage = targetLanguage;
        this.performanceProfile = performanceProfile;
    }

    public async Task<byte[]> TranslateAsync(
        Phrase phrase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phrase);

        Guid requestId = Guid.NewGuid();
        PhraseTranslationResult result = await worker
            .TranslatePhraseAsync(
                sessionId,
                targetLanguage,
                WaveMemoryCodec.EncodeWorkerWave(phrase.Pcm16),
                requestId,
                performanceProfile,
                cancellationToken)
            .ConfigureAwait(false);
        return WaveMemoryCodec.DecodeSynthesizedWave(result.AudioWav);
    }
}
