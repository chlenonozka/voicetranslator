namespace VoiceTranslator.Infrastructure.LocalWorker;

public sealed record WorkerProcessOptions(
    string FileName,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    bool CreateNoWindow,
    bool UseShellExecute,
    IReadOnlyDictionary<string, string> Environment);
