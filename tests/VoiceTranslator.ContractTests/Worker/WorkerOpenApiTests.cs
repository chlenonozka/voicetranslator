using FluentAssertions;

namespace VoiceTranslator.ContractTests.Worker;

public sealed class WorkerOpenApiTests
{
    [Fact]
    public void ContractContainsAuthenticatedWorkerLifecycleRoutes()
    {
        string contract = File.ReadAllText(FindContract());

        contract.Should().Contain("/v1/health:");
        contract.Should().Contain("/v1/preflight:");
        contract.Should().Contain("/v1/speaker-sessions:");
        contract.Should().Contain("/v1/translate-phrase:");
        contract.Should().Contain("/v1/cancel/{requestId}:");
        contract.Should().Contain("name: X-Worker-Token");
        contract.Should().Contain("name: X-Request-Id");
        contract.Should().Contain("\"507\"");
    }

    private static string FindContract()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(
                directory.FullName,
                "specs",
                "001-realtime-voice-translation",
                "contracts",
                "worker.openapi.yaml");
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate worker.openapi.yaml.");
    }
}
