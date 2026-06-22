namespace VoiceTranslator.Testing;

public static class PrivacySafeTestData
{
    public static readonly Guid SessionId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public const string SourceLocale = "en-US";

    public const string TargetLanguage = "ru";

    public const string DeviceId = "test-device-id";

    public static DateTimeOffset Now =>
        new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
}
