using FluentAssertions;
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
    }

    private sealed class FakeWorkerHealthProbe : IWorkerHealthProbe
    {
        public Uri? Endpoint { get; private set; }
        public string? Token { get; private set; }
        public int CallCount { get; private set; }

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
    }
}
