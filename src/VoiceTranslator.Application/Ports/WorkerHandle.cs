namespace VoiceTranslator.Application.Ports;

public sealed record WorkerHandle(
    int ProcessId,
    Uri Endpoint,
    string Token,
    string PerformanceProfile);
