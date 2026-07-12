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
        await using var session = new DesktopTranslationSession(
            worker,
            capture,
            output,
            "en");

        var activitiesLock = new object();
        List<string> activities = [];


        var inputLevelsLock = new object();
        List<double> inputLevels = [];


        var outputLevelsLock = new object();
        List<double> outputLevels = [];

        session.ActivityChanged += a => { lock (activitiesLock) activities.Add(a); };
        session.InputLevelChanged += l => { lock (inputLevelsLock) inputLevels.Add(l); };
        session.OutputLevelChanged += l => { lock (outputLevelsLock) outputLevels.Add(l); };
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
        output.Pcm.ToArray().Should().Equal(7, 0, 8, 0);
        lock (inputLevelsLock) inputLevels.ToArray().Should().Contain(level => level > 0);
        lock (outputLevelsLock) outputLevels.ToArray().Should().Contain(level => level > 0);
        lock (activitiesLock) activities.ToArray().Should().Contain("Phrase captured.");
        lock (activitiesLock) activities.ToArray().Should().Contain("Translating phrase.");
        lock (activitiesLock) activities.ToArray().Should().Contain("Playing translated speech.");
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
