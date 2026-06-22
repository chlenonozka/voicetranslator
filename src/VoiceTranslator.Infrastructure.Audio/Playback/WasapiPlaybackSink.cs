using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.Infrastructure.Audio.Playback;

public sealed class WasapiPlaybackSink : IAudioPlaybackSink, IDisposable
{
    private readonly IWavePlayer output;
    private readonly BufferedWaveProvider buffer;

    public WasapiPlaybackSink(
        IWavePlayer output,
        WaveFormat waveFormat)
    {
        this.output = output;
        buffer = new BufferedWaveProvider(waveFormat)
        {
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
        buffer.AddSamples(pcm, 0, pcm.Length);
        if (output.PlaybackState != PlaybackState.Playing)
        {
            output.Play();
        }
    }

    public void Stop()
    {
        output.Stop();
        buffer.ClearBuffer();
    }

    public void StopPlayback() => Stop();

    public void Dispose()
    {
        Stop();
        output.Dispose();
    }
}
