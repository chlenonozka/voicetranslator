using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Channels;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Infrastructure.Audio.Capture;

namespace VoiceTranslator.App.Services;

public sealed class DesktopTranslationSession : IAsyncDisposable, ISessionStopper
{
    private readonly ILocalTranslationWorker worker;
    private readonly IAudioCaptureSource capture;
    private readonly IAudioPlaybackSink output;
    private readonly string targetLanguage;
    private readonly ISessionFailureObserver? failureObserver;
    private readonly Func<byte[], CancellationToken, Task>? referenceCaptured;
    private readonly string performanceProfile;
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
    private CancellationTokenSource? activeProgressAnimation;
    private byte[]? initialReferenceWav;
    private int started;
    private int stopped;

    public DesktopTranslationSession(
        ILocalTranslationWorker worker,
        IAudioCaptureSource capture,
        IAudioPlaybackSink output,
        string targetLanguage,
        ISessionFailureObserver? failureObserver = null,
        byte[]? existingReferenceWav = null,
        Func<byte[], CancellationToken, Task>? referenceCaptured = null,
        string performanceProfile = "balanced")
    {
        this.worker = worker;
        this.capture = capture;
        this.output = output;
        this.targetLanguage = targetLanguage;
        this.failureObserver = failureObserver;
        initialReferenceWav = existingReferenceWav;
        this.referenceCaptured = referenceCaptured;
        this.performanceProfile = performanceProfile;
    }

    public event Action<Exception>? Failed;

    public event Action<double>? InputLevelChanged;

    public event Action<double>? OutputLevelChanged;

    public event Action<string>? ActivityChanged;

    public event Action<int, string>? ProgressChanged;

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
        ActivityChanged?.Invoke(initialReferenceWav is null
            ? "Слушаю. Первая завершённая фраза станет образцом нового голосового профиля."
            : "Загружаю выбранный голосовой профиль. Первая фраза будет переведена.");
        ProgressChanged?.Invoke(0, "Ожидание речи");
        capture.StartCapture();
    }

    public Task StopSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return StopAsync();
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
        StopProgressAnimation();

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
                ActivityChanged?.Invoke("Фраза записана.");
                ProgressChanged?.Invoke(10, "Фраза записана");
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
            byte[]? existingReference = Interlocked.Exchange(
                ref initialReferenceWav,
                null);
            if (existingReference is not null)
            {
                try
                {
                    ActivityChanged?.Invoke("Загружаю голосовой профиль.");
                    await InitializeSpeakerSessionAsync(
                            existingReference,
                            cancellationToken)
                        .ConfigureAwait(false);
                    ProgressChanged?.Invoke(100, "Голосовой профиль готов");
                    ActivityChanged?.Invoke(
                        "Голосовой профиль готов. Слушаю речь для перевода.");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(existingReference);
                }
            }

            await foreach (byte[] pcm in completedPhrases.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (speakerSessionId is null)
                {
                    ActivityChanged?.Invoke("Создаю голосовой профиль.");
                    byte[] referenceWav = WaveMemoryCodec.EncodeWorkerWave(pcm);
                    await InitializeSpeakerSessionAsync(
                            referenceWav,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (referenceCaptured is not null)
                    {
                        await referenceCaptured(referenceWav, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    ProgressChanged?.Invoke(100, "Голосовой профиль готов");
                    ActivityChanged?.Invoke(
                        "Голосовой профиль готов. Произнесите следующую фразу для перевода.");
                    continue;
                }

                ActivityChanged?.Invoke("Обрабатываю фразу.");
                pipeline!.Enqueue(new Phrase(
                    Guid.NewGuid().ToString("D"),
                    pcm));
                try
                {
                    await RunWithAnimatedProgressAsync(
                            () => pipeline.ProcessQueuedAsync(
                                cancellationToken),
                            startPercent: 20,
                            limitPercent: 90,
                            label: "Распознавание и перевод",
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (HttpRequestException error)
                    when (error.StatusCode
                        == System.Net.HttpStatusCode.UnprocessableEntity)
                {
                    ProgressChanged?.Invoke(0, "Фраза не распознана");
                    ActivityChanged?.Invoke(
                        "Фраза не распознана. Говорите немного дольше и сделайте короткую паузу. Сессия продолжает работать.");
                }
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

    private async Task RunWithAnimatedProgressAsync(
        Func<Task> operation,
        int startPercent,
        int limitPercent,
        string label,
        CancellationToken cancellationToken)
    {
        using var animationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        activeProgressAnimation = animationCancellation;
        Task animation = AnimateProgressAsync(
            startPercent,
            limitPercent,
            label,
            animationCancellation.Token);
        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            await animationCancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await animation.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            Interlocked.CompareExchange(
                ref activeProgressAnimation,
                null,
                animationCancellation);
        }
    }

    private async Task InitializeSpeakerSessionAsync(
        byte[] referenceWav,
        CancellationToken cancellationToken)
    {
        Guid createdSessionId = Guid.Empty;
        await RunWithAnimatedProgressAsync(
                async () =>
                {
                    createdSessionId = await worker
                        .CreateSpeakerSessionAsync(
                            referenceWav,
                            performanceProfile,
                            cancellationToken)
                        .ConfigureAwait(false);
                },
                startPercent: 15,
                limitPercent: 90,
                label: "Подготовка голосового профиля",
                cancellationToken)
            .ConfigureAwait(false);
        speakerSessionId = createdSessionId;
        pipeline = new TranslationPipeline(
            new SessionPhraseTranslationWorker(
                worker,
                createdSessionId,
                targetLanguage,
                performanceProfile),
            new ReportingPlaybackSink(
                output,
                OutputLevelChanged,
                ActivityChanged,
                ProgressChanged,
                StopProgressAnimation),
            queueCapacity: 2,
            failureObserver: failureObserver);
    }

    private async Task AnimateProgressAsync(
        int startPercent,
        int limitPercent,
        string label,
        CancellationToken cancellationToken)
    {
        ProgressChanged?.Invoke(startPercent, label);
        var startedAt = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            await Task
                .Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                .ConfigureAwait(false);
            double fraction = Math.Min(
                startedAt.Elapsed.TotalSeconds / 5.0,
                1.0);
            int percent = startPercent + (int)Math.Round(
                (limitPercent - startPercent) * fraction);
            ProgressChanged?.Invoke(percent, label);
        }
    }

    private void StopProgressAnimation()
    {
        CancellationTokenSource? animation =
            Volatile.Read(ref activeProgressAnimation);
        animation?.Cancel();
    }

    private sealed class ReportingPlaybackSink(
        IAudioPlaybackSink inner,
        Action<double>? outputLevelChanged,
        Action<string>? activityChanged,
        Action<int, string>? progressChanged,
        Action stopProgressAnimation) : IAudioPlaybackSink
    {
        public void Play(byte[] pcm)
        {
            outputLevelChanged?.Invoke(
                CalculatePcm16LevelPercent(pcm));
            activityChanged?.Invoke("Озвучиваю перевод.");
            inner.Play(pcm);
        }

        public async Task PlayAsync(
            byte[] pcm,
            CancellationToken cancellationToken)
        {
            stopProgressAnimation();
            outputLevelChanged?.Invoke(
                CalculatePcm16LevelPercent(pcm));
            progressChanged?.Invoke(94, "Озвучивание перевода");
            activityChanged?.Invoke("Озвучиваю перевод.");
            try
            {
                await inner
                    .PlayAsync(pcm, cancellationToken)
                    .ConfigureAwait(false);
                progressChanged?.Invoke(100, "Перевод озвучен");
                activityChanged?.Invoke(
                    "Перевод озвучен. Слушаю следующую фразу.");
            }
            finally
            {
                outputLevelChanged?.Invoke(0);
            }
        }

        public void StopPlayback()
        {
            outputLevelChanged?.Invoke(0);
            inner.StopPlayback();
        }
    }
}
