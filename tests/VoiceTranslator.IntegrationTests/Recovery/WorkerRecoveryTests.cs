using System.Diagnostics;
using FluentAssertions;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Infrastructure.LocalWorker;

namespace VoiceTranslator.IntegrationTests.Recovery;

public sealed class WorkerRecoveryTests
{
    [Fact]
    public async Task WorkerProcessExitFlowsThroughManagerAndStopsSession()
    {
        var session = new FakeSessionStopper();
        var coordinator = new SessionFailureCoordinator(session);
        var process = new ControllableWorkerProcess(42);
        var launcher = new PythonWorkerLauncher(
            new SingleProcessStarter(process),
            "python",
            "worker");
        await using var manager = new WorkerProcessManager(
            launcher,
            new MonitoringHealthProbe(),
            failureObserver: coordinator,
            heartbeatInterval: TimeSpan.FromSeconds(30));

        await manager.StartAsync(CancellationToken.None);
        process.Exit();
        await WaitUntilAsync(
            () => coordinator.State
                == SessionFailureState.RestartRequired);

        session.StopCount.Should().Be(1);
        coordinator.State.Should().Be(SessionFailureState.RestartRequired);
        coordinator.Failure.Should().Be(SessionFailure.WorkerExited);
    }

    [Fact]
    public async Task ManagerHeartbeatTimeoutStopsSessionWhenProbeIgnoresCancellation()
    {
        var session = new FakeSessionStopper();
        var coordinator = new SessionFailureCoordinator(
            session,
            stopTimeout: TimeSpan.FromMilliseconds(100));
        var launcher = new HangingWorkerLauncher();
        var healthProbe = new MonitoringHealthProbe
        {
            HangHeartbeatIgnoringCancellation = true,
        };
        await using var manager = new WorkerProcessManager(
            launcher,
            healthProbe,
            failureObserver: coordinator,
            heartbeatInterval: TimeSpan.Zero,
            heartbeatTimeout: TimeSpan.FromMilliseconds(100));

        await manager.StartAsync(CancellationToken.None);
        await WaitUntilAsync(
            () => coordinator.State
                == SessionFailureState.RestartRequired);

        session.StopCount.Should().Be(1);
        coordinator.Failure.Should().Be(SessionFailure.HeartbeatTimedOut);
    }

    [Fact]
    public async Task HeartbeatTimeoutStopsSessionWithinDeadline()
    {
        var session = new FakeSessionStopper();
        var coordinator = new SessionFailureCoordinator(
            session,
            heartbeatTimeout: TimeSpan.FromMilliseconds(100));
        var stopwatch = Stopwatch.StartNew();

        bool healthy = await coordinator.CheckHeartbeatAsync(
            _ => Task.Delay(
                TimeSpan.FromMilliseconds(500),
                CancellationToken.None),
            CancellationToken.None);

        stopwatch.Stop();
        healthy.Should().BeFalse();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        session.StopCount.Should().Be(1);
        coordinator.Failure.Should().Be(SessionFailure.HeartbeatTimedOut);
        coordinator.State.Should().Be(SessionFailureState.RestartRequired);
    }

    [Fact]
    public async Task GpuMemoryExhaustionStopsSessionAndRequiresRestart()
    {
        var session = new FakeSessionStopper();
        var coordinator = new SessionFailureCoordinator(session);

        await coordinator.ReportGpuMemoryExhaustionAsync(
            CancellationToken.None);

        session.StopCount.Should().Be(1);
        coordinator.Failure.Should().Be(SessionFailure.GpuMemoryExhausted);
        coordinator.State.Should().Be(SessionFailureState.RestartRequired);
    }

    [Fact]
    public void DefaultFailureStopTimeoutIsTwoSeconds()
    {
        SessionFailureCoordinator.DefaultStopTimeout.Should()
            .Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void DefaultManagerHeartbeatTimeoutIsTwoSeconds()
    {
        WorkerProcessManager.DefaultHeartbeatTimeout.Should()
            .Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task FailureHandlingIsBoundedWhenStopperHangs()
    {
        var session = new FakeSessionStopper
        {
            Hang = true,
        };
        var coordinator = new SessionFailureCoordinator(
            session,
            stopTimeout: TimeSpan.FromMilliseconds(100));
        var stopwatch = Stopwatch.StartNew();

        await coordinator.ReportGpuMemoryExhaustionAsync(
            CancellationToken.None);

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        coordinator.State.Should().Be(SessionFailureState.RestartRequired);
        coordinator.Failure.Should().Be(SessionFailure.GpuMemoryExhausted);
    }

    [Fact]
    public async Task FailureHandlingIsBoundedWhenPlaybackStopBlocks()
    {
        using var output = new BlockingPlaybackSink();
        using var pipeline = new TranslationPipeline(
            new PassthroughWorker(),
            output,
            queueCapacity: 1);
        var coordinator = new SessionFailureCoordinator(
            pipeline,
            stopTimeout: TimeSpan.FromMilliseconds(100));
        Task handling = Task.Run(
            () => coordinator.OnSessionFailureAsync(
                SessionFailure.WorkerExited,
                CancellationToken.None));

        try
        {
            await handling.WaitAsync(TimeSpan.FromSeconds(1));

            coordinator.State.Should()
                .Be(SessionFailureState.RestartRequired);
        }
        finally
        {
            output.Release();
            await handling.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    private sealed class FakeSessionStopper : ISessionStopper
    {
        public int StopCount { get; private set; }
        public bool Hang { get; init; }

        public Task StopSessionAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            if (Hang)
            {
                return Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    CancellationToken.None);
            }

            return Task.CompletedTask;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class SingleProcessStarter(
        IWorkerProcess process) : IWorkerProcessStarter
    {
        public IWorkerProcess Start(WorkerProcessOptions options)
        {
            return process;
        }
    }

    private sealed class ControllableWorkerProcess(int id) : IWorkerProcess
    {
        private readonly TaskCompletionSource exited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Id { get; } = id;
        public bool HasExited { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            return exited.Task.WaitAsync(cancellationToken);
        }

        public void Exit()
        {
            HasExited = true;
            exited.TrySetResult();
        }

        public void KillTree()
        {
            Exit();
        }

        public void Dispose()
        {
        }
    }

    private sealed class MonitoringHealthProbe : IWorkerHealthProbe
    {
        public bool HangHeartbeatIgnoringCancellation { get; init; }

        public Task WaitUntilReadyAsync(
            Uri endpoint,
            string token,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task CheckHealthAsync(
            Uri endpoint,
            string token,
            CancellationToken cancellationToken)
        {
            return HangHeartbeatIgnoringCancellation
                ? Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    CancellationToken.None)
                : Task.CompletedTask;
        }
    }

    private sealed class HangingWorkerLauncher : IWorkerLauncher
    {
        private readonly TaskCompletionSource exited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LaunchedWorker> LaunchAsync(
            WorkerLaunchRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new LaunchedWorker(
                    1,
                    request.Endpoint,
                    "balanced"));
        }

        public Task WaitForExitAsync(
            LaunchedWorker worker,
            CancellationToken cancellationToken)
        {
            return exited.Task.WaitAsync(cancellationToken);
        }

        public Task StopAsync(
            LaunchedWorker worker,
            CancellationToken cancellationToken)
        {
            exited.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class PassthroughWorker : IPhraseTranslationWorker
    {
        public Task<byte[]> TranslateAsync(
            Phrase phrase,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(phrase.Pcm16);
        }
    }

    private sealed class BlockingPlaybackSink : IAudioPlaybackSink, IDisposable
    {
        private readonly ManualResetEventSlim released = new();

        public void Play(byte[] pcm)
        {
        }

        public void StopPlayback()
        {
            released.Wait();
        }

        public void Release()
        {
            released.Set();
        }

        public void Dispose()
        {
            released.Dispose();
        }
    }
}
