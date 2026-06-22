namespace VoiceTranslator.Application.Ports;

public interface IAudioPlaybackSink
{
    void Play(byte[] pcm);

    void StopPlayback();
}
