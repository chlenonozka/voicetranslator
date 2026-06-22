using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceTranslator.Infrastructure.Audio.Capture;

public static class Pcm16Normalizer
{
    private const int WorkerSampleRate = 16_000;

    public static byte[] Normalize(
        byte[] source,
        WaveFormat sourceFormat)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceFormat);

        if (sourceFormat.SampleRate == WorkerSampleRate
            && sourceFormat.Channels == 1
            && sourceFormat.BitsPerSample == 16
            && sourceFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            return (byte[])source.Clone();
        }

        using var sourceStream = new MemoryStream(source, writable: false);
        using var rawStream = new RawSourceWaveStream(
            sourceStream,
            sourceFormat);
        ISampleProvider samples = rawStream.ToSampleProvider();
        samples = samples.WaveFormat.Channels switch
        {
            1 => samples,
            2 => new StereoToMonoSampleProvider(samples),
            _ => throw new NotSupportedException(
                "Only mono and stereo microphone input is supported."),
        };

        if (samples.WaveFormat.SampleRate != WorkerSampleRate)
        {
            samples = new WdlResamplingSampleProvider(
                samples,
                WorkerSampleRate);
        }

        var pcm16 = new SampleToWaveProvider16(samples);
        using var output = new MemoryStream();
        var buffer = new byte[WorkerSampleRate / 2];
        int bytesRead;
        while ((bytesRead = pcm16.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, bytesRead);
        }

        return output.ToArray();
    }
}
