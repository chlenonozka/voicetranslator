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

    private sealed class FakePhraseTranslationWorker
        : IPhraseTranslationWorker
    {
        private int activeCount;

        public List<string> TranslatedIds { get; } = [];
        public int MaximumConcurrency { get; private set; }

        public async Task<byte[]> TranslateAsync(
            Phrase phrase,
            CancellationToken cancellationToken)
        {
            activeCount++;
            MaximumConcurrency = Math.Max(
                MaximumConcurrency,
                activeCount);
            try
            {
                await Task.Yield();
                TranslatedIds.Add(phrase.Id);
                return phrase.Pcm16;
            }
            finally
            {
                activeCount--;
            }
        }
    }

    private sealed class FakeAudioPlaybackSink : IAudioPlaybackSink
    {
        public List<byte[]> Played { get; } = [];
        public bool Stopped { get; private set; }

        public void Play(byte[] pcm) => Played.Add(pcm);

        public void StopPlayback() => Stopped = true;
    }
}
