using FluentAssertions;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;

namespace VoiceTranslator.UnitTests.Orchestration;

public sealed class OutputChannelTestServiceTests
{
    [Fact]
    public void TestPlaysPayloadAndStopsSink()
    {
        var sink = new RecordingSink();
        OutputChannelTestResult result = OutputChannelTestService.Test(
            sink,
            [1, 2, 3, 4]);

        result.Passed.Should().BeTrue();
        sink.Played.Should().Equal(1, 2, 3, 4);
        sink.Stopped.Should().BeTrue();
    }

    [Fact]
    public void TestReportsWarningWhenSinkFails()
    {
        OutputChannelTestResult result = OutputChannelTestService.Test(
            new RecordingSink(fail: true),
            [1]);

        result.Passed.Should().BeFalse();
        result.Warning.Should().Contain("Не удалось");
    }

    private sealed class RecordingSink(bool fail = false) : IAudioPlaybackSink
    {
        public byte[] Played { get; private set; } = [];
        public bool Stopped { get; private set; }

        public void Play(byte[] pcm)
        {
            if (fail)
            {
                throw new InvalidOperationException("device feedback risk");
            }

            Played = pcm;
        }

        public void StopPlayback()
        {
            Stopped = true;
        }
    }
}
