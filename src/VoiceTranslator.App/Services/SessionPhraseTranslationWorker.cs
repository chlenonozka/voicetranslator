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

    public SessionPhraseTranslationWorker(
        ILocalTranslationWorker worker,
        Guid sessionId,
        string targetLanguage)
    {
        this.worker = worker;
        this.sessionId = sessionId;
        this.targetLanguage = targetLanguage;
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
                cancellationToken)
            .ConfigureAwait(false);
        return WaveMemoryCodec.DecodeSynthesizedWave(result.AudioWav);
    }
}
