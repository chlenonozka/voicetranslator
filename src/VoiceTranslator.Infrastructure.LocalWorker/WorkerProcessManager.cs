using System.Security.Cryptography;
using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.Infrastructure.LocalWorker;

public sealed class WorkerProcessManager : IAsyncDisposable
{
    private static readonly Uri DefaultEndpoint = new("http://127.0.0.1:8765");
    private readonly IWorkerLauncher launcher;
    private readonly IWorkerHealthProbe healthProbe;
    private readonly Uri endpoint;
    private LaunchedWorker? activeWorker;

    public WorkerProcessManager(
        IWorkerLauncher launcher,
        IWorkerHealthProbe healthProbe,
        Uri? endpoint = null)
    {
        this.launcher = launcher;
        this.healthProbe = healthProbe;
        this.endpoint = endpoint ?? DefaultEndpoint;
    }

    public async Task<WorkerHandle> StartAsync(
        CancellationToken cancellationToken)
    {
        if (activeWorker is not null)
        {
            throw new InvalidOperationException("Worker is already running.");
        }

        var token = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));
        var request = new WorkerLaunchRequest(endpoint, token);
        activeWorker = await launcher
            .LaunchAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var handle = new WorkerHandle(
            activeWorker.ProcessId,
            activeWorker.Endpoint,
            token,
            activeWorker.PerformanceProfile);

        try
        {
            await healthProbe
                .WaitUntilReadyAsync(
                    handle.Endpoint,
                    handle.Token,
                    cancellationToken)
                .ConfigureAwait(false);
            return handle;
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (activeWorker is null)
        {
            return;
        }

        var worker = activeWorker;
        activeWorker = null;
        await launcher
            .StopAsync(worker, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
