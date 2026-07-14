using FluentAssertions;
using NAudio.Wave;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Infrastructure.Audio.Capture;
using VoiceTranslator.Infrastructure.Audio.Playback;
using VoiceTranslator.Infrastructure.Audio.SignalSafety;

namespace VoiceTranslator.IntegrationTests.Audio;

public sealed class AudioPipelineTests
{
    [Fact]
    public void LimiterClampsSamplesToSafeRange()
    {
        float[] samples = [2.0f, -2.0f, 0.25f];

        SoftLimiter.Process(samples);

        samples.Should().OnlyContain(
            sample => sample >= -1.0f && sample <= 1.0f);
        samples[2].Should().Be(0.25f);
    }

    [Fact]
    public void QueueKeepsOnlyTwoNewestCompletePhrases()
    {
        var queue = new BoundedPhraseQueue(capacity: 2);
        queue.Enqueue(new Phrase("one", []));
        queue.Enqueue(new Phrase("two", []));
        queue.Enqueue(new Phrase("three", []));

        queue.Select(phrase => phrase.Id)
            .Should().Equal("two", "three");
    }

    [Fact]
    public void PlaybackStopClearsBufferedAudio()
    {
        var output = new FakeWavePlayer();
        using var sink = new WasapiPlaybackSink(
            output,
            new WaveFormat(24_000, 16, 1));

        sink.Play([1, 2, 3, 4]);

        var buffer = output.Provider.Should()
            .BeOfType<BufferedWaveProvider>().Subject;
        buffer.DiscardOnBufferOverflow.Should().BeTrue();
        buffer.BufferDuration.Should().Be(TimeSpan.FromSeconds(60));
        buffer.BufferedBytes.Should().BeGreaterThan(4);

        sink.Stop();

        output.Stopped.Should().BeTrue();
        buffer.BufferedBytes.Should().Be(0);
    }

    [Fact]
    public void PlaybackBufferDoesNotTruncateTenSecondPhrase()
    {
        var output = new FakeWavePlayer();
        using var sink = new WasapiPlaybackSink(
            output,
            new WaveFormat(24_000, 16, 1));
        var pcm = new byte[24_000 * sizeof(short) * 10];

        sink.Play(pcm);

        var buffer = output.Provider.Should()
            .BeOfType<BufferedWaveProvider>().Subject;
        buffer.BufferedBytes.Should().BeGreaterThan(pcm.Length);
    }

    [Fact]
    public async Task PlaybackCompletesAfterBufferedAudioIsConsumed()
    {
        var output = new FakeWavePlayer();
        using var sink = new WasapiPlaybackSink(
            output,
            new WaveFormat(24_000, 16, 1));

        Task playback = sink.PlayAsync(
            new byte[2_400],
            CancellationToken.None);
        var buffer = output.Provider.Should()
            .BeOfType<BufferedWaveProvider>().Subject;
        var consumed = new byte[buffer.BufferedBytes];
        _ = buffer.Read(consumed, 0, consumed.Length);

        await playback.WaitAsync(TimeSpan.FromSeconds(2));

        output.Stopped.Should().BeTrue();
        buffer.BufferedBytes.Should().Be(0);
    }

    [Fact]
    public void CaptureCopiesOnlyRecordedBytes()
    {
        var input = new FakeWaveIn();
        using var capture = new WasapiMicrophoneCapture(input);
        byte[]? received = null;
        capture.AudioAvailable += (_, eventArgs) =>
            received = eventArgs.Audio;

        input.RaiseDataAvailable([1, 2, 3, 4], bytesRecorded: 2);

        received.Should().Equal(1, 2);
    }

    [Fact]
    public void NormalizerKeepsMono16KhzPcm16InWorkerFormat()
    {
        byte[] pcm = [1, 0, 2, 0];

        var normalized = Pcm16Normalizer.Normalize(
            pcm,
            new WaveFormat(16_000, 16, 1));

        normalized.Should().Equal(pcm);
    }

    private sealed class FakeWavePlayer : IWavePlayer
    {
        public IWaveProvider? Provider { get; private set; }
        public bool Stopped { get; private set; }
        public PlaybackState PlaybackState { get; private set; }
        public WaveFormat OutputWaveFormat =>
            Provider?.WaveFormat ?? new WaveFormat(24_000, 16, 1);
        public float Volume { get; set; } = 1.0f;

        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public void Init(IWaveProvider waveProvider) =>
            Provider = waveProvider;

        public void Play() => PlaybackState = PlaybackState.Playing;

        public void Pause() => PlaybackState = PlaybackState.Paused;

        public void Stop()
        {
            Stopped = true;
            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeWaveIn : IWaveIn
    {
        public WaveFormat WaveFormat { get; set; } =
            new(48_000, 16, 2);

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public void StartRecording()
        {
        }

        public void StopRecording() =>
            RecordingStopped?.Invoke(this, new StoppedEventArgs());

        public void RaiseDataAvailable(
            byte[] buffer,
            int bytesRecorded) =>
            DataAvailable?.Invoke(
                this,
                new WaveInEventArgs(buffer, bytesRecorded));

        public void Dispose()
        {
        }
    }
}
