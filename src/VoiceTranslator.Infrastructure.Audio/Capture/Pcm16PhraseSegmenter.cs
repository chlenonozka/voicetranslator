using System.Buffers;
using System.Buffers.Binary;

namespace VoiceTranslator.Infrastructure.Audio.Capture;

public sealed class Pcm16PhraseSegmenter
{
    private const int BytesPerSample = sizeof(short);
    private const int SpeechThreshold = 500;
    private readonly int silenceBytesRequired;
    private readonly int minimumSpeechBytes;
    private readonly int maximumPhraseBytes;
    private readonly ArrayBufferWriter<byte> phrase = new();
    private int speechBytes;
    private int trailingSilenceBytes;

    public Pcm16PhraseSegmenter(
        int sampleRate,
        TimeSpan silenceDuration,
        TimeSpan minimumSpeechDuration,
        TimeSpan? maximumPhraseDuration = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            silenceDuration,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            minimumSpeechDuration,
            TimeSpan.Zero);

        silenceBytesRequired = DurationToBytes(
            sampleRate,
            silenceDuration);
        minimumSpeechBytes = DurationToBytes(
            sampleRate,
            minimumSpeechDuration);
        maximumPhraseBytes = DurationToBytes(
            sampleRate,
            maximumPhraseDuration ?? TimeSpan.FromSeconds(15));
    }

    public byte[]? Push(ReadOnlySpan<byte> pcm16)
    {
        if (pcm16.Length % BytesPerSample != 0)
        {
            throw new ArgumentException(
                "PCM16 data must contain complete samples.",
                nameof(pcm16));
        }

        bool containsSpeech = ContainsSpeech(pcm16);
        if (containsSpeech)
        {
            phrase.Write(pcm16);
            speechBytes += pcm16.Length;
            trailingSilenceBytes = 0;
        }
        else if (phrase.WrittenCount > 0)
        {
            phrase.Write(pcm16);
            trailingSilenceBytes += pcm16.Length;
        }

        if (phrase.WrittenCount >= maximumPhraseBytes)
        {
            return CompleteOrDiscard();
        }

        return trailingSilenceBytes >= silenceBytesRequired
            ? CompleteOrDiscard()
            : null;
    }

    public void Reset()
    {
        phrase.Clear();
        speechBytes = 0;
        trailingSilenceBytes = 0;
    }

    private byte[]? CompleteOrDiscard()
    {
        byte[]? completed = speechBytes >= minimumSpeechBytes
            ? phrase.WrittenSpan.ToArray()
            : null;
        Reset();
        return completed;
    }

    private static bool ContainsSpeech(ReadOnlySpan<byte> pcm16)
    {
        for (int offset = 0; offset < pcm16.Length; offset += BytesPerSample)
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(
                pcm16[offset..]);
            if (Math.Abs((int)sample) >= SpeechThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private static int DurationToBytes(
        int sampleRate,
        TimeSpan duration)
    {
        return checked((int)Math.Ceiling(
            sampleRate * duration.TotalSeconds * BytesPerSample));
    }
}
