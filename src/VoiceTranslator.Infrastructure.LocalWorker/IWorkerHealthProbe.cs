namespace VoiceTranslator.Infrastructure.LocalWorker;

public interface IWorkerHealthProbe
{
    Task WaitUntilReadyAsync(
        Uri endpoint,
        string token,
        CancellationToken cancellationToken);
}
