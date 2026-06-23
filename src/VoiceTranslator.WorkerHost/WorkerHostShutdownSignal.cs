using VoiceTranslator.Application.Orchestration;

namespace VoiceTranslator.WorkerHost;

public sealed class WorkerHostShutdownSignal : ISessionStopper
{
    private readonly TaskCompletionSource completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completion => completion.Task;

    public Task StopSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        completion.TrySetResult();
        return Task.CompletedTask;
    }
}
