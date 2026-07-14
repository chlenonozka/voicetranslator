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
        byte[] savedReference = [];
        await using var session = new DesktopTranslationSession(
            worker,
            capture,
            output,
            "en",
            referenceCaptured: (wave, _) =>
            {
                savedReference = wave.ToArray();
                return Task.CompletedTask;
            });

        var activitiesLock = new object();
        List<string> activities = [];


        var inputLevelsLock = new object();
        List<double> inputLevels = [];


        var outputLevelsLock = new object();
        List<double> outputLevels = [];

        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        List<(int Percent, string Label)> progress = [];

        session.ActivityChanged += a => { lock (activitiesLock) activities.Add(a); };
        session.InputLevelChanged += l => { lock (inputLevelsLock) inputLevels.Add(l); };
        session.OutputLevelChanged += l => { lock (outputLevelsLock) outputLevels.Add(l); };
        session.ProgressChanged += (percent, label) =>
        {
            lock (progress) progress.Add((percent, label));
            if (percent == 100 && label == "Перевод озвучен")
            {
                completed.TrySetResult();
            }
        };
        session.Start();

        capture.EmitPhrase();
        await worker.ReferenceCreated.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        capture.EmitPhrase();
        await output.Played.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        worker.ReferenceWave.Should().StartWith(
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
        savedReference.Should().StartWith(
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
        worker.TranslatedWave.Should().StartWith(
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
        output.Pcm.ToArray().Should().Equal(7, 0, 8, 0);
        lock (inputLevelsLock) inputLevels.ToArray().Should().Contain(level => level > 0);
        lock (outputLevelsLock) outputLevels.ToArray().Should().Contain(level => level > 0);
        lock (activitiesLock) activities.ToArray().Should().Contain("Фраза записана.");
        lock (activitiesLock) activities.ToArray().Should().Contain("Обрабатываю фразу.");
        lock (activitiesLock) activities.ToArray().Should().Contain("Озвучиваю перевод.");
        lock (outputLevelsLock) outputLevels.ToArray().Should().EndWith(0);
        lock (progress) progress.Should().Contain((100, "Перевод озвучен"));
    }

    [Fact]
    public async Task ExistingProfileTranslatesFirstCapturedPhrase()
    {
        var worker = new FakeLocalWorker();
        var capture = new FakeCaptureSource();
        var output = new FakePlaybackSink();
        await using var session = new DesktopTranslationSession(
            worker,
            capture,
            output,
            "de",
            existingReferenceWav: CreateReferenceWave());
        session.Start();

        await worker.ReferenceCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));
        capture.EmitPhrase();
        await output.Played.Task.WaitAsync(TimeSpan.FromSeconds(2));

        worker.TranslatedWave.Should().StartWith(
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
        output.Pcm.ToArray().Should().Equal(7, 0, 8, 0);
    }

    [Fact]
    public async Task UnrecognizedPhraseDoesNotStopTheSession()
    {
        var worker = new RecoverablePhraseWorker();
        var capture = new FakeCaptureSource();
        var output = new FakePlaybackSink();
        await using var session = new DesktopTranslationSession(
            worker,
            capture,
            output,
            "en");
        var warning = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;
        session.ActivityChanged += message =>
        {
            if (message.StartsWith("Фраза не распознана", StringComparison.Ordinal))
            {
                warning.TrySetResult();
            }
        };
        session.Failed += error => failure = error;
        session.Start();

        capture.EmitPhrase();
        await worker.ReferenceCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));
        capture.EmitPhrase();
        await warning.Task.WaitAsync(TimeSpan.FromSeconds(2));
        capture.EmitPhrase();
        await output.Played.Task.WaitAsync(TimeSpan.FromSeconds(2));

        failure.Should().BeNull();
        worker.TranslationAttempts.Should().Be(2);
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

        capture.EmitPhrase();
        await worker.ReferenceCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));
        capture.EmitPhrase();
        await observer.Signaled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        observer.Failure.Should().Be(SessionFailure.GpuMemoryExhausted);
    }

    private static byte[] CreateReferenceWave()
    {
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(
            stream,
            new WaveFormat(16_000, 16, 1)))
        {
            writer.Write(new byte[16_000]);
        }

        return stream.ToArray();
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

    private sealed class RecoverablePhraseWorker : ILocalTranslationWorker
    {
        public TaskCompletionSource ReferenceCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int TranslationAttempts { get; private set; }

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
            CancellationToken cancellationToken)
        {
            TranslationAttempts++;
            if (TranslationAttempts == 1)
            {
                return Task.FromException<PhraseTranslationResult>(
                    new HttpRequestException(
                        "Фраза не распознана",
                        inner: null,
                        statusCode: System.Net.HttpStatusCode.UnprocessableEntity));
            }

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
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<WorkerPreflightReport> PreflightAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(
            Guid requestId,
            CancellationToken cancellationToken) => Task.CompletedTask;

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


        private readonly object _lock = new();
        private byte[] _pcm = [];
        public byte[] Pcm
        {
            get { lock (_lock) return _pcm; }
            private set { lock (_lock) _pcm = value; }
        }


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
            ReferenceWave = referenceWav.ToArray();
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
