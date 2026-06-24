namespace VoiceTranslator.Infrastructure.Audio.Devices;

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsVirtual,
    bool IsDefault = false)
{
    public override string ToString() => Name;
}
