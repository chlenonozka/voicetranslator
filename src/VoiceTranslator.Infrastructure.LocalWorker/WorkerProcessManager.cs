using System.Security.Cryptography;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.Infrastructure.LocalWorker;

public sealed class WorkerProcessManager : IAsyncDisposable
{
    private static readonly Uri DefaultEndpoint = new("http://127.0.0.1:8765");
    private static readonly TimeSpan DefaultHeartbeatInterval =
        TimeSpan.FromMilliseconds(500);

    public static TimeSpan DefaultHeartbeatTimeout { get; } =
        TimeSpan.FromSeconds(2);

    private readonly IWorkerLauncher launcher;
    private readonly IWorkerHealthProbe healthProbe;
    private readonly Uri endpoint;
    private readonly ISessionFailureObserver? failureObserver;
    private readonly TimeSpan heartbeatInterval;
    private readonly TimeSpan heartbeatTimeout;
    private LaunchedWorker? activeWorker;
    private CancellationTokenSource? monitorCancellation;
    private Task? monitorTask;

    public WorkerProcessManager(
        IWorkerLauncher launcher,
        IWorkerHealthProbe healthProbe,
        Uri? endpoint = null,
        ISessionFailureObserver? failureObserver = null,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? heartbeatTimeout = null)
    {
        this.launcher = launcher;
        this.healthProbe = healthProbe;
        this.endpoint = endpoint ?? DefaultEndpoint;
        this.failureObserver = failureObserver;
        this.heartbeatInterval =
            heartbeatInterval ?? DefaultHeartbeatInterval;
        this.heartbeatTimeout =
            heartbeatTimeout ?? DefaultHeartbeatTimeout;

        if (this.heartbeatInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heartbeatInterval));
        }

        if (this.heartbeatTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heartbeatTimeout));
        }
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
            StartMonitoring(activeWorker, handle);
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

        CancellationTokenSource? cancellation = monitorCancellation;
        Task? monitoring = monitorTask;
        monitorCancellation = null;
        monitorTask = null;
        if (cancellation is not null)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }

        await launcher
            .StopAsync(worker, cancellationToken)
            .ConfigureAwait(false);

        if (monitoring is not null)
        {
            try
            {
                await monitoring.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void StartMonitoring(
        LaunchedWorker worker,
        WorkerHandle handle)
    {
        if (failureObserver is null)
        {
            return;
        }

        monitorCancellation = new CancellationTokenSource();
        monitorTask = MonitorAsync(
            worker,
            handle,
            monitorCancellation.Token);
    }

    private async Task MonitorAsync(
        LaunchedWorker worker,
        WorkerHandle handle,
        CancellationToken cancellationToken)
    {
        Task<SessionFailure> exit = MonitorExitAsync(
            worker,
            cancellationToken);
        Task<SessionFailure> heartbeat = MonitorHeartbeatAsync(
            handle,
            cancellationToken);

        Task<SessionFailure> completed =
            await Task.WhenAny(exit, heartbeat).ConfigureAwait(false);
        SessionFailure failure = await completed.ConfigureAwait(false);

        if (monitorCancellation is not null)
        {
            await monitorCancellation.CancelAsync().ConfigureAwait(false);
        }

        await failureObserver!
            .OnSessionFailureAsync(failure, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task<SessionFailure> MonitorExitAsync(
        LaunchedWorker worker,
        CancellationToken cancellationToken)
    {
        await launcher
            .WaitForExitAsync(worker, cancellationToken)
            .ConfigureAwait(false);
        return SessionFailure.WorkerExited;
    }

    private async Task<SessionFailure> MonitorHeartbeatAsync(
        WorkerHandle handle,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (heartbeatInterval > TimeSpan.Zero)
            {
                await Task
                    .Delay(heartbeatInterval, cancellationToken)
                    .ConfigureAwait(false);
            }

            try
            {
                await healthProbe
                    .CheckHealthAsync(
                        handle.Endpoint,
                        handle.Token,
                        cancellationToken)
                    .WaitAsync(
                        heartbeatTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return SessionFailure.HeartbeatTimedOut;
            }
            catch (Exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                return SessionFailure.HeartbeatTimedOut;
            }
        }
    }
}
