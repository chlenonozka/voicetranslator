namespace VoiceTranslator.Application.Ports;

public interface ILocalWorker : IDisposable
{
    Task CheckHealthAsync(CancellationToken cancellationToken);

    Task WaitUntilReadyAsync(CancellationToken cancellationToken);
}
