namespace VoiceTranslator.Application.Orchestration;

public sealed record Phrase(
    string Id,
    byte[] Pcm16);
