using VoiceTranslator.Application.Orchestration;

namespace VoiceTranslator.Application.Ports;

public interface IPhraseTranslationWorker
{
    Task<byte[]> TranslateAsync(
        Phrase phrase,
        CancellationToken cancellationToken);
}
