using System.Threading.Channels;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Infrastructure.Audio.Capture;

namespace VoiceTranslator.App.Services;

public sealed class DesktopTranslationSession : IAsyncDisposable
{
    private readonly ILocalTranslationWorker worker;
    private readonly IAudioCaptureSource capture;
    private readonly IAudioPlaybackSink output;
    private readonly string targetLanguage;
    private readonly ISessionFailureObserver? failureObserver;
    private readonly Pcm16PhraseSegmenter segmenter = new(
        sampleRate: 16_000,
        silenceDuration: TimeSpan.FromMilliseconds(500),
        minimumSpeechDuration: TimeSpan.FromMilliseconds(300));
    private readonly Channel<byte[]> completedPhrases =
        Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });
    private readonly CancellationTokenSource cancellation = new();
    private TranslationPipeline? pipeline;
    private Task? consumer;
    private Guid? speakerSessionId;
    private int started;
    private int stopped;

    public DesktopTranslationSession(
        ILocalTranslationWorker worker,
        IAudioCaptureSource capture,
        IAudioPlaybackSink output,
        string targetLanguage,
        ISessionFailureObserver? failureObserver = null)
    {
        this.worker = worker;
        this.capture = capture;
        this.output = output;
        this.targetLanguage = targetLanguage;
        this.failureObserver = failureObserver;
    }

    public event Action<Exception>? Failed;

    public event Action<double>? InputLevelChanged;

    public event Action<double>? OutputLevelChanged;

    public event Action<string>? ActivityChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref stopped) != 0,
            this);
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            return;
        }

        capture.AudioAvailable += OnAudioAvailable;
        consumer = ConsumeAsync(cancellation.Token);
        ActivityChanged?.Invoke(
            "Listening. First completed phrase becomes the voice reference.");
        capture.StartCapture();
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
        {
            return;
        }

        capture.AudioAvailable -= OnAudioAvailable;
        capture.StopCapture();
        completedPhrases.Writer.TryComplete();
        await cancellation.CancelAsync().ConfigureAwait(false);

        if (consumer is not null)
        {
            try
            {
                await consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (pipeline is not null)
        {
            await pipeline
                .StopSessionAsync(CancellationToken.None)
                .ConfigureAwait(false);
            pipeline.Dispose();
            pipeline = null;
        }

        if (speakerSessionId is Guid sessionId)
        {
            await worker
                .DeleteSpeakerSessionAsync(
                    sessionId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            speakerSessionId = null;
        }

        segmenter.Reset();
        output.StopPlayback();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        capture.Dispose();
        if (output is IDisposable disposableOutput)
        {
            disposableOutput.Dispose();
        }
        cancellation.Dispose();
    }

    private void OnAudioAvailable(
        object? sender,
        AudioAvailableEventArgs eventArgs)
    {
        try
        {
            byte[] normalized = Pcm16Normalizer.Normalize(
                eventArgs.Audio,
                capture.WaveFormat);
            InputLevelChanged?.Invoke(CalculatePcm16LevelPercent(normalized));
            byte[]? phrase = segmenter.Push(normalized);
            if (phrase is not null)
            {
                ActivityChanged?.Invoke("Phrase captured.");
                _ = completedPhrases.Writer.TryWrite(phrase);
            }
        }
        catch (Exception error)
        {
            Failed?.Invoke(error);
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (byte[] pcm in completedPhrases.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (speakerSessionId is null)
                {
                    ActivityChanged?.Invoke("Creating voice reference.");
                    speakerSessionId = await worker
                        .CreateSpeakerSessionAsync(
                            WaveMemoryCodec.EncodeWorkerWave(pcm),
                            cancellationToken)
                        .ConfigureAwait(false);
                    pipeline = new TranslationPipeline(
                        new SessionPhraseTranslationWorker(
                            worker,
                            speakerSessionId.Value,
                            targetLanguage),
                        new ReportingPlaybackSink(
                            output,
                            OutputLevelChanged,
                            ActivityChanged),
                        queueCapacity: 2,
                        failureObserver: failureObserver);
                    ActivityChanged?.Invoke(
                        "Voice reference ready. Speak the next Russian phrase to translate.");
                    continue;
                }

                ActivityChanged?.Invoke("Translating phrase.");
                pipeline!.Enqueue(new Phrase(
                    Guid.NewGuid().ToString("D"),
                    pcm));
                await pipeline
                    .ProcessQueuedAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Failed?.Invoke(error);
        }
    }

    private static double CalculatePcm16LevelPercent(byte[] pcm)
    {
        if (pcm.Length < sizeof(short))
        {
            return 0;
        }

        double sumSquares = 0;
        int sampleCount = pcm.Length / sizeof(short);
        for (int index = 0; index < sampleCount; index++)
        {
            short sample = BitConverter.ToInt16(
                pcm,
                index * sizeof(short));
            double normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        double rms = Math.Sqrt(sumSquares / sampleCount);
        return Math.Clamp(rms * 200, 0, 100);
    }

    private sealed class ReportingPlaybackSink(
        IAudioPlaybackSink inner,
        Action<double>? outputLevelChanged,
        Action<string>? activityChanged) : IAudioPlaybackSink
    {
        public void Play(byte[] pcm)
        {
            outputLevelChanged?.Invoke(
                CalculatePcm16LevelPercent(pcm));
            activityChanged?.Invoke("Playing translated speech.");
            inner.Play(pcm);
        }

        public void StopPlayback()
        {
            outputLevelChanged?.Invoke(0);
            inner.StopPlayback();
        }
    }
}
