using System.Buffers.Binary;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceTranslator.Infrastructure.Audio.SignalSafety;

namespace VoiceTranslator.Infrastructure.Audio.Capture;

public static class WaveMemoryCodec
{
    private const int WorkerSampleRate = 16_000;
    private const int SynthesisSampleRate = 24_000;

    public static byte[] EncodeWorkerWave(byte[] pcm16)
    {
        ArgumentNullException.ThrowIfNull(pcm16);

        using var output = new MemoryStream();
        using (var writer = new WaveFileWriter(
            output,
            new WaveFormat(WorkerSampleRate, 16, 1)))
        {
            writer.Write(pcm16);
        }

        return output.ToArray();
    }

    public static byte[] DecodeSynthesizedWave(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);

        using var input = new MemoryStream(wav, writable: false);
        using var reader = new WaveFileReader(input);
        ISampleProvider samples = reader.ToSampleProvider();
        samples = samples.WaveFormat.Channels switch
        {
            1 => samples,
            2 => new StereoToMonoSampleProvider(samples),
            _ => throw new NotSupportedException(
                "Only mono and stereo synthesized audio is supported."),
        };
        if (samples.WaveFormat.SampleRate != SynthesisSampleRate)
        {
            samples = new WdlResamplingSampleProvider(
                samples,
                SynthesisSampleRate);
        }

        using var output = new MemoryStream();
        var sampleBuffer = new float[SynthesisSampleRate / 2];
        var pcmBuffer = new byte[sampleBuffer.Length * sizeof(short)];
        int samplesRead;
        while ((samplesRead = samples.Read(
            sampleBuffer,
            0,
            sampleBuffer.Length)) > 0)
        {
            SoftLimiter.Process(sampleBuffer.AsSpan(0, samplesRead));
            for (int index = 0; index < samplesRead; index++)
            {
                short sample = (short)Math.Round(
                    sampleBuffer[index] * short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(
                    pcmBuffer.AsSpan(
                        index * sizeof(short),
                        sizeof(short)),
                    sample);
            }
            output.Write(pcmBuffer, 0, samplesRead * sizeof(short));
        }

        return output.ToArray();
    }
}
