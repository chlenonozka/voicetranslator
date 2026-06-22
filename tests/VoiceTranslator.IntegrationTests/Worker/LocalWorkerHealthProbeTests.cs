using FluentAssertions;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Infrastructure.LocalWorker;

namespace VoiceTranslator.IntegrationTests.Worker;

public sealed class LocalWorkerHealthProbeTests
{
    [Fact]
    public async Task WaitUntilReadyAsyncCreatesAuthenticatedClientAndDisposesIt()
    {
        var localWorker = new FakeLocalWorker();
        Uri? capturedEndpoint = null;
        string? capturedToken = null;
        var probe = new LocalWorkerHealthProbe(
            (endpoint, token) =>
            {
                capturedEndpoint = endpoint;
                capturedToken = token;
                return localWorker;
            });
        var endpoint = new Uri("http://127.0.0.1:8765");

        await probe.WaitUntilReadyAsync(
            endpoint,
            "launch-token",
            CancellationToken.None);

        capturedEndpoint.Should().Be(endpoint);
        capturedToken.Should().Be("launch-token");
        localWorker.WaitCallCount.Should().Be(1);
        localWorker.Disposed.Should().BeTrue();
    }

    private sealed class FakeLocalWorker : ILocalWorker
    {
        public int WaitCallCount { get; private set; }
        public bool Disposed { get; private set; }

        public Task CheckHealthAsync(
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task WaitUntilReadyAsync(
            CancellationToken cancellationToken)
        {
            WaitCallCount++;
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;
    }
}
