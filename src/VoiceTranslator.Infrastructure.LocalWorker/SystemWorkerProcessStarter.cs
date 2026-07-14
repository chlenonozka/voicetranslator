using System.Diagnostics;

namespace VoiceTranslator.Infrastructure.LocalWorker;

public sealed class SystemWorkerProcessStarter : IWorkerProcessStarter
{
    public IWorkerProcess Start(WorkerProcessOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.FileName,
            WorkingDirectory = options.WorkingDirectory,
            CreateNoWindow = options.CreateNoWindow,
            UseShellExecute = options.UseShellExecute,
        };

        foreach (var argument in options.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var variable in options.Environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Python worker process could not be started.");
        try
        {
            return new SystemWorkerProcess(
                process,
                WindowsProcessJob.CreateAndAssign(process));
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
            throw;
        }
    }

    private sealed class SystemWorkerProcess(
        Process process,
        WindowsProcessJob processJob) : IWorkerProcess
    {
        public int Id => process.Id;
        public bool HasExited => process.HasExited;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);

        public void KillTree() => process.Kill(entireProcessTree: true);

        public void Dispose()
        {
            process.Dispose();
            processJob.Dispose();
        }
    }
}
