using System.Diagnostics;
using System.IO;
using VoiceTranslator.Infrastructure.Audio.Capture;

namespace VoiceTranslator.App.Services;

public sealed class VoiceProfileRecordingSession : IAsyncDisposable
{
    public static TimeSpan DefaultMinimumDuration { get; } =
        TimeSpan.FromSeconds(3);

    public static TimeSpan DefaultMaximumDuration { get; } =
        TimeSpan.FromSeconds(15);

    private readonly IAudioCaptureSource capture;
    private readonly TimeSpan minimumDuration;
    private readonly TimeSpan maximumDuration;
    private readonly MemoryStream pcm = new();
    private readonly object sync = new();
    private readonly CancellationTokenSource timerCancellation = new();
    private Task? timerTask;
    private int started;
    private int stopped;

    public VoiceProfileRecordingSession(
        IAudioCaptureSource capture,
        TimeSpan? minimumDuration = null,
        TimeSpan? maximumDuration = null)
    {
        this.capture = capture;
        this.minimumDuration =
            minimumDuration ?? DefaultMinimumDuration;
        this.maximumDuration =
            maximumDuration ?? DefaultMaximumDuration;

        if (this.minimumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDuration));
        }

        if (this.maximumDuration <= this.minimumDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        }
    }

    public event Action<double>? InputLevelChanged;

    public event Action<int>? SecondsRemainingChanged;

    public event Action? LimitReached;

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
        capture.StartCapture();
        timerTask = TrackLimitAsync(timerCancellation.Token);
    }

    public Task<byte[]> StopAsync(CancellationToken cancellationToken = default) =>
        StopCoreAsync(validateMinimumDuration: true, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref stopped) == 0)
        {
            _ = await StopCoreAsync(
                    validateMinimumDuration: false,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        capture.Dispose();
        pcm.Dispose();
        timerCancellation.Dispose();
    }

    private async Task<byte[]> StopCoreAsync(
        bool validateMinimumDuration,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
        {
            throw new InvalidOperationException("Запись уже завершена.");
        }

        capture.AudioAvailable -= OnAudioAvailable;
        capture.StopCapture();
        await timerCancellation.CancelAsync().ConfigureAwait(false);
        if (timerTask is not null)
        {
            try
            {
                await timerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] capturedPcm;
        lock (sync)
        {
            capturedPcm = pcm.ToArray();
        }

        int minimumBytes = checked((int)(
            16_000 * sizeof(short) * minimumDuration.TotalSeconds));
        if (validateMinimumDuration && capturedPcm.Length < minimumBytes)
        {
            throw new InvalidDataException(
                "Запись слишком короткая. Говорите не менее трёх секунд.");
        }

        return WaveMemoryCodec.EncodeWorkerWave(capturedPcm);
    }

    private void OnAudioAvailable(
        object? sender,
        AudioAvailableEventArgs eventArgs)
    {
        byte[] normalized = Pcm16Normalizer.Normalize(
            eventArgs.Audio,
            capture.WaveFormat);
        int maximumBytes = checked((int)(
            16_000 * sizeof(short) * maximumDuration.TotalSeconds));
        lock (sync)
        {
            int remaining = maximumBytes - checked((int)pcm.Length);
            if (remaining > 0)
            {
                pcm.Write(normalized, 0, Math.Min(normalized.Length, remaining));
            }
        }

        InputLevelChanged?.Invoke(CalculatePcm16LevelPercent(normalized));
    }

    private async Task TrackLimitAsync(CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        int lastRemaining = -1;
        while (elapsed.Elapsed < maximumDuration)
        {
            int remaining = Math.Max(
                0,
                (int)Math.Ceiling(
                    (maximumDuration - elapsed.Elapsed).TotalSeconds));
            if (remaining != lastRemaining)
            {
                lastRemaining = remaining;
                SecondsRemainingChanged?.Invoke(remaining);
            }

            await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        SecondsRemainingChanged?.Invoke(0);
        LimitReached?.Invoke();
    }

    private static double CalculatePcm16LevelPercent(byte[] buffer)
    {
        if (buffer.Length < sizeof(short))
        {
            return 0;
        }

        double sumSquares = 0;
        int sampleCount = buffer.Length / sizeof(short);
        for (int index = 0; index < sampleCount; index++)
        {
            short sample = BitConverter.ToInt16(
                buffer,
                index * sizeof(short));
            double normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        double rms = Math.Sqrt(sumSquares / sampleCount);
        return Math.Clamp(rms * 200, 0, 100);
    }
}
