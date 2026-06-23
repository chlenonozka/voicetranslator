namespace VoiceTranslator.Infrastructure.Audio.Devices;

public sealed class WasapiDeviceCatalog : IDisposable
{
    private readonly IAudioEndpointSource source;

    public WasapiDeviceCatalog(IAudioEndpointSource source)
    {
        this.source = source;
    }

    public IReadOnlyList<AudioDeviceInfo> CaptureDevices { get; private set; } =
        [];

    public IReadOnlyList<AudioDeviceInfo> RenderDevices { get; private set; } =
        [];

    public event EventHandler? DevicesChanged;

    public void Refresh()
    {
        IReadOnlyList<AudioDeviceInfo> captures =
            source.EnumerateCaptureDevices();
        IReadOnlyList<AudioDeviceInfo> renders =
            source.EnumerateRenderDevices();
        if (
            CaptureDevices.SequenceEqual(captures)
            && RenderDevices.SequenceEqual(renders)
        )
        {
            return;
        }

        CaptureDevices = captures;
        RenderDevices = renders;
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => source.Dispose();
}
