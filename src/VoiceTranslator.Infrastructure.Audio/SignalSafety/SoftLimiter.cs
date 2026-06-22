namespace VoiceTranslator.Infrastructure.Audio.SignalSafety;

public static class SoftLimiter
{
    public static void Process(Span<float> samples)
    {
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = Math.Clamp(samples[index], -1.0f, 1.0f);
        }
    }
}
