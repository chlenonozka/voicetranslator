using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceTranslator.Infrastructure.Audio.Capture;

public sealed class AudioAvailableEventArgs(byte[] audio) : EventArgs
{
    public byte[] Audio { get; } = audio;
}

public interface IAudioCaptureSource : IDisposable
{
    WaveFormat WaveFormat { get; }

    event EventHandler<AudioAvailableEventArgs>? AudioAvailable;

    void StartCapture();

    void StopCapture();
}

public sealed class WasapiMicrophoneCapture : IAudioCaptureSource
{
    private readonly IWaveIn capture;

    public WasapiMicrophoneCapture(IWaveIn capture)
    {
        this.capture = capture;
        this.capture.DataAvailable += OnDataAvailable;
    }

    public WasapiMicrophoneCapture(MMDevice device)
        : this(new WasapiCapture(device))
    {
    }

    public WaveFormat WaveFormat => capture.WaveFormat;

    public event EventHandler<AudioAvailableEventArgs>? AudioAvailable;

    public void Start() => capture.StartRecording();

    public void Stop() => capture.StopRecording();

    void IAudioCaptureSource.StartCapture() => Start();

    void IAudioCaptureSource.StopCapture() => Stop();

    public void Dispose()
    {
        capture.DataAvailable -= OnDataAvailable;
        capture.Dispose();
    }

    private void OnDataAvailable(
        object? sender,
        WaveInEventArgs eventArgs)
    {
        var audio = eventArgs.Buffer.AsSpan(
            0,
            eventArgs.BytesRecorded).ToArray();
        AudioAvailable?.Invoke(
            this,
            new AudioAvailableEventArgs(audio));
    }
}
