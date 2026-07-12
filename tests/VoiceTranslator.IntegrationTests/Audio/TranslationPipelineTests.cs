using FluentAssertions;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.IntegrationTests.Audio;

public sealed class TranslationPipelineTests
{
    [Fact]
    public async Task ProcessQueuedAsyncTranslatesOnlyNewestPhrasesSequentially()
    {
        var worker = new FakePhraseTranslationWorker();
        var output = new FakeAudioPlaybackSink();
        using var pipeline = new TranslationPipeline(
            worker,
            output,
            queueCapacity: 2);
        pipeline.Enqueue(new Phrase("one", [1]));
        pipeline.Enqueue(new Phrase("two", [2]));
        pipeline.Enqueue(new Phrase("three", [3]));

        await pipeline.ProcessQueuedAsync(CancellationToken.None);

        worker.TranslatedIds.Should().Equal("two", "three");
        worker.MaximumConcurrency.Should().Be(1);
        output.Played.Select(pcm => pcm.Single())
            .Should().Equal(2, 3);
    }

    [Fact]
    public async Task StopClearsQueuedAndPlayingAudio()
    {
        var worker = new FakePhraseTranslationWorker();
        var output = new FakeAudioPlaybackSink();
        using var pipeline = new TranslationPipeline(
            worker,
            output,
            queueCapacity: 2);
        pipeline.Enqueue(new Phrase("one", [1]));

        pipeline.Stop();
        await pipeline.ProcessQueuedAsync(CancellationToken.None);

        worker.TranslatedIds.Should().BeEmpty();
        output.Stopped.Should().BeTrue();
    }

    [Fact]
    public async Task StopCancelsInFlightTranslationAndSuppressesLateOutput()
    {
        var worker = new BlockingPhraseTranslationWorker();
        var output = new FakeAudioPlaybackSink();
        using var pipeline = new TranslationPipeline(
            worker,
            output,
            queueCapacity: 2);
        pipeline.Enqueue(new Phrase("one", [1]));
        Task processing = pipeline.ProcessQueuedAsync(
            CancellationToken.None);
        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        pipeline.Stop();
        worker.Complete([9]);
        await processing;

        worker.CancellationObserved.Should().BeTrue();
        output.Played.Should().BeEmpty();
        output.Stopped.Should().BeTrue();
    }

    private sealed class FakePhraseTranslationWorker
        : IPhraseTranslationWorker
    {
        private int activeCount;
        private readonly object syncLock = new();

        public List<string> TranslatedIds { get; } = [];
        public int MaximumConcurrency { get; private set; }

        public async Task<byte[]> TranslateAsync(
            Phrase phrase,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref activeCount);
            lock (syncLock)
            {
                MaximumConcurrency = Math.Max(
                    MaximumConcurrency,
                    current);
            }
            try
            {
                await Task.Yield();
                lock (syncLock)
                {
                    TranslatedIds.Add(phrase.Id);
                }
                return phrase.Pcm16;
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        }
    }

    private sealed class FakeAudioPlaybackSink : IAudioPlaybackSink
    {
        private readonly object syncLock = new();
        private readonly List<byte[]> played = [];
        private bool stopped;

        public List<byte[]> Played
        {
            get
            {
                lock (syncLock)
                {
                    return [.. played];
                }
            }
        }

        public bool Stopped
        {
            get => Volatile.Read(ref stopped);
            private set => Volatile.Write(ref stopped, value);
        }

        public void Play(byte[] pcm)
        {
            lock (syncLock)
            {
                played.Add(pcm);
            }
        }

        public void StopPlayback() => Stopped = true;
    }

    private sealed class BlockingPhraseTranslationWorker
        : IPhraseTranslationWorker
    {
        private readonly TaskCompletionSource<byte[]> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved { get; private set; }

        public async Task<byte[]> TranslateAsync(
            Phrase phrase,
            CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () => CancellationObserved = true);
            Started.TrySetResult();
            return await completion.Task.ConfigureAwait(false);
        }

        public void Complete(byte[] pcm)
        {
            completion.TrySetResult(pcm);
        }
    }
}
