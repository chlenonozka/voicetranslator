using FluentAssertions;
using VoiceTranslator.Infrastructure.Audio.Capture;

namespace VoiceTranslator.IntegrationTests.Audio;

public sealed class PhraseSegmentationTests
{
    [Fact]
    public void SilenceAfterSpeechCompletesOnePhrase()
    {
        var segmenter = new Pcm16PhraseSegmenter(
            sampleRate: 16_000,
            silenceDuration: TimeSpan.FromMilliseconds(200),
            minimumSpeechDuration: TimeSpan.FromMilliseconds(100));

        segmenter.Push(CreatePcm(milliseconds: 100, amplitude: 8_000))
            .Should().BeNull();
        segmenter.Push(CreatePcm(milliseconds: 100, amplitude: 0))
            .Should().BeNull();

        byte[]? phrase = segmenter.Push(
            CreatePcm(milliseconds: 100, amplitude: 0));

        phrase.Should().NotBeNull();
        phrase!.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SilenceWithoutSpeechDoesNotCreatePhrase()
    {
        var segmenter = new Pcm16PhraseSegmenter(
            sampleRate: 16_000,
            silenceDuration: TimeSpan.FromMilliseconds(200),
            minimumSpeechDuration: TimeSpan.FromMilliseconds(100));

        byte[]? phrase = segmenter.Push(
            CreatePcm(milliseconds: 500, amplitude: 0));

        phrase.Should().BeNull();
    }

    [Fact]
    public void ResetDiscardsBufferedSpeech()
    {
        var segmenter = new Pcm16PhraseSegmenter(
            sampleRate: 16_000,
            silenceDuration: TimeSpan.FromMilliseconds(200),
            minimumSpeechDuration: TimeSpan.FromMilliseconds(100));
        segmenter.Push(CreatePcm(milliseconds: 100, amplitude: 8_000));

        segmenter.Reset();
        byte[]? phrase = segmenter.Push(
            CreatePcm(milliseconds: 300, amplitude: 0));

        phrase.Should().BeNull();
    }

    private static byte[] CreatePcm(int milliseconds, short amplitude)
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
