using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
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
    private readonly VoiceProfileStore voiceProfileStore;
    private readonly WasapiDeviceCatalog devices =
        new(new CoreAudioEndpointSource());
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private readonly SemaphoreSlim voiceProfileRecordingGate = new(1, 1);
    private WorkerProcessManager? workerManager;
    private LocalWorkerClient? workerClient;
    private Task? deviceRefreshTask;
    private DesktopTranslationSession? translationSession;
    private MMDeviceEnumerator? sessionDeviceEnumerator;
    private MMDevice? sessionCaptureDevice;
    private MMDevice? sessionOutputDevice;
    private MMDevice? sessionVirtualOutputDevice;
    private VoiceProfileRecordingSession? voiceProfileRecording;
    private MMDeviceEnumerator? voiceProfileDeviceEnumerator;
    private MMDevice? voiceProfileCaptureDevice;
    private string? voiceProfileRecordingName;
    private int voiceProfileRecordingSecondsRemaining =
        MainViewModel.VoiceProfileRecordingLimitSeconds;
    private double voiceProfileRecordingInputLevel;

    public DesktopRuntimeService(
        MainViewModel viewModel,
        VoiceProfileStore voiceProfileStore)
    {
        this.viewModel = viewModel;
        this.voiceProfileStore = voiceProfileStore;
        this.viewModel.StartRequested += OnStartRequested;
        this.viewModel.StopRequested += OnStopRequested;
        this.viewModel.RenameVoiceProfileRequested += OnRenameVoiceProfileRequested;
        this.viewModel.DeleteVoiceProfileRequested += OnDeleteVoiceProfileRequested;
        this.viewModel.StartVoiceProfileRecordingRequested +=
            OnStartVoiceProfileRecordingRequested;
        this.viewModel.StopVoiceProfileRecordingRequested +=
            OnStopVoiceProfileRecordingRequested;
    }

    public ILocalTranslationWorker? Worker => workerClient;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<VoiceProfile> profiles = await voiceProfileStore
            .LoadProfilesAsync(cancellationToken)
            .ConfigureAwait(false);
        await DispatchAsync(() => viewModel.ApplyVoiceProfiles(profiles))
            .ConfigureAwait(false);
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
                        "Обработчик Python не установлен. Сначала запустите "
                        + "worker\\bootstrap.ps1.")
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
                    $"Не удалось запустить локальный обработчик: {error.Message}")
                .ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await CancelVoiceProfileRecordingAsync().ConfigureAwait(false);
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
        }
        finally
        {
            workerClient?.Dispose();
            workerClient = null;
            WorkerProcessManager? manager = workerManager;
            workerManager = null;
            if (manager is not null)
            {
                try
                {
                    await manager
                        .StopAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                finally
                {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public async Task OnSessionFailureAsync(
        SessionFailure failure,
        CancellationToken cancellationToken)
    {
        await sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (translationSession is not null)
            {
                var coordinator = new SessionFailureCoordinator(translationSession);
                await coordinator.OnSessionFailureAsync(failure, cancellationToken).ConfigureAwait(false);

                translationSession.Failed -= OnTranslationSessionFailed;
                translationSession.InputLevelChanged -= OnInputLevelChanged;
                translationSession.OutputLevelChanged -= OnOutputLevelChanged;
                translationSession.ActivityChanged -= OnActivityChanged;
                translationSession.ProgressChanged -= OnProgressChanged;
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

        await ReportFailureAsync(
                $"{DescribeSessionFailure(failure)} Требуется перезапуск.")
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        viewModel.StartRequested -= OnStartRequested;
        viewModel.StopRequested -= OnStopRequested;
        viewModel.RenameVoiceProfileRequested -= OnRenameVoiceProfileRequested;
        viewModel.DeleteVoiceProfileRequested -= OnDeleteVoiceProfileRequested;
        viewModel.StartVoiceProfileRecordingRequested -=
            OnStartVoiceProfileRecordingRequested;
        viewModel.StopVoiceProfileRecordingRequested -=
            OnStopVoiceProfileRecordingRequested;
        devices.Dispose();
        sessionGate.Dispose();
        voiceProfileRecordingGate.Dispose();
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
        bool selectedDeviceLost =
            (viewModel.State == SessionState.Listening
                || viewModel.IsVoiceProfileRecording)
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
            if (viewModel.IsVoiceProfileRecording)
            {
                await CancelVoiceProfileRecordingAsync().ConfigureAwait(false);
                await DispatchAsync(() => viewModel.CompleteVoiceProfileRecording(
                        "Микрофон отключён. Запись профиля отменена."))
                    .ConfigureAwait(false);
                return;
            }

            await StopTranslationSessionAsync().ConfigureAwait(false);
            await ReportFailureAsync(
                    "Выбранное аудиоустройство отключено. "
                    + "Выберите доступное устройство и создайте новую сессию.")
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
        catch (Exception)
        {
            await StopTranslationSessionAsync().ConfigureAwait(false);
            await ReportFailureAsync(
                    "Не удалось запустить сессию перевода. Проверьте выбранные устройства.")
                .ConfigureAwait(false);
        }
    }

    private async void OnStopRequested(object? sender, EventArgs eventArgs)
    {
        try
        {
            await StopTranslationSessionAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            await ReportFailureAsync(
                    "Не удалось корректно остановить сессию перевода.")
                .ConfigureAwait(false);
        }
    }

    private async void OnRenameVoiceProfileRequested(
        VoiceProfile profile,
        string name)
    {
        try
        {
            VoiceProfile renamed = await voiceProfileStore
                .RenameAsync(profile.Id, name, lifetime.Token)
                .ConfigureAwait(false);
            IReadOnlyList<VoiceProfile> profiles = await voiceProfileStore
                .LoadProfilesAsync(lifetime.Token)
                .ConfigureAwait(false);
            await DispatchAsync(
                    () => viewModel.ApplyVoiceProfiles(profiles, renamed.Id))
                .ConfigureAwait(false);
        }
        catch (Exception error)
            when (!lifetime.IsCancellationRequested)
        {
            await DispatchAsync(() => viewModel.ReportActivity(
                    $"Не удалось переименовать профиль: {error.Message}"))
                .ConfigureAwait(false);
        }
    }

    private async void OnDeleteVoiceProfileRequested(VoiceProfile profile)
    {
        try
        {
            await voiceProfileStore
                .DeleteAsync(profile.Id, lifetime.Token)
                .ConfigureAwait(false);
            IReadOnlyList<VoiceProfile> profiles = await voiceProfileStore
                .LoadProfilesAsync(lifetime.Token)
                .ConfigureAwait(false);
            Guid? nextProfileId = profiles.Count > 0
                ? profiles[0].Id
                : null;
            await DispatchAsync(
                    () => viewModel.ApplyVoiceProfiles(
                        profiles,
                        nextProfileId))
                .ConfigureAwait(false);
        }
        catch (Exception error)
            when (!lifetime.IsCancellationRequested)
        {
            await DispatchAsync(() => viewModel.ReportActivity(
                    $"Не удалось удалить профиль: {error.Message}"))
                .ConfigureAwait(false);
        }
    }

    private async void OnStartVoiceProfileRecordingRequested(
        object? sender,
        EventArgs eventArgs)
    {
        try
        {
            await StartVoiceProfileRecordingAsync().ConfigureAwait(false);
        }
        catch (Exception error)
            when (!lifetime.IsCancellationRequested)
        {
            await CancelVoiceProfileRecordingAsync().ConfigureAwait(false);
            await DispatchAsync(() => viewModel.CompleteVoiceProfileRecording(
                    $"Не удалось начать запись профиля: {error.Message}"))
                .ConfigureAwait(false);
        }
    }

    private async void OnStopVoiceProfileRecordingRequested(
        object? sender,
        EventArgs eventArgs)
    {
        await CompleteVoiceProfileRecordingAsync().ConfigureAwait(false);
    }

    private async void OnVoiceProfileRecordingLimitReached()
    {
        await CompleteVoiceProfileRecordingAsync().ConfigureAwait(false);
    }

    private async Task StartVoiceProfileRecordingAsync()
    {
        await voiceProfileRecordingGate.WaitAsync(lifetime.Token)
            .ConfigureAwait(false);
        try
        {
            if (voiceProfileRecording is not null)
            {
                return;
            }

            AudioDeviceInfo microphone = viewModel.SelectedMicrophone
                ?? throw new InvalidOperationException(
                    "Микрофон не выбран.");
            voiceProfileRecordingName = viewModel.VoiceProfileName.Trim();
            ArgumentException.ThrowIfNullOrWhiteSpace(
                voiceProfileRecordingName);
            voiceProfileDeviceEnumerator = new MMDeviceEnumerator();
            voiceProfileCaptureDevice = voiceProfileDeviceEnumerator
                .GetDevice(microphone.Id);
            var capture = new WasapiMicrophoneCapture(
                voiceProfileCaptureDevice);
            var recording = new VoiceProfileRecordingSession(capture);
            recording.InputLevelChanged +=
                OnVoiceProfileRecordingInputLevelChanged;
            recording.SecondsRemainingChanged +=
                OnVoiceProfileRecordingSecondsRemainingChanged;
            recording.LimitReached += OnVoiceProfileRecordingLimitReached;
            voiceProfileRecordingSecondsRemaining =
                MainViewModel.VoiceProfileRecordingLimitSeconds;
            voiceProfileRecordingInputLevel = 0;
            voiceProfileRecording = recording;
            recording.Start();
        }
        finally
        {
            voiceProfileRecordingGate.Release();
        }
    }

    private async Task CompleteVoiceProfileRecordingAsync()
    {
        await voiceProfileRecordingGate.WaitAsync().ConfigureAwait(false);
        byte[] referenceWav = [];
        VoiceProfileRecordingSession? recording = null;
        try
        {
            recording = voiceProfileRecording;
            if (recording is null)
            {
                return;
            }

            voiceProfileRecording = null;
            referenceWav = await recording.StopAsync(lifetime.Token)
                .ConfigureAwait(false);
            string name = voiceProfileRecordingName
                ?? throw new InvalidOperationException(
                    "Имя голосового профиля не задано.");
            VoiceProfile created = await voiceProfileStore
                .CreateAsync(name, referenceWav, lifetime.Token)
                .ConfigureAwait(false);
            IReadOnlyList<VoiceProfile> profiles = await voiceProfileStore
                .LoadProfilesAsync(lifetime.Token)
                .ConfigureAwait(false);
            await DispatchAsync(() =>
                {
                    viewModel.ApplyVoiceProfiles(profiles, created.Id);
                    viewModel.CompleteVoiceProfileRecording(
                        $"Профиль «{created.Name}» записан и сохранён.");
                })
                .ConfigureAwait(false);
        }
        catch (Exception error)
            when (!lifetime.IsCancellationRequested)
        {
            await DispatchAsync(() => viewModel.CompleteVoiceProfileRecording(
                    $"Профиль не сохранён: {error.Message}"))
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(referenceWav);
            await DisposeVoiceProfileRecordingResourcesAsync(recording)
                .ConfigureAwait(false);
            voiceProfileRecordingGate.Release();
        }
    }

    private async Task CancelVoiceProfileRecordingAsync()
    {
        await voiceProfileRecordingGate.WaitAsync().ConfigureAwait(false);
        try
        {
            VoiceProfileRecordingSession? recording = voiceProfileRecording;
            voiceProfileRecording = null;
            await DisposeVoiceProfileRecordingResourcesAsync(recording)
                .ConfigureAwait(false);
        }
        finally
        {
            voiceProfileRecordingGate.Release();
        }
    }

    private async Task DisposeVoiceProfileRecordingResourcesAsync(
        VoiceProfileRecordingSession? recording)
    {
        if (recording is not null)
        {
            recording.InputLevelChanged -=
                OnVoiceProfileRecordingInputLevelChanged;
            recording.SecondsRemainingChanged -=
                OnVoiceProfileRecordingSecondsRemainingChanged;
            recording.LimitReached -= OnVoiceProfileRecordingLimitReached;
            await recording.DisposeAsync().ConfigureAwait(false);
        }

        voiceProfileCaptureDevice?.Dispose();
        voiceProfileCaptureDevice = null;
        voiceProfileDeviceEnumerator?.Dispose();
        voiceProfileDeviceEnumerator = null;
        voiceProfileRecordingName = null;
    }

    private async void OnVoiceProfileRecordingInputLevelChanged(double percent)
    {
        voiceProfileRecordingInputLevel = percent;
        await DispatchAsync(() => viewModel.ReportVoiceProfileRecordingProgress(
                voiceProfileRecordingSecondsRemaining,
                percent))
            .ConfigureAwait(false);
    }

    private async void OnVoiceProfileRecordingSecondsRemainingChanged(
        int secondsRemaining)
    {
        voiceProfileRecordingSecondsRemaining = secondsRemaining;
        await DispatchAsync(() => viewModel.ReportVoiceProfileRecordingProgress(
                secondsRemaining,
                voiceProfileRecordingInputLevel))
            .ConfigureAwait(false);
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
                    "Локальный обработчик не готов.");
            AudioDeviceInfo microphone = viewModel.SelectedMicrophone
                ?? throw new InvalidOperationException(
                    "Микрофон не выбран.");
            AudioDeviceInfo? physicalOutput = viewModel.SelectedPhysicalOutput;
            AudioDeviceInfo? virtualOutput = viewModel.SelectedVirtualOutput;
            OutputMode outputMode = viewModel.SelectedOutputMode;
            string targetLanguage =
                viewModel.SelectedTargetLanguage?.Code
                ?? throw new InvalidOperationException(
                    "Язык перевода не выбран.");

            VoiceProfile? selectedProfile = viewModel.SelectedVoiceProfile;
            if (selectedProfile is null)
            {
                throw new InvalidOperationException(
                    "Голосовой профиль не выбран.");
            }

            byte[]? referenceWav = await voiceProfileStore
                .LoadReferenceAsync(selectedProfile.Id, lifetime.Token)
                .ConfigureAwait(false);

            DesktopTranslationSession session;
            try
            {
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
                session = new DesktopTranslationSession(
                    worker,
                    capture,
                    output,
                    targetLanguage,
                    this,
                    referenceWav,
                    referenceCaptured: null,
                    performanceProfile:
                        viewModel.SelectedPerformanceProfile.Code);
                referenceWav = null;
            }
            finally
            {
                if (referenceWav is not null)
                {
                    CryptographicOperations.ZeroMemory(referenceWav);
                }
            }
            session.Failed += OnTranslationSessionFailed;
            session.InputLevelChanged += OnInputLevelChanged;
            session.OutputLevelChanged += OnOutputLevelChanged;
            session.ActivityChanged += OnActivityChanged;
            session.ProgressChanged += OnProgressChanged;
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
                translationSession.ProgressChanged -= OnProgressChanged;
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
                DescribeTranslationError(error))
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

    private async void OnProgressChanged(int percent, string label)
    {
        await DispatchAsync(
                () => viewModel.ReportTranslationProgress(percent, label))
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
                    "Физическое устройство вывода не выбрано."));
            return new WasapiPlaybackSink(sessionOutputDevice, waveFormat);
        }

        if (outputMode == OutputMode.VirtualCable)
        {
            sessionVirtualOutputDevice = deviceEnumerator.GetDevice(
                virtualOutput?.Id
                ?? throw new InvalidOperationException(
                    "Виртуальное устройство вывода не выбрано."));
            return new WasapiPlaybackSink(
                sessionVirtualOutputDevice,
                waveFormat);
        }

        sessionOutputDevice = deviceEnumerator.GetDevice(
            physicalOutput?.Id
            ?? throw new InvalidOperationException(
                "Физическое устройство вывода не выбрано."));
        sessionVirtualOutputDevice = deviceEnumerator.GetDevice(
            virtualOutput?.Id
            ?? throw new InvalidOperationException(
                "Виртуальное устройство вывода не выбрано."));
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

    private static string DescribeTranslationError(Exception error)
    {
        return error is HttpRequestException { StatusCode: not null } request
            ? $"Не удалось обработать фразу. Код HTTP: {(int)request.StatusCode.Value}."
            : "Не удалось обработать фразу. Создайте новую сессию и повторите попытку.";
    }

    private static string DescribeSessionFailure(SessionFailure failure)
    {
        return failure switch
        {
            SessionFailure.GpuMemoryExhausted =>
                "Недостаточно видеопамяти для перевода.",
            SessionFailure.WorkerExited =>
                "Локальный обработчик неожиданно завершился.",
            SessionFailure.HeartbeatTimedOut =>
                "Локальный обработчик перестал отвечать.",
            _ => "Произошёл сбой локального обработчика.",
        };
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
            "Не удалось найти установленный обработчик Python.");
    }
}
