using FluentAssertions;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.WorkerHost;

namespace VoiceTranslator.IntegrationTests.Recovery;

public sealed class WorkerHostCompositionTests
{
    [Fact]
    public async Task FailureCoordinatorSignalsHostShutdownWithoutStoppingManagerInline()
    {
        var shutdown = new WorkerHostShutdownSignal();
        var coordinator = new SessionFailureCoordinator(shutdown);

        await coordinator.OnSessionFailureAsync(
            SessionFailure.WorkerExited,
            CancellationToken.None);
        await shutdown.Completion
            .WaitAsync(TimeSpan.FromSeconds(1));

        coordinator.Failure.Should().Be(SessionFailure.WorkerExited);
        coordinator.State.Should().Be(SessionFailureState.RestartRequired);
    }
}
