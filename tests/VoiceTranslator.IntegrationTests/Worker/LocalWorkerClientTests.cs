using System.Net;
using FluentAssertions;
using VoiceTranslator.Infrastructure.LocalWorker;

namespace VoiceTranslator.IntegrationTests.Worker;

public sealed class LocalWorkerClientTests
{
    [Fact]
    public async Task CheckHealthAsyncSendsTheLaunchToken()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler);
        using var client = new LocalWorkerClient(
            httpClient,
            new Uri("http://127.0.0.1:8765"),
            "launch-token");

        await client.CheckHealthAsync(CancellationToken.None);

        handler.WorkerToken.Should().Be("launch-token");
        handler.RequestUri.Should().Be(
            new Uri("http://127.0.0.1:8765/v1/health"));
    }

    [Fact]
    public async Task WaitUntilReadyAsyncRetriesUntilHealthSucceeds()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler);
        using var client = new LocalWorkerClient(
            httpClient,
            new Uri("http://127.0.0.1:8765"),
            "launch-token",
            TimeSpan.Zero);

        await client.WaitUntilReadyAsync(CancellationToken.None);

        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task WaitUntilReadyAsyncRetriesHttpClientTimeout()
    {
        var handler = new TimeoutThenOkHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new LocalWorkerClient(
            httpClient,
            new Uri("http://127.0.0.1:8765"),
            "launch-token",
            TimeSpan.Zero);

        await client.WaitUntilReadyAsync(CancellationToken.None);

        handler.CallCount.Should().Be(2);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> statusCodes;

        public RecordingHandler(params HttpStatusCode[] statusCodes)
        {
            this.statusCodes = new Queue<HttpStatusCode>(statusCodes);
        }

        public string? WorkerToken { get; private set; }
        public Uri? RequestUri { get; private set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            WorkerToken = request.Headers.GetValues("X-Worker-Token").Single();
            return Task.FromResult(
                new HttpResponseMessage(statusCodes.Dequeue())
                {
                    Content = new StringContent("""{"status":"ready"}"""),
                });
        }
    }

    private sealed class TimeoutThenOkHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                throw new TaskCanceledException(
                    "The request was canceled due to HttpClient.Timeout.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
