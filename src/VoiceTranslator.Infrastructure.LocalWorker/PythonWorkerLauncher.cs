using System.Collections.Concurrent;

namespace VoiceTranslator.Infrastructure.LocalWorker;

public sealed class PythonWorkerLauncher : IWorkerLauncher
{
    private static readonly TimeSpan DefaultGracefulStopTimeout =
        TimeSpan.FromSeconds(2);
    private readonly IWorkerProcessStarter processStarter;
    private readonly string pythonExecutable;
    private readonly string workerDirectory;
    private readonly TimeSpan gracefulStopTimeout;
    private readonly ConcurrentDictionary<int, IWorkerProcess> processes = [];

    public PythonWorkerLauncher(
        IWorkerProcessStarter processStarter,
        string pythonExecutable,
        string workerDirectory,
        TimeSpan? gracefulStopTimeout = null)
    {
        this.processStarter = processStarter;
        this.pythonExecutable = pythonExecutable;
        this.workerDirectory = workerDirectory;
        this.gracefulStopTimeout =
            gracefulStopTimeout ?? DefaultGracefulStopTimeout;
    }

    public Task<LaunchedWorker> LaunchAsync(
        WorkerLaunchRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new WorkerProcessOptions(
            pythonExecutable,
            workerDirectory,
            [
                "-m",
                "voice_translator_worker.main",
                "--host",
                request.Endpoint.Host,
                "--port",
                request.Endpoint.Port.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            ],
            CreateNoWindow: true,
            UseShellExecute: false,
            new Dictionary<string, string>
            {
                ["VOICE_TRANSLATOR_WORKER_TOKEN"] = request.Token,
            });
        var process = processStarter.Start(options);

        if (!processes.TryAdd(process.Id, process))
        {
            process.Dispose();
            throw new InvalidOperationException(
                $"Worker process {process.Id} is already tracked.");
        }

        return Task.FromResult(
            new LaunchedWorker(
                process.Id,
                request.Endpoint,
                "balanced"));
    }

    public async Task StopAsync(
        LaunchedWorker worker,
        CancellationToken cancellationToken)
    {
        if (!processes.TryRemove(worker.ProcessId, out var process))
        {
            return;
        }

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                   cancellationToken))
        {
            timeout.CancelAfter(gracefulStopTimeout);

            try
            {
                if (!process.HasExited)
                {
                    await process
                        .WaitForExitAsync(timeout.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.KillTree();
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    public Task WaitForExitAsync(
        LaunchedWorker worker,
        CancellationToken cancellationToken)
    {
        if (!processes.TryGetValue(worker.ProcessId, out var process))
        {
            return Task.CompletedTask;
        }

        return process.WaitForExitAsync(cancellationToken);
    }
}
