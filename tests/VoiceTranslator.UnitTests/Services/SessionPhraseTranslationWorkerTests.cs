using FluentAssertions;
using NAudio.Wave;
using VoiceTranslator.App.Services;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.UnitTests.Services;

public sealed class SessionPhraseTranslationWorkerTests
{
    [Fact]
    public async Task TranslateAsyncSendsWorkerWaveAndDecodesSynthesizedWave()
    {
        var worker = new FakeLocalWorker();
        Guid sessionId = Guid.NewGuid();
        var adapter = new SessionPhraseTranslationWorker(
            worker,
            sessionId,
            "en");
        var phrase = new Phrase("phrase-1", [1, 0, 2, 0]);

        byte[] pcm = await adapter.TranslateAsync(
            phrase,
            CancellationToken.None);

        worker.SessionId.Should().Be(sessionId);
        worker.TargetLanguage.Should().Be("en");
        worker.InputWave.Should().StartWith(
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
        pcm.Should().Equal(3, 0, 4, 0);
    }

    private sealed class FakeLocalWorker : ILocalTranslationWorker
    {
        public Guid SessionId { get; private set; }
        public string? TargetLanguage { get; private set; }
        public byte[] InputWave { get; private set; } = [];

        public Task<PhraseTranslationResult> TranslatePhraseAsync(
            Guid sessionId,
            string targetLanguage,
            byte[] phraseWav,
            Guid requestId,
            CancellationToken cancellationToken)
        {
            SessionId = sessionId;
            TargetLanguage = targetLanguage;
            InputWave = phraseWav;
            return Task.FromResult(new PhraseTranslationResult(
                requestId,
                CreateWave([3, 0, 4, 0]),
                1,
                2,
                3,
                "balanced"));
        }

        public Task CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<WorkerPreflightReport> PreflightAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid> CreateSpeakerSessionAsync(
            byte[] referenceWav,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteSpeakerSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

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
