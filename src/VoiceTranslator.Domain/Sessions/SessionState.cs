namespace VoiceTranslator.Domain.Sessions;

public enum SessionState
{
    Draft,
    Ready,
    Listening,
    Faulted,
    Stopped,
}
