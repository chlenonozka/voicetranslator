using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Infrastructure.LocalWorker;

var workspaceRoot = FindWorkspaceRoot(AppContext.BaseDirectory);
var workerDirectory = Path.Combine(workspaceRoot, "worker");
var pythonExecutable = Path.Combine(
    workerDirectory,
    ".venv",
    "Scripts",
    "python.exe");

if (!File.Exists(pythonExecutable))
{
    Console.Error.WriteLine(
        $"Python worker runtime was not found at {pythonExecutable}.");
    return 2;
}

var endpoint = new Uri(
    Environment.GetEnvironmentVariable("VOICE_TRANSLATOR_WORKER_ENDPOINT")
    ?? "http://127.0.0.1:8765");
var launcher = new PythonWorkerLauncher(
    new SystemWorkerProcessStarter(),
    pythonExecutable,
    workerDirectory);
var healthProbe = new LocalWorkerHealthProbe(
    (workerEndpoint, token) => new LocalWorkerClient(
        new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2),
        },
        workerEndpoint,
        token));
var shutdown = new VoiceTranslator.WorkerHost.WorkerHostShutdownSignal();
var failureCoordinator = new SessionFailureCoordinator(shutdown);

await using var manager = new WorkerProcessManager(
    launcher,
    healthProbe,
    endpoint,
    failureObserver: failureCoordinator);
var handle = await manager.StartAsync(CancellationToken.None);

Console.WriteLine(
    $"Worker ready at {handle.Endpoint} ({handle.PerformanceProfile}).");
Console.WriteLine("Press Ctrl+C to stop.");

var stopped = new TaskCompletionSource(
    TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.TrySetResult();
};
Task completed = await Task.WhenAny(
    stopped.Task,
    shutdown.Completion);
if (ReferenceEquals(completed, shutdown.Completion))
{
    Console.Error.WriteLine(
        $"Worker failure: {failureCoordinator.Failure}; restart required.");
    return 1;
}

return 0;

static string FindWorkspaceRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "worker")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException(
        "Could not locate the worker directory.");
}
