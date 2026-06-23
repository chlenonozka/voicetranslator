namespace VoiceTranslator.Infrastructure.Audio.Devices;

public interface IAudioEndpointSource : IDisposable
{
    IReadOnlyList<AudioDeviceInfo> EnumerateCaptureDevices();

    IReadOnlyList<AudioDeviceInfo> EnumerateRenderDevices();
}
