using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.Infrastructure.LocalWorker;

public sealed class LocalWorkerClient : ILocalWorker
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
        }
    }

    public void Dispose() => httpClient.Dispose();
}
