using FluentAssertions;
using NAudio.Wave;
using VoiceTranslator.App.Services;
using VoiceTranslator.Infrastructure.Audio.Capture;

namespace VoiceTranslator.UnitTests.Services;

public sealed class VoiceProfileRecordingSessionTests
{
    [Fact]
    public async Task StopReturnsWorkerCompatibleWaveAfterMinimumDuration()
    {
        var capture = new FakeCaptureSource();
        await using var recording = new VoiceProfileRecordingSession(
            capture,
            minimumDuration: TimeSpan.FromMilliseconds(100),
            maximumDuration: TimeSpan.FromSeconds(1));
        double inputLevel = 0;
        recording.InputLevelChanged += level => inputLevel = level;
        recording.Start();

        capture.Emit(milliseconds: 200, amplitude: 8_000);
        byte[] wave = await recording.StopAsync();

        wave.Should().StartWith(
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
        inputLevel.Should().BeGreaterThan(0);
        capture.Started.Should().BeTrue();
        capture.Stopped.Should().BeTrue();
    }

    [Fact]
    public async Task StopRejectsRecordingShorterThanMinimumDuration()
    {
        var capture = new FakeCaptureSource();
        await using var recording = new VoiceProfileRecordingSession(capture);
        recording.Start();
        capture.Emit(milliseconds: 500, amplitude: 8_000);

        Func<Task> stop = () => recording.StopAsync();

        await stop.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*трёх секунд*");
    }

    [Fact]
    public async Task MaximumDurationRaisesAutomaticCompletionSignal()
    {
        var capture = new FakeCaptureSource();
        await using var recording = new VoiceProfileRecordingSession(
            capture,
            minimumDuration: TimeSpan.FromMilliseconds(20),
            maximumDuration: TimeSpan.FromMilliseconds(150));
        var limitReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        recording.LimitReached += () => limitReached.TrySetResult();
        recording.Start();

        await limitReached.Task.WaitAsync(TimeSpan.FromSeconds(2));

        limitReached.Task.IsCompletedSuccessfully.Should().BeTrue();
    }

    private sealed class FakeCaptureSource : IAudioCaptureSource
    {
        public WaveFormat WaveFormat { get; } = new(16_000, 16, 1);

        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        public event EventHandler<AudioAvailableEventArgs>? AudioAvailable;

        public void StartCapture() => Started = true;

        public void StopCapture() => Stopped = true;

        public void Emit(int milliseconds, short amplitude)
        {
            int samples = 16_000 * milliseconds / 1_000;
            var bytes = new byte[samples * sizeof(short)];
            for (int index = 0; index < samples; index++)
            {
                BitConverter.TryWriteBytes(
                    bytes.AsSpan(
                        index * sizeof(short),
                        sizeof(short)),
                    amplitude);
            }

            AudioAvailable?.Invoke(
                this,
                new AudioAvailableEventArgs(bytes));
        }

        public void Dispose()
        {
        }
    }
}
