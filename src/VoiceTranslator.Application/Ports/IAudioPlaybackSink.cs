namespace VoiceTranslator.Application.Ports;

public interface IAudioPlaybackSink
{
    void Play(byte[] pcm);

    Task PlayAsync(
        byte[] pcm,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Play(pcm);
        return Task.CompletedTask;
    }

    void StopPlayback();
}
