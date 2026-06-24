using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.App.ViewModels;
using VoiceTranslator.Domain.Audio;
using VoiceTranslator.Domain.Sessions;
using VoiceTranslator.Infrastructure.Audio.Capture;
using VoiceTranslator.Infrastructure.Audio.Devices;
using VoiceTranslator.Infrastructure.Audio.Playback;
using VoiceTranslator.Infrastructure.Audio.Routing;
using VoiceTranslator.Infrastructure.LocalWorker;

namespace VoiceTranslator.App.Services;

public sealed class DesktopRuntimeService :
    IHostedService,
    ISessionFailureObserver,
    IAsyncDisposable
{
    private readonly MainViewModel viewModel;
    private readonly WasapiDeviceCatalog devices =
        new(new CoreAudioEndpointSource());
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private WorkerProcessManager? workerManager;
    private LocalWorkerClient? workerClient;
    private Task? deviceRefreshTask;
    private DesktopTranslationSession? translationSession;
    private MMDeviceEnumerator? sessionDeviceEnumerator;
    private MMDevice? sessionCaptureDevice;
    private MMDevice? sessionOutputDevice;
    private MMDevice? sessionVirtualOutputDevice;

    public DesktopRuntimeService(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        this.viewModel.StartRequested += OnStartRequested;
        this.viewModel.StopRequested += OnStopRequested;
    }

    public ILocalTranslationWorker? Worker => workerClient;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshDevicesAsync().ConfigureAwait(false);
        deviceRefreshTask = RefreshDevicesPeriodicallyAsync(lifetime.Token);

        try
        {
            string workspaceRoot = FindWorkspaceRoot(AppContext.BaseDirectory);
            string workerDirectory = Path.Combine(workspaceRoot, "worker");
            string pythonExecutable = Path.Combine(
                workerDirectory,
                ".venv",
                "Scripts",
                "python.exe");
            if (!File.Exists(pythonExecutable))
            {
                await ReportFailureAsync(
                        "Python worker is not installed. Run "
                        + "worker\\bootstrap.ps1 first.")
                    .ConfigureAwait(false);
                return;
            }

            var endpoint = ReserveLoopbackEndpoint();
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
            workerManager = new WorkerProcessManager(
                launcher,
                healthProbe,
                endpoint,
                failureObserver: this,
                startMonitoringOnStart: false);
            WorkerHandle handle = await workerManager
                .StartAsync(cancellationToken)
                .ConfigureAwait(false);
            workerClient = new LocalWorkerClient(
                new HttpClient
                {
                    Timeout = TimeSpan.FromMinutes(10),
                },
                handle.Endpoint,
                handle.Token);
            WorkerPreflightReport report = await workerClient
                .PreflightAsync(cancellationToken)
                .ConfigureAwait(false);
            await DispatchAsync(() => viewModel.ApplyPreflight(report))
                .ConfigureAwait(false);
            workerManager.StartMonitoring(handle);
        }
        catch (Exception error)
            when (!cancellationToken.IsCancellationRequested)
        {
            if (workerManager is not null)
            {
                await workerManager
                    .StopAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await ReportFailureAsync(
                    $"Local worker could not start: {error.Message}")
                .ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifetime.CancelAsync().ConfigureAwait(false);
        await StopTranslationSessionAsync().ConfigureAwait(false);
        if (deviceRefreshTask is not null)
        {
            try
            {
                await deviceRefreshTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        workerClient?.Dispose();
        workerClient = null;
        if (workerManager is not null)
        {
            await workerManager
                .StopAsync(cancellationToken)
                .ConfigureAwait(false);
            await workerManager.DisposeAsync().ConfigureAwait(false);
            workerManager = null;
        }
    }

    public async Task OnSessionFailureAsync(
        SessionFailure failure,
        CancellationToken cancellationToken)
    {
        await ReportFailureAsync(
                $"Local worker failure: {failure}. Restart is required.")
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        viewModel.StartRequested -= OnStartRequested;
        viewModel.StopRequested -= OnStopRequested;
        devices.Dispose();
        sessionGate.Dispose();
        lifetime.Dispose();
    }

    private async Task RefreshDevicesPeriodicallyAsync(
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer
            .WaitForNextTickAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            await RefreshDevicesAsync().ConfigureAwait(false);
        }
    }

    private async Task RefreshDevicesAsync()
    {
        devices.Refresh();
        AudioDeviceInfo[] captures = devices.CaptureDevices.ToArray();
        AudioDeviceInfo[] renders = devices.RenderDevices.ToArray();
        string? selectedCaptureId = viewModel.SelectedMicrophone?.Id;
        string? selectedOutputId = viewModel.SelectedPhysicalOutput?.Id;
        string? selectedVirtualOutputId = viewModel.SelectedVirtualOutput?.Id;
        OutputMode outputMode = viewModel.SelectedOutputMode;
        bool selectedDeviceLost = viewModel.State == SessionState.Listening
            && (
                selectedCaptureId is not null
                && !captures.Any(device => device.Id == selectedCaptureId)
                || outputMode is OutputMode.Physical or OutputMode.Both
                && selectedOutputId is not null
                && !renders.Any(device => device.Id == selectedOutputId)
                || outputMode is OutputMode.VirtualCable or OutputMode.Both
                && selectedVirtualOutputId is not null
                && !renders.Any(device => device.Id == selectedVirtualOutputId)
            );
        await DispatchAsync(
                () => viewModel.UpdateDevices(captures, renders))
            .ConfigureAwait(false);
        if (selectedDeviceLost)
        {
            await StopTranslationSessionAsync().ConfigureAwait(false);
            await ReportFailureAsync(
                    "The selected audio device was disconnected. "
                    + "Select an available device and start a new session.")
                .ConfigureAwait(false);
        }
    }

    private Task ReportFailureAsync(string message)
    {
        return DispatchAsync(() => viewModel.ReportWorkerFailure(message));
    }

    private async void OnStartRequested(object? sender, EventArgs eventArgs)
    {
        try
        {
            await StartTranslationSessionAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await StopTranslationSessionAsync().ConfigureAwait(false);
            await ReportFailureAsync(
                    $"Translation session could not start: {error.Message}")
                .ConfigureAwait(false);
        }
    }

    private async void OnStopRequested(object? sender, EventArgs eventArgs)
    {
        try
        {
            await StopTranslationSessionAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await ReportFailureAsync(
                    $"Translation session could not stop cleanly: "
                    + error.Message)
                .ConfigureAwait(false);
        }
    }

    private async Task StartTranslationSessionAsync()
    {
        await sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (translationSession is not null)
            {
                return;
            }

            LocalWorkerClient worker = workerClient
                ?? throw new InvalidOperationException(
                    "The local worker is not ready.");
            AudioDeviceInfo microphone = viewModel.SelectedMicrophone
                ?? throw new InvalidOperationException(
                    "No microphone is selected.");
            AudioDeviceInfo? physicalOutput = viewModel.SelectedPhysicalOutput;
            AudioDeviceInfo? virtualOutput = viewModel.SelectedVirtualOutput;
            OutputMode outputMode = viewModel.SelectedOutputMode;
            string targetLanguage =
                viewModel.SelectedTargetLanguage?.Code
                ?? throw new InvalidOperationException(
                    "No target language is selected.");

            sessionDeviceEnumerator = new MMDeviceEnumerator();
            sessionCaptureDevice =
                sessionDeviceEnumerator.GetDevice(microphone.Id);
            var capture = new WasapiMicrophoneCapture(
                sessionCaptureDevice);
            IAudioPlaybackSink output = CreatePlaybackSink(
                sessionDeviceEnumerator,
                physicalOutput,
                virtualOutput,
                outputMode);
            var session = new DesktopTranslationSession(
                worker,
                capture,
                output,
                targetLanguage);
            session.Failed += OnTranslationSessionFailed;
            session.InputLevelChanged += OnInputLevelChanged;
            session.OutputLevelChanged += OnOutputLevelChanged;
            session.ActivityChanged += OnActivityChanged;
            translationSession = session;
            session.Start();
        }
        finally
        {
            sessionGate.Release();
        }
    }

    private async Task StopTranslationSessionAsync()
    {
        await sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (translationSession is not null)
            {
                translationSession.Failed -= OnTranslationSessionFailed;
                translationSession.InputLevelChanged -= OnInputLevelChanged;
                translationSession.OutputLevelChanged -= OnOutputLevelChanged;
                translationSession.ActivityChanged -= OnActivityChanged;
                await translationSession.DisposeAsync().ConfigureAwait(false);
                translationSession = null;
            }

            sessionCaptureDevice?.Dispose();
            sessionCaptureDevice = null;
            sessionOutputDevice?.Dispose();
            sessionOutputDevice = null;
            sessionVirtualOutputDevice?.Dispose();
            sessionVirtualOutputDevice = null;
            sessionDeviceEnumerator?.Dispose();
            sessionDeviceEnumerator = null;
        }
        finally
        {
            sessionGate.Release();
        }
    }

    private async void OnTranslationSessionFailed(Exception error)
    {
        await StopTranslationSessionAsync().ConfigureAwait(false);
        await ReportFailureAsync(
                $"Audio translation failed: {error.Message}")
            .ConfigureAwait(false);
    }

    private async void OnInputLevelChanged(double percent)
    {
        await DispatchAsync(() => viewModel.ReportInputLevel(percent))
            .ConfigureAwait(false);
    }

    private async void OnOutputLevelChanged(double percent)
    {
        await DispatchAsync(() => viewModel.ReportOutputLevel(percent))
            .ConfigureAwait(false);
    }

    private async void OnActivityChanged(string message)
    {
        await DispatchAsync(() => viewModel.ReportActivity(message))
            .ConfigureAwait(false);
    }

    private IAudioPlaybackSink CreatePlaybackSink(
        MMDeviceEnumerator deviceEnumerator,
        AudioDeviceInfo? physicalOutput,
        AudioDeviceInfo? virtualOutput,
        OutputMode outputMode)
    {
        var waveFormat = new WaveFormat(24_000, 16, 1);
        if (outputMode == OutputMode.Physical)
        {
            sessionOutputDevice = deviceEnumerator.GetDevice(
                physicalOutput?.Id
                ?? throw new InvalidOperationException(
                    "No physical output is selected."));
            return new WasapiPlaybackSink(sessionOutputDevice, waveFormat);
        }

        if (outputMode == OutputMode.VirtualCable)
        {
            sessionVirtualOutputDevice = deviceEnumerator.GetDevice(
                virtualOutput?.Id
                ?? throw new InvalidOperationException(
                    "No virtual output is selected."));
            return new WasapiPlaybackSink(
                sessionVirtualOutputDevice,
                waveFormat);
        }

        sessionOutputDevice = deviceEnumerator.GetDevice(
            physicalOutput?.Id
            ?? throw new InvalidOperationException(
                "No physical output is selected."));
        sessionVirtualOutputDevice = deviceEnumerator.GetDevice(
            virtualOutput?.Id
            ?? throw new InvalidOperationException(
                "No virtual output is selected."));
        return new AudioOutputRoutingSink(
            new WasapiPlaybackSink(sessionOutputDevice, waveFormat),
            new WasapiPlaybackSink(sessionVirtualOutputDevice, waveFormat),
            OutputMode.Both);
    }

    private static Task DispatchAsync(Action action)
    {
        Dispatcher dispatcher = System.Windows.Application.Current.Dispatcher;
        return dispatcher.CheckAccess()
            ? RunInline(action)
            : dispatcher.InvokeAsync(action).Task;
    }

    private static Task RunInline(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static Uri ReserveLoopbackEndpoint()
    {
        var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return new Uri(
                $"http://127.0.0.1:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string FindWorkspaceRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "worker",
                "pyproject.toml")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the packaged Python worker.");
    }
}
