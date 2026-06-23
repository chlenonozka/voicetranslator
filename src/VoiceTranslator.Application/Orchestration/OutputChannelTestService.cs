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
                Warning: "Output channel test has no audio payload.");
        }

        try
        {
            output.Play(testPcm.ToArray());
            return new OutputChannelTestResult(
                Passed: true,
                Warning: null);
        }
        catch (Exception error)
        {
            return new OutputChannelTestResult(
                Passed: false,
                Warning: $"Output channel test failed: {error.Message}");
        }
        finally
        {
            output.StopPlayback();
        }
    }
}
