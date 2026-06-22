using FluentAssertions;
using VoiceTranslator.Infrastructure.LocalWorker;

namespace VoiceTranslator.IntegrationTests.Worker;

public sealed class PythonWorkerLauncherTests
{
    [Fact]
    public async Task LaunchAsyncPassesTokenThroughHiddenChildEnvironment()
    {
        var process = new FakeWorkerProcess(42);
        var starter = new FakeWorkerProcessStarter(process);
        var launcher = new PythonWorkerLauncher(
            starter,
            @"C:\worker\.venv\Scripts\python.exe",
            @"C:\worker");
        var request = new WorkerLaunchRequest(
            new Uri("http://127.0.0.1:8765"),
            "secret-token");

        var launched = await launcher.LaunchAsync(
            request,
            CancellationToken.None);

        launched.ProcessId.Should().Be(42);
        starter.Options.Should().NotBeNull();
        starter.Options!.CreateNoWindow.Should().BeTrue();
        starter.Options.UseShellExecute.Should().BeFalse();
        starter.Options.Environment["VOICE_TRANSLATOR_WORKER_TOKEN"]
            .Should().Be("secret-token");
        starter.Options.Arguments.Should().Contain(
            "voice_translator_worker.main");
        starter.Options.Arguments.Should().Contain("8765");
        starter.Options.Arguments.Should().NotContain("secret-token");
    }

    [Fact]
    public async Task StopAsyncKillsProcessTreeAfterGracefulTimeout()
    {
        var process = new FakeWorkerProcess(42)
        {
            WaitUntilCancelled = true,
        };
        var launcher = new PythonWorkerLauncher(
            new FakeWorkerProcessStarter(process),
            @"C:\worker\.venv\Scripts\python.exe",
            @"C:\worker",
            TimeSpan.Zero);
        var launched = await launcher.LaunchAsync(
            new WorkerLaunchRequest(
                new Uri("http://127.0.0.1:8765"),
                "secret-token"),
            CancellationToken.None);

        await launcher.StopAsync(launched, CancellationToken.None);

        process.KilledTree.Should().BeTrue();
        process.Disposed.Should().BeTrue();
    }

    private sealed class FakeWorkerProcessStarter(
        IWorkerProcess process) : IWorkerProcessStarter
    {
        public WorkerProcessOptions? Options { get; private set; }

        public IWorkerProcess Start(WorkerProcessOptions options)
        {
            Options = options;
            return process;
        }
    }

    private sealed class FakeWorkerProcess(int id) : IWorkerProcess
    {
        public int Id { get; } = id;
        public bool HasExited { get; private set; }
        public bool WaitUntilCancelled { get; init; }
        public bool KilledTree { get; private set; }
        public bool Disposed { get; private set; }

        public async Task WaitForExitAsync(
            CancellationToken cancellationToken)
        {
            if (WaitUntilCancelled)
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }

            HasExited = true;
        }

        public void KillTree()
        {
            KilledTree = true;
            HasExited = true;
        }

        public void Dispose() => Disposed = true;
    }
}
