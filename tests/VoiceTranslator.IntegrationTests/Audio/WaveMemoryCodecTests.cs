using FluentAssertions;
using NAudio.Wave;
using VoiceTranslator.Infrastructure.Audio.Capture;

namespace VoiceTranslator.IntegrationTests.Audio;

public sealed class WaveMemoryCodecTests
{
    [Fact]
    public void EncodeWorkerWaveProducesMono16KhzPcmWave()
    {
        byte[] pcm = [1, 0, 2, 0, 3, 0];

        byte[] wav = WaveMemoryCodec.EncodeWorkerWave(pcm);

        using var stream = new MemoryStream(wav, writable: false);
        using var reader = new WaveFileReader(stream);
        reader.WaveFormat.SampleRate.Should().Be(16_000);
        reader.WaveFormat.Channels.Should().Be(1);
        reader.WaveFormat.BitsPerSample.Should().Be(16);
        var decoded = new byte[pcm.Length];
        reader.ReadExactly(decoded);
        decoded.Should().Equal(pcm);
    }

    [Fact]
    public void DecodeSynthesizedWaveReturnsMono24KhzPcm16()
    {
        byte[] sourcePcm = [1, 0, 2, 0, 3, 0, 4, 0];
        using var wave = new MemoryStream();
        using (var writer = new WaveFileWriter(
            wave,
            new WaveFormat(24_000, 16, 1)))
        {
            writer.Write(sourcePcm);
        }

        byte[] pcm = WaveMemoryCodec.DecodeSynthesizedWave(wave.ToArray());

        pcm.Should().Equal(sourcePcm);
    }
}
