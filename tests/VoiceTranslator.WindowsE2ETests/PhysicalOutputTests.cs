using FluentAssertions;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Domain.Audio;
using VoiceTranslator.Infrastructure.Audio.Routing;

namespace VoiceTranslator.WindowsE2ETests;

public sealed class PhysicalOutputTests
{
    [Fact]
    public async Task RussianPhraseIsTranslatedAndPlayedOnPhysicalOutput()
    {
        // 1. Arrange
        var worker = new FakePhraseTranslationWorker();

        // Use a RecordingSink to stand in for the physical output device
        // This validates the E2E logic (routing to physical only)
        // without requiring a real audio device in CI.
        var physicalSink = new RecordingSink();
        var virtualSink = new RecordingSink();

        await using var router = new AudioOutputRouter(
            physicalSink,
            virtualSink,
            OutputMode.Physical); // Physical output only

        using var pipeline = new TranslationPipeline(
            worker,
            new RouterPlaybackAdapter(router),
            queueCapacity: 2);

        // Simulate a completed Russian phrase (mono, 16kHz PCM)
        byte[] inputPcm = Enumerable.Range(0, 16000 * 2) // 2 seconds of dummy data
            .Select(i => (byte)(i % 256))
            .ToArray();

        var phrase = new Phrase("test-phrase-1", inputPcm);

        // 2. Act
        pipeline.Enqueue(phrase);

        // Let the pipeline process the queued phrase
        var processingTask = pipeline.ProcessQueuedAsync(CancellationToken.None);

        // Wait for the physical sink to receive the played audio
        await physicalSink.WaitForPlayCountAsync(1);

        // Stop the pipeline gracefully
        pipeline.Stop();
        await processingTask;

        // 3. Assert
        // The worker should have translated the phrase
        worker.TranslatedIds.Should().ContainSingle(id => id == "test-phrase-1");

        // The physical output should have received the translated PCM
        physicalSink.Played.Should().HaveCount(1);
        physicalSink.Played[0].Pcm.Span.Length.Should().BeGreaterThan(0);

        // The virtual output should not have received anything due to OutputMode.Physical
        virtualSink.Played.Should().BeEmpty();
    }

    private sealed class FakePhraseTranslationWorker : IPhraseTranslationWorker
    {
        private readonly object syncLock = new();

        public List<string> TranslatedIds { get; } = [];

        public async Task<byte[]> TranslateAsync(
            Phrase phrase,
            CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken); // Simulate latency
            lock (syncLock)
            {
                TranslatedIds.Add(phrase.Id);
            }

            // Return some dummy translated PCM data
            return [10, 20, 30, 40];
        }
    }

    private sealed class RouterPlaybackAdapter : IAudioPlaybackSink
    {
        private readonly AudioOutputRouter router;

        public RouterPlaybackAdapter(AudioOutputRouter router)
        {
            this.router = router;
        }

        public void Play(byte[] pcm)
        {
            var payload = SynthesizedPcmPayload.Create(pcm);
            router.RouteSynthesized(payload);
        }

        public void StopPlayback()
        {
            router.StopAsync().GetAwaiter().GetResult();
        }
    }

    private sealed class RecordingSink : ISynthesizedAudioSink
    {
        private TaskCompletionSource? _tcs;
        private int _expectedCount;
        private readonly object _lock = new();

        public List<SynthesizedPcmPayload> Played { get; } = [];

        public ValueTask PlayAsync(
            SynthesizedPcmPayload payload,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource? toSet = null;
            lock (_lock)
            {
                Played.Add(payload);
                if (_tcs != null && Played.Count >= _expectedCount)
                {
                    toSet = _tcs;
                    _tcs = null;
                }
            }
            toSet?.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public void StopPlayback()
        {
        }

        public async Task WaitForPlayCountAsync(int expected)
        {
            Task waitTask;
            lock (_lock)
            {
                if (Played.Count >= expected)
                {
                    return;
                }

                _expectedCount = expected;
                _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = _tcs.Task;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await waitTask.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }
            Played.Count.Should().BeGreaterThanOrEqualTo(expected);
        }
    }
}
