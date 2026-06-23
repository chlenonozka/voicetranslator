namespace VoiceTranslator.Infrastructure.LocalWorker;

public interface IWorkerLauncher
{
    Task<LaunchedWorker> LaunchAsync(
        WorkerLaunchRequest request,
        CancellationToken cancellationToken);

    Task StopAsync(
        LaunchedWorker worker,
        CancellationToken cancellationToken);

    Task WaitForExitAsync(
        LaunchedWorker worker,
        CancellationToken cancellationToken);
}
