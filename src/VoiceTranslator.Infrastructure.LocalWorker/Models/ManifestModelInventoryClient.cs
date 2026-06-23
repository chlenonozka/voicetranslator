using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Domain.Models;

namespace VoiceTranslator.Infrastructure.LocalWorker.Models;

public sealed class ManifestModelInventoryClient : IModelInventoryClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly string manifestDirectory;
    private readonly string modelCacheDirectory;
    private readonly Func<ProcessStartInfo, IProcessHandle> processStarter;

    public ManifestModelInventoryClient(
        string manifestDirectory,
        string modelCacheDirectory,
        Func<ProcessStartInfo, IProcessHandle>? processStarter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelCacheDirectory);
        this.manifestDirectory = manifestDirectory;
        this.modelCacheDirectory = modelCacheDirectory;
        this.processStarter = processStarter ?? StartProcess;
    }

    public Task<ModelInventoryReport> GetInventoryAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var models = Directory
            .EnumerateFiles(manifestDirectory, "*.json")
            .Select(ReadManifest)
            .OrderBy(manifest => manifest.ModelId, StringComparer.Ordinal)
            .Select(manifest => new ModelInventoryItem(
                manifest,
                Installed: IsInstalled(manifest),
                InstalledAt: ReadInstalledAt(manifest)))
            .ToArray();
        return Task.FromResult(new ModelInventoryReport(models));
    }

    public async IAsyncEnumerable<ModelDownloadProgress> DownloadModelsAsync(
        bool acceptNoncommercial,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!acceptNoncommercial)
        {
            throw new InvalidOperationException(
                "Noncommercial model license acknowledgement is required.");
        }

        yield return new ModelDownloadProgress(
            "starting",
            ModelId: null,
            Fraction: 0);

        var startInfo = new ProcessStartInfo
        {
            FileName = "uv",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(
            Directory.GetParent(manifestDirectory)?.Parent?.FullName
                ?? ".",
            "worker"));
        startInfo.ArgumentList.Add("--extra");
        startInfo.ArgumentList.Add("ml");
        startInfo.ArgumentList.Add("voice-translator-models");
        startInfo.ArgumentList.Add("--accept-noncommercial");

        using IProcessHandle process = processStarter(startInfo);
        await process.WaitForExitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Model download failed with exit code {process.ExitCode}.");
        }

        yield return new ModelDownloadProgress(
            "completed",
            ModelId: null,
            Fraction: 1);
    }

    private ModelManifest ReadManifest(string path)
    {
        var payload = JsonSerializer.Deserialize<ManifestPayload>(
                File.ReadAllText(path),
                JsonOptions)
            ?? throw new InvalidOperationException(
                $"Manifest {path} is empty.");
        var manifest = new ModelManifest(
            payload.Id,
            payload.RepositoryId,
            payload.Revision,
            payload.License,
            payload.CommercialUseAllowed,
            payload.Files);
        manifest.Validate();
        return manifest;
    }

    private bool IsInstalled(ModelManifest manifest)
    {
        string receiptPath = ReceiptPath(manifest);
        return File.Exists(receiptPath);
    }

    private DateTimeOffset? ReadInstalledAt(ModelManifest manifest)
    {
        string receiptPath = ReceiptPath(manifest);
        return File.Exists(receiptPath)
            ? File.GetLastWriteTimeUtc(receiptPath)
            : null;
    }

    private string ReceiptPath(ModelManifest manifest)
    {
        return Path.Combine(
            modelCacheDirectory,
            manifest.ModelId,
            "receipt.json");
    }

    private static ProcessHandle StartProcess(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Could not start model download process.");
        return new ProcessHandle(process);
    }

    public interface IProcessHandle : IDisposable
    {
        int ExitCode { get; }

        Task WaitForExitAsync(CancellationToken cancellationToken);
    }

    private sealed class ProcessHandle(Process process) : IProcessHandle
    {
        public int ExitCode => process.ExitCode;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);

        public void Dispose() => process.Dispose();
    }

    private sealed record ManifestPayload(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("repo_id")] string RepositoryId,
        [property: JsonPropertyName("revision")] string Revision,
        [property: JsonPropertyName("license")] string License,
        [property: JsonPropertyName("commercial_use_allowed")] bool CommercialUseAllowed,
        [property: JsonPropertyName("files")] IReadOnlyList<string> Files);
}
