using System.Net.Http;
using VoiceTranslator.Application.Orchestration;
using FluentAssertions;
using NAudio.Wave;
using VoiceTranslator.App.Services;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Infrastructure.Audio.Capture;

namespace VoiceTranslator.UnitTests.Services;

public sealed class DesktopTranslationSessionTests
{
    [Fact]
    public async Task FirstPhraseCreatesReferenceAndSecondPhraseIsTranslated()
    {
        var worker = new FakeLocalWorker();
        var capture = new FakeCaptureSource();
        var output = new FakePlaybackSink();
        var observer = new FakeSessionFailureObserver();
        await using var session = new DesktopTranslationSession(
            worker,
            capture,
            output,
            "en",
            observer);
        List<string> activities = [];
        List<double> inputLevels = [];
        List<double> outputLevels = [];
        session.ActivityChanged += activities.Add;
        session.InputLevelChanged += inputLevels.Add;
        session.OutputLevelChanged += outputLevels.Add;
        session.Start();

        capture.EmitPhrase();
        await worker.ReferenceCreated.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        capture.EmitPhrase();
        await output.Played.Task.WaitAsync(TimeSpan.FromSeconds(2));

        worker.ReferenceWave.Should().StartWith(
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
        worker.TranslatedWave.Should().StartWith(
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
        output.Pcm.Should().Equal(7, 0, 8, 0);
        inputLevels.Should().Contain(level => level > 0);
        outputLevels.Should().Contain(level => level > 0);
        activities.Should().Contain("Phrase captured.");
        activities.Should().Contain("Translating phrase.");
        activities.Should().Contain("Playing translated speech.");
    }

    [Fact]
    public async Task GpuMemoryExhaustionReportsToCoordinator()
    {
        var worker = new OomWorker();
        var capture = new FakeCaptureSource();
        var output = new FakePlaybackSink();
        var observer = new FakeSessionFailureObserver();
        await using var session = new DesktopTranslationSession(
            worker,
            capture,
            output,
            "en",
            observer);
        session.Start();

        // Emit first phrase to create speaker session
        capture.EmitPhrase();

        await worker.ReferenceCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Emit second phrase to trigger translation
        capture.EmitPhrase();

        await observer.Signaled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        observer.Failure.Should().Be(SessionFailure.GpuMemoryExhausted);
    }

    private sealed class FakeSessionFailureObserver : ISessionFailureObserver
    {
        public TaskCompletionSource Signaled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SessionFailure? Failure { get; private set; }

        public Task OnSessionFailureAsync(
            SessionFailure failure,
            CancellationToken cancellationToken)
        {
            Failure = failure;
            Signaled.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class OomWorker : ILocalTranslationWorker
    {
        public TaskCompletionSource ReferenceCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Guid> CreateSpeakerSessionAsync(
            byte[] referenceWav,
            CancellationToken cancellationToken)
        {
            ReferenceCreated.TrySetResult();
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<PhraseTranslationResult> TranslatePhraseAsync(
            Guid sessionId,
            string targetLanguage,
            byte[] phraseWav,
            Guid requestId,
            CancellationToken cancellationToken) =>
            Task.FromException<PhraseTranslationResult>(
                new HttpRequestException(
                    "Worker request failed with 507 InsufficientStorage",
                    inner: null,
                    statusCode: System.Net.HttpStatusCode.InsufficientStorage));

        public Task DeleteSpeakerSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<WorkerPreflightReport> PreflightAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FakeCaptureSource : IAudioCaptureSource
    {
        public WaveFormat WaveFormat { get; } =
            new(16_000, 16, 1);

        public event EventHandler<AudioAvailableEventArgs>? AudioAvailable;

        public void StartCapture()
        {
        }

        public void StopCapture()
        {
        }

        public void EmitPhrase()
        {
            AudioAvailable?.Invoke(
                this,
                new AudioAvailableEventArgs(
                    CreatePcm(milliseconds: 400, amplitude: 8_000)));
            AudioAvailable?.Invoke(
                this,
                new AudioAvailableEventArgs(
                    CreatePcm(milliseconds: 600, amplitude: 0)));
        }

        public void Dispose()
        {
        }

        private static byte[] CreatePcm(
            int milliseconds,
            short amplitude)
        {
            int sampleCount = 16_000 * milliseconds / 1_000;
            var pcm = new byte[sampleCount * sizeof(short)];
            for (int index = 0; index < sampleCount; index++)
            {
                BitConverter.TryWriteBytes(
                    pcm.AsSpan(index * sizeof(short), sizeof(short)),
                    amplitude);
            }
            return pcm;
        }
    }

    private sealed class FakePlaybackSink : IAudioPlaybackSink
    {
        public TaskCompletionSource Played { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public byte[] Pcm { get; private set; } = [];

        public void Play(byte[] pcm)
        {
            Pcm = pcm;
            Played.TrySetResult();
        }

        public void StopPlayback()
        {
        }
    }

    private sealed class FakeLocalWorker : ILocalTranslationWorker
    {
        public TaskCompletionSource ReferenceCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public byte[] ReferenceWave { get; private set; } = [];
        public byte[] TranslatedWave { get; private set; } = [];

        public Task<Guid> CreateSpeakerSessionAsync(
            byte[] referenceWav,
            CancellationToken cancellationToken)
        {
            ReferenceWave = referenceWav;
            ReferenceCreated.TrySetResult();
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<PhraseTranslationResult> TranslatePhraseAsync(
            Guid sessionId,
            string targetLanguage,
            byte[] phraseWav,
            Guid requestId,
            CancellationToken cancellationToken)
        {
            TranslatedWave = phraseWav;
            return Task.FromResult(new PhraseTranslationResult(
                requestId,
                CreateWave([7, 0, 8, 0]),
                1,
                2,
                3,
                "balanced"));
        }

        public Task DeleteSpeakerSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<WorkerPreflightReport> PreflightAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(
            Guid requestId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose()
        {
        }

        private static byte[] CreateWave(byte[] pcm)
        {
            using var stream = new MemoryStream();
            using (var writer = new WaveFileWriter(
                stream,
                new WaveFormat(24_000, 16, 1)))
            {
                writer.Write(pcm);
            }
            return stream.ToArray();
        }
    }
}
