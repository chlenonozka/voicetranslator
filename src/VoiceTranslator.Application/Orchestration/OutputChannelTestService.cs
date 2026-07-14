using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.Application.Orchestration;

public sealed class OutputChannelTestService
{
    public static OutputChannelTestResult Test(
        IAudioPlaybackSink output,
        ReadOnlySpan<byte> testPcm)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (testPcm.IsEmpty)
        {
            return new OutputChannelTestResult(
                Passed: false,
                Warning: "Для проверки канала вывода нет аудиоданных.");
        }

        try
        {
            output.Play(testPcm.ToArray());
            return new OutputChannelTestResult(
                Passed: true,
                Warning: null);
        }
        catch (Exception)
        {
            return new OutputChannelTestResult(
                Passed: false,
                Warning: "Не удалось проверить канал вывода.");
        }
        finally
        {
            output.StopPlayback();
        }
    }
}
