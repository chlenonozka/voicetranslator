namespace VoiceTranslator.Infrastructure.LocalWorker;

public sealed record LaunchedWorker(
    int ProcessId,
    Uri Endpoint,
    string PerformanceProfile);
