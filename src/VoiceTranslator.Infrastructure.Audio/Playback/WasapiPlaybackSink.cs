using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Infrastructure.Audio.Routing;

namespace VoiceTranslator.Infrastructure.Audio.Playback;

public sealed class WasapiPlaybackSink :
    IAudioPlaybackSink,
    ISynthesizedAudioSink,
    IDisposable
{
    private static readonly TimeSpan BufferPollInterval =
        TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan DeviceDrainDelay =
        TimeSpan.FromMilliseconds(120);
    private const int FadeMilliseconds = 35;
    private readonly IWavePlayer output;
    private readonly BufferedWaveProvider buffer;
    private readonly WaveFormat waveFormat;
    private readonly SemaphoreSlim playbackGate = new(1, 1);

    public WasapiPlaybackSink(
        IWavePlayer output,
        WaveFormat waveFormat)
    {
        this.output = output;
        this.waveFormat = waveFormat;
        buffer = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(60),
            DiscardOnBufferOverflow = true,
        };
        output.Init(buffer);
    }

    public WasapiPlaybackSink(
        MMDevice device,
        WaveFormat waveFormat)
        : this(
            new WasapiOut(
                device,
                AudioClientShareMode.Shared,
                useEventSync: true,
                latency: 100),
            waveFormat)
    {
    }

    public void Play(byte[] pcm)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        byte[] prepared = PreparePlaybackTail(pcm, waveFormat);
        buffer.AddSamples(prepared, 0, prepared.Length);
        if (output.PlaybackState != PlaybackState.Playing)
        {
            output.Play();
        }
    }

    public async Task PlayAsync(
        byte[] pcm,
        CancellationToken cancellationToken)
    {
        await playbackGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            Play(pcm);
            while (buffer.BufferedBytes > 0)
            {
                await Task
                    .Delay(BufferPollInterval, cancellationToken)
                    .ConfigureAwait(false);
            }

            await Task
                .Delay(DeviceDrainDelay, cancellationToken)
                .ConfigureAwait(false);
            if (output.PlaybackState == PlaybackState.Playing)
            {
                output.Stop();
            }
        }
        finally
        {
            playbackGate.Release();
        }
    }

    public void Stop()
    {
        output.Stop();
        buffer.ClearBuffer();
    }

    public void StopPlayback() => Stop();

    public ValueTask PlayAsync(
        SynthesizedPcmPayload payload,
        CancellationToken cancellationToken)
    {
        return new ValueTask(PlayAsync(
            payload.Pcm.ToArray(),
            cancellationToken));
    }

    public void Dispose()
    {
        Stop();
        output.Dispose();
        playbackGate.Dispose();
    }

    private static byte[] PreparePlaybackTail(
        byte[] pcm,
        WaveFormat format)
    {
        int blockAlign = format.BlockAlign;
        int fadeBytes = format.AverageBytesPerSecond
            * FadeMilliseconds
            / 1_000;
        fadeBytes -= fadeBytes % blockAlign;
        fadeBytes = Math.Min(fadeBytes, pcm.Length - pcm.Length % blockAlign);

        int silenceBytes = (int)Math.Ceiling(
            format.AverageBytesPerSecond
            * DeviceDrainDelay.TotalSeconds);
        silenceBytes += (blockAlign - silenceBytes % blockAlign) % blockAlign;
        var prepared = new byte[pcm.Length + silenceBytes];
        pcm.CopyTo(prepared, 0);

        if (format.BitsPerSample == 16 && fadeBytes >= sizeof(short))
        {
            int fadeStart = pcm.Length - fadeBytes;
            int fadeSamples = fadeBytes / sizeof(short);
            for (int index = 0; index < fadeSamples; index++)
            {
                int offset = fadeStart + index * sizeof(short);
                short sample = BitConverter.ToInt16(prepared, offset);
                double gain = 1.0 - (double)(index + 1) / fadeSamples;
                BitConverter.TryWriteBytes(
                    prepared.AsSpan(offset, sizeof(short)),
                    (short)Math.Round(sample * gain));
            }
        }

        return prepared;
    }
}
