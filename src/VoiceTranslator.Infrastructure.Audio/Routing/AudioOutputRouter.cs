using System.Collections.Concurrent;
using System.Threading.Channels;

namespace VoiceTranslator.Infrastructure.Audio.Routing;

public sealed class SynthesizedPcmPayload
{
    private readonly byte[] pcm;

    private SynthesizedPcmPayload(byte[] pcm)
    {
        this.pcm = pcm;
    }

    public ReadOnlyMemory<byte> Pcm => pcm;

    public static SynthesizedPcmPayload Create(ReadOnlySpan<byte> pcm)
    {
        return new SynthesizedPcmPayload(pcm.ToArray());
    }
}

public interface ISynthesizedAudioSink
{
    ValueTask PlayAsync(
        SynthesizedPcmPayload payload,
        CancellationToken cancellationToken);

    void StopPlayback();
}

public sealed record OutputSinkFailure(
    string OutputName,
    Exception Error);

public sealed class AudioOutputRouter : IAsyncDisposable
{
    private readonly ConcurrentQueue<OutputSinkFailure> failures = [];
    private readonly OutputLane physical;
    private readonly OutputLane virtualOutput;
    private int stopped;

    public AudioOutputRouter(
        ISynthesizedAudioSink physicalSink,
        ISynthesizedAudioSink virtualSink)
    {
        ArgumentNullException.ThrowIfNull(physicalSink);
        ArgumentNullException.ThrowIfNull(virtualSink);

        physical = new OutputLane(
            "physical",
            physicalSink,
            failures);
        virtualOutput = new OutputLane(
            "virtual",
            virtualSink,
            failures);
    }

    public IReadOnlyCollection<OutputSinkFailure> Failures =>
        failures.ToArray();

    public void RouteSynthesized(SynthesizedPcmPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref stopped) != 0,
            this);

        physical.Enqueue(payload);
        virtualOutput.Enqueue(payload);
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
        {
            return;
        }

        await Task.WhenAll(
                physical.StopAsync(),
                virtualOutput.StopAsync())
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private sealed class OutputLane : IAsyncDisposable
    {
        private readonly string name;
        private readonly ISynthesizedAudioSink sink;
        private readonly ConcurrentQueue<OutputSinkFailure> failures;
        private readonly Channel<SynthesizedPcmPayload> channel =
            Channel.CreateUnbounded<SynthesizedPcmPayload>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                });
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task consumer;
        private int stopped;

        public OutputLane(
            string name,
            ISynthesizedAudioSink sink,
            ConcurrentQueue<OutputSinkFailure> failures)
        {
            this.name = name;
            this.sink = sink;
            this.failures = failures;
            consumer = ConsumeAsync();
        }

        public void Enqueue(SynthesizedPcmPayload payload)
        {
            _ = channel.Writer.TryWrite(payload);
        }

        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0)
            {
                return;
            }

            channel.Writer.TryComplete();
            await cancellation.CancelAsync().ConfigureAwait(false);
            DrainQueuedPayloads();
            sink.StopPlayback();

            try
            {
                await consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        private async Task ConsumeAsync()
        {
            try
            {
                await foreach (SynthesizedPcmPayload payload in
                    channel.Reader.ReadAllAsync(cancellation.Token)
                        .ConfigureAwait(false))
                {
                    await sink
                        .PlayAsync(payload, cancellation.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                channel.Writer.TryComplete(error);
                failures.Enqueue(new OutputSinkFailure(name, error));
            }
            finally
            {
                DrainQueuedPayloads();
            }
        }

        private void DrainQueuedPayloads()
        {
            while (channel.Reader.TryRead(out _))
            {
            }
        }
    }
}
