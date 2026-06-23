using FluentAssertions;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.IntegrationTests.Privacy;

public sealed class NoSpeechPersistenceTests
{
    [Fact]
    public async Task TranslationAndStopDoNotCreateSpeechArtifacts()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"voice-translator-privacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string previousDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            using var pipeline = new TranslationPipeline(
                new MemoryOnlyWorker(),
                new MemoryOnlyOutput(),
                queueCapacity: 2);
            pipeline.Enqueue(new Phrase("private-phrase", [1, 2, 3]));

            await pipeline.ProcessQueuedAsync(CancellationToken.None);
            await pipeline.StopSessionAsync(CancellationToken.None);

            Directory.EnumerateFiles(
                    root,
                    "*",
                    SearchOption.AllDirectories)
                .Should().BeEmpty();
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class MemoryOnlyWorker : IPhraseTranslationWorker
    {
        public Task<byte[]> TranslateAsync(
            Phrase phrase,
            CancellationToken cancellationToken)
        {
            return Task.FromResult((byte[])phrase.Pcm16.Clone());
        }
    }

    private sealed class MemoryOnlyOutput : IAudioPlaybackSink
    {
        public void Play(byte[] pcm)
        {
        }

        public void StopPlayback()
        {
        }
    }
}
