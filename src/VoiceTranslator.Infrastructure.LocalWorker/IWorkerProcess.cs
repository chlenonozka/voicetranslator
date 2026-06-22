namespace VoiceTranslator.Infrastructure.LocalWorker;

public interface IWorkerProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void KillTree();
}
