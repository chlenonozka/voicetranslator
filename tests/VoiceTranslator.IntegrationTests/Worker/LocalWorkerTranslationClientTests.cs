using System.Net;
using System.Text;
using FluentAssertions;
using VoiceTranslator.Infrastructure.LocalWorker;

namespace VoiceTranslator.IntegrationTests.Worker;

public sealed class LocalWorkerTranslationClientTests
{
    [Fact]
    public async Task PreflightAsyncDeserializesCapabilityReport()
    {
        var handler = new QueueHandler(
            JsonResponse(
                """
                {
                  "ready": true,
                  "cudaAvailable": true,
                  "deviceName": "RTX 3070",
                  "totalVramBytes": 8589934592,
                  "freeVramBytes": 6442450944,
                  "performanceProfile": "balanced",
                  "missingModels": [],
                  "availableLanguages": ["en", "de"]
                }
                """));
        using var client = CreateClient(handler);

        var report = await client.PreflightAsync(CancellationToken.None);

        report.Ready.Should().BeTrue();
        report.DeviceName.Should().Be("RTX 3070");
        report.AvailableLanguages.Should().Equal("en", "de");
    }

    [Fact]
    public async Task TranslatePhraseAsyncSendsMultipartAndReadsTimingHeaders()
    {
        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        };
        response.Headers.Add("X-Request-Id", requestId.ToString());
        response.Headers.Add("X-Asr-Ms", "10.5");
        response.Headers.Add("X-Translate-Ms", "20.5");
        response.Headers.Add("X-Synthesize-Ms", "30.5");
        response.Headers.Add("X-Performance-Profile", "balanced");
        var handler = new QueueHandler(response);
        using var client = CreateClient(handler);

        var result = await client.TranslatePhraseAsync(
            sessionId,
            "en",
            [9, 8, 7],
            requestId,
            CancellationToken.None);

        result.AudioWav.Should().Equal(1, 2, 3);
        result.RequestId.Should().Be(requestId);
        result.AsrMilliseconds.Should().Be(10.5);
        handler.LastRequest!.Headers.GetValues("X-Request-Id")
            .Should().ContainSingle(requestId.ToString());
        handler.LastBody.Should().Contain(sessionId.ToString());
        handler.LastBody.Should().Contain("targetLanguage");
        handler.LastBody.Should().Contain("phrase.wav");
    }

    [Fact]
    public async Task TranslatePhraseAsyncIncludesWorkerErrorBody()
    {
        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var response = new HttpResponseMessage(
            HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                """{"detail":"XTTS failed to synthesize"}""",
                Encoding.UTF8,
                "application/json"),
        };
        using var client = CreateClient(new QueueHandler(response));

        Func<Task> translate = () => client.TranslatePhraseAsync(
            sessionId,
            "en",
            [9, 8, 7],
            requestId,
            CancellationToken.None);

        await translate.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*500*XTTS failed to synthesize*");
    }

    private static LocalWorkerClient CreateClient(
        HttpMessageHandler handler)
    {
        return new LocalWorkerClient(
            new HttpClient(handler),
            new Uri("http://127.0.0.1:8765"),
            "launch-token");
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"),
        };
    }

    private sealed class QueueHandler(
        params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses =
            new(responses);

        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content
                    .ReadAsStringAsync(cancellationToken);
            }
            return responses.Dequeue();
        }
    }
}
