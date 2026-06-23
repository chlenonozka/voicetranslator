namespace VoiceTranslator.Application.Orchestration;

public interface ISessionStopper
{
    Task StopSessionAsync(CancellationToken cancellationToken);
}

public interface ISessionFailureObserver
{
    Task OnSessionFailureAsync(
        SessionFailure failure,
        CancellationToken cancellationToken);
}

public enum SessionFailureState
{
    Healthy,
    RestartRequired,
}

public enum SessionFailure
{
    WorkerExited,
    HeartbeatTimedOut,
    GpuMemoryExhausted,
}

public sealed class SessionFailureCoordinator : ISessionFailureObserver
{
    public static TimeSpan DefaultHeartbeatTimeout { get; } =
        TimeSpan.FromSeconds(2);

    public static TimeSpan DefaultStopTimeout { get; } =
        TimeSpan.FromSeconds(2);

    private readonly ISessionStopper session;
    private readonly TimeSpan heartbeatTimeout;
    private readonly TimeSpan stopTimeout;
    private int failed;

    public SessionFailureCoordinator(
        ISessionStopper session,
        TimeSpan? heartbeatTimeout = null,
        TimeSpan? stopTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        this.session = session;
        this.heartbeatTimeout =
            heartbeatTimeout ?? DefaultHeartbeatTimeout;
        this.stopTimeout = stopTimeout ?? DefaultStopTimeout;

        if (this.heartbeatTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heartbeatTimeout));
        }

        if (this.stopTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stopTimeout));
        }
    }

    public SessionFailureState State { get; private set; } =
        SessionFailureState.Healthy;

    public SessionFailure? Failure { get; private set; }

    public async Task MonitorWorkerExitAsync(
        Func<CancellationToken, Task> waitForExit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(waitForExit);

        await waitForExit(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await FailAsync(
                SessionFailure.WorkerExited,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task ReportGpuMemoryExhaustionAsync(
        CancellationToken cancellationToken)
    {
        return FailAsync(
            SessionFailure.GpuMemoryExhausted,
            cancellationToken);
    }

    public Task OnSessionFailureAsync(
        SessionFailure failure,
        CancellationToken cancellationToken)
    {
        return FailAsync(failure, cancellationToken);
    }

    public async Task<bool> CheckHeartbeatAsync(
        Func<CancellationToken, Task> heartbeat,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        try
        {
            await heartbeat(linked.Token)
                .WaitAsync(heartbeatTimeout, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            await linked.CancelAsync().ConfigureAwait(false);
            await FailAsync(
                    SessionFailure.HeartbeatTimedOut,
                    cancellationToken)
                .ConfigureAwait(false);
            return false;
        }
    }

    private async Task FailAsync(
        SessionFailure failure,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref failed, 1) != 0)
        {
            return;
        }

        Failure = failure;
        State = SessionFailureState.RestartRequired;

        using var stopCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        try
        {
            await session
                .StopSessionAsync(stopCancellation.Token)
                .WaitAsync(stopTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await stopCancellation.CancelAsync().ConfigureAwait(false);
        }
    }
}
