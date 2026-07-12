using FluentAssertions;
using VoiceTranslator.Infrastructure.Audio.Routing;

namespace VoiceTranslator.WindowsE2ETests;

public sealed class OutputRoutingTests
{
    [Fact]
    public async Task FailingSinkDoesNotStopTheOtherOutputLane()
    {
        var physical = new RecordingSink(failFirstPlay: true);
        var virtualOutput = new RecordingSink();
        await using var router = new AudioOutputRouter(
            physical,
            virtualOutput);
        var first = SynthesizedPcmPayload.Create([1, 2, 3]);
        var second = SynthesizedPcmPayload.Create([4, 5, 6]);

        router.RouteSynthesized(first);
        await virtualOutput.WaitForPlayCountAsync(1);
        router.RouteSynthesized(second);
        await virtualOutput.WaitForPlayCountAsync(2);

        physical.PlayAttempts.Should().Be(1);
        virtualOutput.Played.Should().Equal(first, second);
        virtualOutput.Played[0].Should().BeSameAs(first);
        router.Failures.Should().ContainSingle(
            failure => failure.OutputName == "physical");
    }

    [Fact]
    public async Task PayloadCopiesSourceAndBothSinksReceiveTheSameInstance()
    {
        var physical = new RecordingSink();
        var virtualOutput = new RecordingSink();
        await using var router = new AudioOutputRouter(
            physical,
            virtualOutput);
        byte[] sourcePcm = [1, 2, 3];
        var payload = SynthesizedPcmPayload.Create(sourcePcm);

        sourcePcm[0] = 99;
        router.RouteSynthesized(payload);
        await Task.WhenAll(
            physical.WaitForPlayCountAsync(1),
            virtualOutput.WaitForPlayCountAsync(1));

        payload.Pcm.Span.ToArray().Should().Equal(1, 2, 3);
        physical.Played.Single().Should().BeSameAs(payload);
        virtualOutput.Played.Single().Should().BeSameAs(payload);
    }

    [Fact]
    public async Task StopCancelsPlayingAudioAndClearsQueuedPayloads()
    {
        var physical = new RecordingSink(blockPlayback: true);
        var virtualOutput = new RecordingSink(blockPlayback: true);
        await using var router = new AudioOutputRouter(
            physical,
            virtualOutput);

        router.RouteSynthesized(SynthesizedPcmPayload.Create([1]));
        await Task.WhenAll(
            physical.WaitForPlayCountAsync(1),
            virtualOutput.WaitForPlayCountAsync(1));
        router.RouteSynthesized(SynthesizedPcmPayload.Create([2]));

        await router.StopAsync();

        physical.StopCount.Should().Be(1);
        virtualOutput.StopCount.Should().Be(1);
        physical.PlayAttempts.Should().Be(1);
        virtualOutput.PlayAttempts.Should().Be(1);
    }

    private sealed class RecordingSink(
        bool failFirstPlay = false,
        bool blockPlayback = false) : ISynthesizedAudioSink
    {
        private readonly TaskCompletionSource unblock =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
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

        public int PlayAttempts { get; private set; }
        public int StopCount { get; private set; }

        public ValueTask PlayAsync(
            SynthesizedPcmPayload payload,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource? toSet = null;
            lock (_lock)
            {
                PlayAttempts++;
                if (failFirstPlay && PlayAttempts == 1)
                {
                    throw new InvalidOperationException("sink failed");
                }

                played.Add(payload);
                if (_tcs != null && played.Count >= _expectedCount)
                {
                    toSet = _tcs;
                    _tcs = null;
                }
            }

            toSet?.TrySetResult();

            if (!blockPlayback)
            {
                return ValueTask.CompletedTask;
            }

            return new ValueTask(
                unblock.Task.WaitAsync(cancellationToken));
        }

        public void StopPlayback()
        {
            StopCount++;
            unblock.TrySetResult();
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

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
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
