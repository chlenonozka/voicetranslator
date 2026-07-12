using FluentAssertions;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Domain.Audio;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Infrastructure.Audio.Routing;

namespace VoiceTranslator.WindowsE2ETests;

public sealed class LanguageAndRoutingE2ETests
{
    public static IEnumerable<object[]> GetRoutingCombinations()
    {
        foreach (var language in TargetLanguage.All)
        {
            yield return new object[] { language.Code, OutputMode.Physical, true, false };
            yield return new object[] { language.Code, OutputMode.VirtualCable, false, true };
            yield return new object[] { language.Code, OutputMode.Both, true, true };
        }
    }

    [Theory]
    [MemberData(nameof(GetRoutingCombinations))]
    public async Task RussianPhraseIsTranslatedAndRoutedToCorrectSinks(
        string targetLanguageCode,
        OutputMode outputMode,
        bool expectPhysical,
        bool expectVirtual)
    {
        // 1. Arrange
        var worker = new FakePhraseTranslationWorker(targetLanguageCode);
        var physicalSink = new RecordingSink();
        var virtualSink = new RecordingSink();

        await using var router = new AudioOutputRouter(
            physicalSink,
            virtualSink,
            outputMode);

        using var pipeline = new TranslationPipeline(
            worker,
            new RouterPlaybackAdapter(router),
            queueCapacity: 2);

        byte[] inputPcm = Enumerable.Range(0, 16000 * 2)
            .Select(i => (byte)(i % 256))
            .ToArray();

        var phraseId = $"test-phrase-{targetLanguageCode}-{outputMode}";
        var phrase = new Phrase(phraseId, inputPcm);

        // 2. Act
        pipeline.Enqueue(phrase);
        var processingTask = pipeline.ProcessQueuedAsync(CancellationToken.None);

        if (expectPhysical)
        {
            await physicalSink.WaitForPlayCountAsync(1);
        }
        if (expectVirtual)
        {
            await virtualSink.WaitForPlayCountAsync(1);
        }

        pipeline.Stop();
        await processingTask;

        // 3. Assert
        worker.TranslatedIds.Should().ContainSingle(id => id == phraseId);

        if (expectPhysical)
        {
            physicalSink.Played.Should().HaveCount(1);
            physicalSink.Played[0].Pcm.Span.Length.Should().BeGreaterThan(0);
        }
        else
        {
            physicalSink.Played.Should().BeEmpty();
        }

        if (expectVirtual)
        {
            virtualSink.Played.Should().HaveCount(1);
            virtualSink.Played[0].Pcm.Span.Length.Should().BeGreaterThan(0);
        }
        else
        {
            virtualSink.Played.Should().BeEmpty();
        }
    }

    private sealed class FakePhraseTranslationWorker : IPhraseTranslationWorker
    {
        private readonly string expectedLanguage;
        private readonly object syncLock = new();

        public FakePhraseTranslationWorker(string expectedLanguage)
        {
            this.expectedLanguage = expectedLanguage;
        }


        private readonly List<string> translatedIds = [];
        public List<string> TranslatedIds
        {
            get
            {
                lock (syncLock) return [.. translatedIds];
            }
        }


        public async Task<byte[]> TranslateAsync(
            Phrase phrase,
            CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken);
            lock (syncLock)
            {
                translatedIds.Add(phrase.Id);
            }
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


        private readonly List<SynthesizedPcmPayload> played = [];
        public List<SynthesizedPcmPayload> Played
        {
            get
            {
                lock (_lock) return [.. played];
            }
        }


        public ValueTask PlayAsync(
            SynthesizedPcmPayload payload,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource? toSet = null;
            lock (_lock)
            {
                played.Add(payload);
                if (_tcs != null && played.Count >= _expectedCount)
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
                if (played.Count >= expected)
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
            played.Count.Should().BeGreaterThanOrEqualTo(expected);
        }
    }
}
