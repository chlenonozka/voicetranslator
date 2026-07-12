using FluentAssertions;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Infrastructure.LocalWorker;

namespace VoiceTranslator.IntegrationTests.Worker;

public sealed class WorkerProcessManagerTests
{
    [Fact]
    public async Task StartAsyncGeneratesANewTokenForEachProcess()
    {
        var launcher = new FakeWorkerLauncher();
        var healthProbe = new FakeWorkerHealthProbe();
        await using var manager = new WorkerProcessManager(
            launcher,
            healthProbe);

        var first = await manager.StartAsync(CancellationToken.None);
        await manager.StopAsync(CancellationToken.None);
        var second = await manager.StartAsync(CancellationToken.None);

        first.Token.Should().NotBe(second.Token);
        first.Token.Should().HaveLength(64);
        second.Token.Should().HaveLength(64);
    }

    [Fact]
    public async Task StartAsyncWaitsForAuthenticatedReadiness()
    {
        var launcher = new FakeWorkerLauncher();
        var healthProbe = new FakeWorkerHealthProbe();
        await using var manager = new WorkerProcessManager(
            launcher,
            healthProbe);

        var handle = await manager.StartAsync(CancellationToken.None);

        healthProbe.Endpoint.Should().Be(handle.Endpoint);
        healthProbe.Token.Should().Be(handle.Token);
        healthProbe.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsyncCanDelayHeartbeatMonitoringUntilAfterPreflight()
    {
        var launcher = new FakeWorkerLauncher();
        var healthProbe = new FakeWorkerHealthProbe();
        await using var manager = new WorkerProcessManager(
            launcher,
            healthProbe,
            failureObserver: new RecordingFailureObserver(),
            heartbeatInterval: TimeSpan.FromMilliseconds(10),
            startMonitoringOnStart: false);

        WorkerHandle handle = await manager.StartAsync(
            CancellationToken.None);

        healthProbe.HealthCheckCount.Should().Be(0);

        manager.StartMonitoring(handle);
        await healthProbe.WaitForHealthCheckAsync();

        healthProbe.HealthCheckCount.Should().BeGreaterThan(0);
    }

    private sealed class FakeWorkerLauncher : IWorkerLauncher
    {
        private int nextProcessId = 1;

        public Task<LaunchedWorker> LaunchAsync(
            WorkerLaunchRequest request,
            CancellationToken cancellationToken)
        {
            var worker = new LaunchedWorker(
                nextProcessId++,
                request.Endpoint,
                "balanced");
            return Task.FromResult(worker);
        }

        public Task StopAsync(
            LaunchedWorker worker,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task WaitForExitAsync(
            LaunchedWorker worker,
            CancellationToken cancellationToken) =>
            new TaskCompletionSource().Task.WaitAsync(cancellationToken);
    }

    private sealed class FakeWorkerHealthProbe : IWorkerHealthProbe
    {
        public Uri? Endpoint { get; private set; }
        public string? Token { get; private set; }
        public int CallCount { get; private set; }
        public int HealthCheckCount { get; private set; }
        private readonly TaskCompletionSource healthChecked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilReadyAsync(
            Uri endpoint,
            string token,
            CancellationToken cancellationToken)
        {
            Endpoint = endpoint;
            Token = token;
            CallCount++;
            return Task.CompletedTask;
        }

        public Task CheckHealthAsync(
            Uri endpoint,
            string token,
            CancellationToken cancellationToken)
        {
            HealthCheckCount++;
            healthChecked.TrySetResult();
            return Task.CompletedTask;
        }

        public Task WaitForHealthCheckAsync() =>
            healthChecked.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class RecordingFailureObserver : ISessionFailureObserver
    {
        public Task OnSessionFailureAsync(
            SessionFailure failure,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
