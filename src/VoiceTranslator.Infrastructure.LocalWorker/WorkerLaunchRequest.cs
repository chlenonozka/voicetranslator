namespace VoiceTranslator.Infrastructure.LocalWorker;

public sealed record WorkerLaunchRequest(
    Uri Endpoint,
    string Token);
