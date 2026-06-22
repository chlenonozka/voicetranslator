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

    public DateTimeOffset? SpeakerConsentAt { get; private set; }

    public static TranslationSession Create(TargetLanguage targetLanguage)
    {
        ArgumentNullException.ThrowIfNull(targetLanguage);
        return new TranslationSession(targetLanguage);
    }

    public void GrantSpeakerConsent(DateTimeOffset acceptedAt)
    {
        SpeakerConsentAt = acceptedAt;
    }

    public void MarkReady()
    {
        State = SessionState.Ready;
    }

    public void Start()
    {
        if (SpeakerConsentAt is null)
        {
            throw new InvalidOperationException(
                "Speaker consent is required before starting.");
        }

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
