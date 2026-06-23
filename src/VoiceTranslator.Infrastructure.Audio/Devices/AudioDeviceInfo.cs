namespace VoiceTranslator.Infrastructure.Audio.Devices;

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsVirtual)
{
    public override string ToString() => Name;
}
