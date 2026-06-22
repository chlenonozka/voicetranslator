namespace VoiceTranslator.Infrastructure.LocalWorker;

public interface IWorkerProcessStarter
{
    IWorkerProcess Start(WorkerProcessOptions options);
}
