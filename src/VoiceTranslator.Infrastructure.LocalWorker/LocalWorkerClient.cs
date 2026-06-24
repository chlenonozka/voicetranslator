using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.Infrastructure.LocalWorker;

public sealed class LocalWorkerClient : ILocalTranslationWorker
{
    private static readonly TimeSpan DefaultRetryDelay =
        TimeSpan.FromMilliseconds(100);
    private readonly HttpClient httpClient;
    private readonly TimeSpan retryDelay;

    public LocalWorkerClient(
        HttpClient httpClient,
        Uri endpoint,
        string token,
        TimeSpan? retryDelay = null)
    {
        this.httpClient = httpClient;
        this.retryDelay = retryDelay ?? DefaultRetryDelay;
        this.httpClient.BaseAddress = endpoint;
        this.httpClient.DefaultRequestHeaders.Add(
            "X-Worker-Token",
            token);
    }

    public async Task CheckHealthAsync(
        CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .GetAsync("v1/health", cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task WaitUntilReadyAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await CheckHealthAsync(cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (HttpRequestException)
            {
                await Task
                    .Delay(retryDelay, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TaskCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                await Task
                    .Delay(retryDelay, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public async Task<WorkerPreflightReport> PreflightAsync(
        CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .PostAsync(
                "v1/preflight",
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureWorkerSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadFromJsonAsync<WorkerPreflightReport>(
                cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Worker returned an empty preflight response.");
    }

    public async Task<Guid> CreateSpeakerSessionAsync(
        byte[] referenceWav,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(referenceWav);
        using var content = new ByteArrayContent(referenceWav);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var response = await httpClient
            .PostAsync(
                "v1/speaker-sessions",
                content,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureWorkerSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        var payload = await response.Content
            .ReadFromJsonAsync<SpeakerSessionResponse>(
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return payload?.SessionId
            ?? throw new InvalidOperationException(
                "Worker returned an empty speaker session response.");
    }

    public async Task DeleteSpeakerSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .DeleteAsync(
                $"v1/speaker-sessions/{sessionId:D}",
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureWorkerSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PhraseTranslationResult> TranslatePhraseAsync(
        Guid sessionId,
        string targetLanguage,
        byte[] phraseWav,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);
        ArgumentNullException.ThrowIfNull(phraseWav);

        using var content = new MultipartFormDataContent();
        content.Add(
            new StringContent(sessionId.ToString("D")),
            "sessionId");
        content.Add(new StringContent(targetLanguage), "targetLanguage");
        var audio = new ByteArrayContent(phraseWav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "audio", "phrase.wav");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v1/translate-phrase")
        {
            Content = content,
        };
        request.Headers.Add("X-Request-Id", requestId.ToString("D"));

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureWorkerSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        var wav = await response.Content
            .ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PhraseTranslationResult(
            RequestId: ReadGuidHeader(response, "X-Request-Id"),
            AudioWav: wav,
            AsrMilliseconds: ReadDoubleHeader(response, "X-Asr-Ms"),
            TranslateMilliseconds:
                ReadDoubleHeader(response, "X-Translate-Ms"),
            SynthesizeMilliseconds:
                ReadDoubleHeader(response, "X-Synthesize-Ms"),
            PerformanceProfile:
                ReadHeader(response, "X-Performance-Profile"));
    }

    public async Task CancelAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .PostAsync(
                $"v1/cancel/{requestId:D}",
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureWorkerSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose() => httpClient.Dispose();

    private static string ReadHeader(
        HttpResponseMessage response,
        string name)
    {
        return response.Headers.TryGetValues(name, out var values)
            ? values.Single()
            : throw new InvalidOperationException(
                $"Worker response is missing {name}.");
    }

    private static async Task EnsureWorkerSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        string reason = string.IsNullOrWhiteSpace(body)
            ? response.ReasonPhrase ?? "Worker request failed."
            : body;
        throw new HttpRequestException(
            $"Worker request failed with {(int)response.StatusCode} "
            + $"{response.StatusCode}: {reason}",
            inner: null,
            response.StatusCode);
    }

    private static Guid ReadGuidHeader(
        HttpResponseMessage response,
        string name)
    {
        return Guid.Parse(ReadHeader(response, name));
    }

    private static double ReadDoubleHeader(
        HttpResponseMessage response,
        string name)
    {
        return double.Parse(
            ReadHeader(response, name),
            CultureInfo.InvariantCulture);
    }

    private sealed record SpeakerSessionResponse(
        [property: JsonPropertyName("sessionId")] Guid SessionId);
}
