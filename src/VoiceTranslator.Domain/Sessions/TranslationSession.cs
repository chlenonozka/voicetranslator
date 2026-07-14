using VoiceTranslator.Domain.Languages;

namespace VoiceTranslator.Domain.Sessions;

public sealed class TranslationSession
{
    private TranslationSession(TargetLanguage targetLanguage)
    {
        TargetLanguage = targetLanguage;
    }

    public TargetLanguage TargetLanguage { get; }

    public SessionState State { get; private set; } = SessionState.Draft;

    public static TranslationSession Create(TargetLanguage targetLanguage)
    {
        ArgumentNullException.ThrowIfNull(targetLanguage);
        return new TranslationSession(targetLanguage);
    }

    public void MarkReady()
    {
        State = SessionState.Ready;
    }

    public void Start()
    {
        if (State != SessionState.Ready)
        {
            throw new InvalidOperationException("Session is not ready.");
        }

        State = SessionState.Listening;
    }

    public void Stop()
    {
        State = SessionState.Stopped;
    }
}
