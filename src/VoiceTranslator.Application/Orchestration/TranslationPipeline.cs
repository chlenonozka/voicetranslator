using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.Application.Orchestration;

public sealed class TranslationPipeline : IDisposable, ISessionStopper
{
    private readonly IPhraseTranslationWorker worker;
    private readonly IAudioPlaybackSink output;
    private readonly BoundedPhraseQueue queue;
    private readonly ISessionFailureObserver? failureObserver;
    private readonly SemaphoreSlim consumer = new(1, 1);
    private readonly object sessionGate = new();
    private readonly CancellationTokenSource sessionCancellation = new();
    private long sessionGeneration;
    private int activeOutputCalls;
    private TaskCompletionSource? outputIdle;
    private Task? stopPlaybackTask;
    private bool stopped;

    public TranslationPipeline(
        IPhraseTranslationWorker worker,
        IAudioPlaybackSink output,
        int queueCapacity,
        ISessionFailureObserver? failureObserver = null)
    {
        this.worker = worker;
        this.output = output;
        queue = new BoundedPhraseQueue(queueCapacity);
        this.failureObserver = failureObserver;
    }

    public void Enqueue(Phrase phrase) => queue.Enqueue(phrase);

    public async Task ProcessQueuedAsync(
        CancellationToken cancellationToken)
    {
        await consumer
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            CancellationToken sessionToken;
            long generation;
            lock (sessionGate)
            {
                if (stopped)
                {
                    return;
                }

                sessionToken = sessionCancellation.Token;
                generation = sessionGeneration;
            }

            using var linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    sessionToken);

            while (queue.TryDequeue(out var phrase))
            {
                byte[] translated;
                try
                {
                    translated = await worker
                        .TranslateAsync(
                            phrase,
                            linkedCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (sessionToken.IsCancellationRequested
                        && !cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.InsufficientStorage)
                {
                    if (failureObserver != null)
                    {
                        await failureObserver.OnSessionFailureAsync(SessionFailure.GpuMemoryExhausted, cancellationToken).ConfigureAwait(false);
                    }
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!TryReserveOutput(generation))
                {
                    return;
                }

                try
                {
                    await output
                        .PlayAsync(
                            translated,
                            linkedCancellation.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    ReleaseOutput();
                }
            }
        }
        finally
        {
            consumer.Release();
        }
    }

    public void Stop()
    {
        StopSessionCoreAsync().GetAwaiter().GetResult();
    }

    public Task StopSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return StopSessionCoreAsync();
    }

    private async Task StopSessionCoreAsync()
    {
        bool initiateStop;
        lock (sessionGate)
        {
            initiateStop = !stopped;
            if (initiateStop)
            {
                stopped = true;
                sessionGeneration++;
            }
        }

        if (initiateStop)
        {
            sessionCancellation.Cancel();
            queue.Clear();
        }

        await WaitForOutputIdleAsync().ConfigureAwait(false);

        Task playbackStop;
        TaskCompletionSource? playbackStopCompletion = null;
        lock (sessionGate)
        {
            if (stopPlaybackTask is null)
            {
                playbackStopCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                stopPlaybackTask = playbackStopCompletion.Task;
            }
            playbackStop = stopPlaybackTask;
        }

        if (playbackStopCompletion is not null)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    output.StopPlayback();
                    playbackStopCompletion.TrySetResult();
                }
                catch (Exception error)
                {
                    playbackStopCompletion.TrySetException(error);
                }
            });
        }

        await playbackStop.ConfigureAwait(false);
    }

    private bool TryReserveOutput(long generation)
    {
        lock (sessionGate)
        {
            if (stopped || generation != sessionGeneration)
            {
                return false;
            }

            activeOutputCalls++;
            if (activeOutputCalls == 1)
            {
                outputIdle = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return true;
        }
    }

    private void ReleaseOutput()
    {
        TaskCompletionSource? completed = null;
        lock (sessionGate)
        {
            activeOutputCalls--;
            if (activeOutputCalls == 0)
            {
                completed = outputIdle;
                outputIdle = null;
            }
        }

        completed?.TrySetResult();
    }

    private Task WaitForOutputIdleAsync()
    {
        lock (sessionGate)
        {
            return activeOutputCalls == 0
                ? Task.CompletedTask
                : outputIdle!.Task;
        }
    }

    public void Dispose()
    {
        Stop();
        sessionCancellation.Dispose();
        consumer.Dispose();
    }
}
