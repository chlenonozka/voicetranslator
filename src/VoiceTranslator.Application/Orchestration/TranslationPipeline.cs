using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.Application.Orchestration;

public sealed class TranslationPipeline : IDisposable
{
    private readonly IPhraseTranslationWorker worker;
    private readonly IAudioPlaybackSink output;
    private readonly BoundedPhraseQueue queue;
    private readonly SemaphoreSlim consumer = new(1, 1);

    public TranslationPipeline(
        IPhraseTranslationWorker worker,
        IAudioPlaybackSink output,
        int queueCapacity)
    {
        this.worker = worker;
        this.output = output;
        queue = new BoundedPhraseQueue(queueCapacity);
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
            while (queue.TryDequeue(out var phrase))
            {
                var translated = await worker
                    .TranslateAsync(phrase, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                output.Play(translated);
            }
        }
        finally
        {
            consumer.Release();
        }
    }

    public void Stop()
    {
        queue.Clear();
        output.StopPlayback();
    }

    public void Dispose()
    {
        Stop();
        consumer.Dispose();
    }
}
