using VoiceTranslator.Application.Ports;
using VoiceTranslator.Domain.Audio;

namespace VoiceTranslator.Infrastructure.Audio.Routing;

public sealed class AudioOutputRoutingSink : IAudioPlaybackSink, IDisposable
{
    private readonly AudioOutputRouter router;
    private readonly IDisposable? physicalDisposable;
    private readonly IDisposable? virtualDisposable;
    private int disposed;

    public AudioOutputRoutingSink(
        ISynthesizedAudioSink physicalSink,
        ISynthesizedAudioSink virtualSink,
        OutputMode mode)
    {
        physicalDisposable = physicalSink as IDisposable;
        virtualDisposable = virtualSink as IDisposable;
        router = new AudioOutputRouter(physicalSink, virtualSink, mode);
    }

    public void Play(byte[] pcm)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        router.RouteSynthesized(SynthesizedPcmPayload.Create(pcm));
    }

    public void StopPlayback()
    {
        router.StopAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        router.StopAsync().GetAwaiter().GetResult();
        physicalDisposable?.Dispose();
        virtualDisposable?.Dispose();
    }
}
