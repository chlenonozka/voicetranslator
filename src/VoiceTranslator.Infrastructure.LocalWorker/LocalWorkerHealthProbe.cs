using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.Infrastructure.LocalWorker;

public sealed class LocalWorkerHealthProbe(
    Func<Uri, string, ILocalWorker> clientFactory) : IWorkerHealthProbe
{
    public async Task WaitUntilReadyAsync(
        Uri endpoint,
        string token,
        CancellationToken cancellationToken)
    {
        using var client = clientFactory(endpoint, token);
        await client
            .WaitUntilReadyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CheckHealthAsync(
        Uri endpoint,
        string token,
        CancellationToken cancellationToken)
    {
        using var client = clientFactory(endpoint, token);
        await client
            .CheckHealthAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
