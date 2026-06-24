using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;

namespace VoiceTranslator.Infrastructure.Audio.Devices;

public sealed class CoreAudioEndpointSource : IAudioEndpointSource
{
    private readonly MMDeviceEnumerator enumerator = new();

    public IReadOnlyList<AudioDeviceInfo> EnumerateCaptureDevices() =>
        Enumerate(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceInfo> EnumerateRenderDevices() =>
        Enumerate(DataFlow.Render);

    public void Dispose() => enumerator.Dispose();

    private AudioDeviceInfo[] Enumerate(DataFlow flow)
    {
        string? defaultDeviceId = GetDefaultDeviceId(flow);
        return enumerator
            .EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(device => new AudioDeviceInfo(
                device.ID,
                device.FriendlyName,
                IsVirtualDevice(device.FriendlyName),
                IsDefault: device.ID == defaultDeviceId))
            .OrderBy(device => device.Name, StringComparer.CurrentCulture)
            .ToArray();
    }

    private string? GetDefaultDeviceId(DataFlow flow)
    {
        try
        {
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(
                flow,
                Role.Multimedia);
            return device.ID;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static bool IsVirtualDevice(string name)
    {
        return name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Virtual Cable", StringComparison.OrdinalIgnoreCase)
            || name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)
            || name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase);
    }
}
