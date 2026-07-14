using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VoiceTranslator.Domain.Audio;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Sessions;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.App.Services;
using VoiceTranslator.Infrastructure.Audio.Devices;

namespace VoiceTranslator.App.ViewModels;

public sealed record PerformanceProfileOption(
    string Code,
    string DisplayName);

public sealed class MainViewModel : INotifyPropertyChanged
{
    public const int VoiceProfileRecordingLimitSeconds = 15;

    private static readonly PerformanceProfileOption LowMemoryProfile = new(
        "low-memory",
        "Экономный");
    private static readonly PerformanceProfileOption BalancedProfile = new(
        "balanced",
        "Баланс");
    private static readonly PerformanceProfileOption MaximumPerformanceProfile = new(
        "performance",
        "Производительность");
    private readonly RelayCommand startCommand;
    private readonly RelayCommand stopCommand;
    private readonly RelayCommand newSessionCommand;
    private readonly RelayCommand newVoiceProfileCommand;
    private readonly RelayCommand renameVoiceProfileCommand;
    private readonly RelayCommand deleteVoiceProfileCommand;
    private readonly RelayCommand startVoiceProfileRecordingCommand;
    private readonly RelayCommand stopVoiceProfileRecordingCommand;
    private TargetLanguage? selectedTargetLanguage;
    private AudioDeviceInfo? selectedMicrophone;
    private AudioDeviceInfo? selectedPhysicalOutput;
    private AudioDeviceInfo? selectedVirtualOutput;
    private OutputMode selectedOutputMode = OutputMode.Physical;
    private IReadOnlyList<TargetLanguage> targetLanguages =
        TargetLanguage.All;
    private IReadOnlyList<AudioDeviceInfo> microphones = [];
    private IReadOnlyList<AudioDeviceInfo> physicalOutputs = [];
    private IReadOnlyList<AudioDeviceInfo> virtualOutputs = [];
    private VoiceProfile[] voiceProfiles = [];
    private VoiceProfile? selectedVoiceProfile;
    private string voiceProfileName = string.Empty;
    private PerformanceProfileOption selectedPerformanceProfile =
        BalancedProfile;
    private bool isVoiceProfileRecording;
    private int voiceProfileRecordingSecondsRemaining =
        VoiceProfileRecordingLimitSeconds;
    private bool outputChannelTestPassed;
    private bool isModelPreflightPassed;
    private bool isWorkerReady;
    private SessionState state = SessionState.Draft;
    private string performanceProfile = "unavailable";
    private string modelInventorySummary = "Сведения о моделях недоступны.";
    private string activityMessage = "Ожидание настройки.";
    private int translationProgressPercent;
    private string translationProgressLabel = "Ожидание";
    private double inputLevelPercent;
    private double outputLevelPercent;
    private string? outputWarning;
    private string? failureMessage;

    public MainViewModel()
    {
        startCommand = new RelayCommand(Start, CanStart);
        stopCommand = new RelayCommand(Stop, () => State == SessionState.Listening);
        newSessionCommand = new RelayCommand(
            BeginNewSession,
            () => State == SessionState.Stopped);
        newVoiceProfileCommand = new RelayCommand(
            BeginNewVoiceProfile,
            () => State != SessionState.Listening
                && !IsVoiceProfileRecording);
        renameVoiceProfileCommand = new RelayCommand(
            RenameVoiceProfile,
            CanRenameVoiceProfile);
        deleteVoiceProfileCommand = new RelayCommand(
            DeleteVoiceProfile,
            () => State != SessionState.Listening
                && !IsVoiceProfileRecording
                && SelectedVoiceProfile is not null);
        startVoiceProfileRecordingCommand = new RelayCommand(
            StartVoiceProfileRecording,
            CanStartVoiceProfileRecording);
        stopVoiceProfileRecordingCommand = new RelayCommand(
            StopVoiceProfileRecording,
            () => IsVoiceProfileRecording);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? StartRequested;

    public event EventHandler? StopRequested;

    public event EventHandler? StartVoiceProfileRecordingRequested;

    public event EventHandler? StopVoiceProfileRecordingRequested;

    public event Action<VoiceProfile, string>? RenameVoiceProfileRequested;

    public event Action<VoiceProfile>? DeleteVoiceProfileRequested;

    public IReadOnlyList<TargetLanguage> TargetLanguages => targetLanguages;

    public IReadOnlyList<AudioDeviceInfo> Microphones => microphones;

    public IReadOnlyList<AudioDeviceInfo> PhysicalOutputs => physicalOutputs;

    public IReadOnlyList<AudioDeviceInfo> VirtualOutputs => virtualOutputs;

    public IReadOnlyList<VoiceProfile> VoiceProfiles => voiceProfiles;

    public IReadOnlyList<PerformanceProfileOption> PerformanceProfiles { get; } =
    [
        LowMemoryProfile,
        BalancedProfile,
        MaximumPerformanceProfile,
    ];

    public IReadOnlyList<OutputMode> OutputModes { get; } =
    [
        OutputMode.Physical,
        OutputMode.VirtualCable,
        OutputMode.Both,
    ];

    public TargetLanguage? SelectedTargetLanguage
    {
        get => selectedTargetLanguage;
        set => SetPrerequisite(ref selectedTargetLanguage, value);
    }

    public AudioDeviceInfo? SelectedMicrophone
    {
        get => selectedMicrophone;
        set => SetPrerequisite(ref selectedMicrophone, value);
    }

    public AudioDeviceInfo? SelectedPhysicalOutput
    {
        get => selectedPhysicalOutput;
        set => SetPrerequisite(ref selectedPhysicalOutput, value);
    }

    public AudioDeviceInfo? SelectedVirtualOutput
    {
        get => selectedVirtualOutput;
        set => SetPrerequisite(ref selectedVirtualOutput, value);
    }

    public OutputMode SelectedOutputMode
    {
        get => selectedOutputMode;
        set => SetPrerequisite(ref selectedOutputMode, value);
    }

    public VoiceProfile? SelectedVoiceProfile
    {
        get => selectedVoiceProfile;
        set
        {
            if (selectedVoiceProfile == value)
            {
                return;
            }

            selectedVoiceProfile = value;
            voiceProfileName = value?.Name ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VoiceProfileName));
            OnPropertyChanged(nameof(StatusMessage));
            if (value is not null && State != SessionState.Listening)
            {
                ActivityMessage =
                    $"Профиль «{value.Name}» выбран и будет применён при запуске.";
            }
            UpdateReadinessAndCommands();
        }
    }

    public string VoiceProfileName
    {
        get => voiceProfileName;
        set
        {
            value ??= string.Empty;
            if (voiceProfileName == value)
            {
                return;
            }

            voiceProfileName = value;
            OnPropertyChanged();
            UpdateReadinessAndCommands();
        }
    }

    public PerformanceProfileOption SelectedPerformanceProfile
    {
        get => selectedPerformanceProfile;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (selectedPerformanceProfile == value)
            {
                return;
            }

            selectedPerformanceProfile = value;
            OnPropertyChanged();
            if (State != SessionState.Listening)
            {
                ActivityMessage =
                    $"Режим «{value.DisplayName}» выбран и будет применён при запуске.";
            }
        }
    }

    public bool IsVoiceProfileRecording
    {
        get => isVoiceProfileRecording;
        private set
        {
            if (isVoiceProfileRecording == value)
            {
                return;
            }

            isVoiceProfileRecording = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanChangeConfiguration));
            OnPropertyChanged(nameof(VoiceProfileRecordingStatus));
            OnPropertyChanged(nameof(StatusMessage));
            UpdateReadinessAndCommands();
        }
    }

    public int VoiceProfileRecordingSecondsRemaining
    {
        get => voiceProfileRecordingSecondsRemaining;
        private set
        {
            int normalized = Math.Clamp(
                value,
                0,
                VoiceProfileRecordingLimitSeconds);
            if (voiceProfileRecordingSecondsRemaining == normalized)
            {
                return;
            }

            voiceProfileRecordingSecondsRemaining = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VoiceProfileRecordingStatus));
        }
    }

    public bool CanChangeConfiguration =>
        State != SessionState.Listening && !IsVoiceProfileRecording;

    public string VoiceProfileRecordingStatus => IsVoiceProfileRecording
        ? $"Идёт запись. Осталось не более {VoiceProfileRecordingSecondsRemaining} с."
        : "Запишите от 3 до 15 секунд обычной речи.";

    public bool OutputChannelTestPassed
    {
        get => outputChannelTestPassed;
        set => SetPrerequisite(ref outputChannelTestPassed, value);
    }

    public bool IsModelPreflightPassed
    {
        get => isModelPreflightPassed;
        set => SetPrerequisite(ref isModelPreflightPassed, value);
    }

    public bool IsWorkerReady
    {
        get => isWorkerReady;
        set => SetPrerequisite(ref isWorkerReady, value);
    }

    public SessionState State
    {
        get => state;
        private set
        {
            if (state == value)
            {
                return;
            }

            state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(CanChangeConfiguration));
            RaiseCommandStates();
        }
    }

    public string PerformanceProfile
    {
        get => performanceProfile;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            if (performanceProfile == value)
            {
                return;
            }

            performanceProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PerformanceProfileDisplay));
        }
    }

    public string ModelInventorySummary
    {
        get => modelInventorySummary;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (modelInventorySummary == value)
            {
                return;
            }

            modelInventorySummary = value;
            OnPropertyChanged();
        }
    }

    public string? OutputWarning
    {
        get => outputWarning;
        private set
        {
            if (outputWarning == value)
            {
                return;
            }

            outputWarning = value;
            OnPropertyChanged();
        }
    }

    public string ActivityMessage
    {
        get => activityMessage;
        private set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (activityMessage == value)
            {
                return;
            }

            activityMessage = value;
            OnPropertyChanged();
        }
    }

    public int TranslationProgressPercent
    {
        get => translationProgressPercent;
        private set
        {
            int normalized = Math.Clamp(value, 0, 100);
            if (translationProgressPercent == normalized)
            {
                return;
            }

            translationProgressPercent = normalized;
            OnPropertyChanged();
        }
    }

    public string TranslationProgressLabel
    {
        get => translationProgressLabel;
        private set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (translationProgressLabel == value)
            {
                return;
            }

            translationProgressLabel = value;
            OnPropertyChanged();
        }
    }

    public double InputLevelPercent
    {
        get => inputLevelPercent;
        private set
        {
            double normalized = ClampPercent(value);
            if (Math.Abs(inputLevelPercent - normalized) < 0.1)
            {
                return;
            }

            inputLevelPercent = normalized;
            OnPropertyChanged();
        }
    }

    public double OutputLevelPercent
    {
        get => outputLevelPercent;
        private set
        {
            double normalized = ClampPercent(value);
            if (Math.Abs(outputLevelPercent - normalized) < 0.1)
            {
                return;
            }

            outputLevelPercent = normalized;
            OnPropertyChanged();
        }
    }

    public string ModelPreflightState => IsModelPreflightPassed
        ? "Модели проверены"
        : "Требуется проверка моделей";

    public string PerformanceProfileDisplay => PerformanceProfile switch
    {
        "balanced" => "Баланс",
        "low-memory" => "Экономный",
        "performance" => "Производительность",
        _ => "Недоступен",
    };

    public string StatusMessage
    {
        get
        {
            if (IsVoiceProfileRecording)
            {
                return VoiceProfileRecordingStatus;
            }

            if (State == SessionState.Faulted)
            {
                return failureMessage ?? "Локальный обработчик остановлен.";
            }

            if (State == SessionState.Listening)
            {
                return "Слушаю русскую речь.";
            }

            if (State == SessionState.Stopped)
            {
                return "Перевод остановлен. Создайте новую сессию, чтобы продолжить.";
            }

            if (!IsWorkerReady && !IsModelPreflightPassed)
            {
                return "Локальный обработчик и необходимые модели недоступны.";
            }

            if (!IsWorkerReady)
            {
                return "Локальный обработчик недоступен.";
            }

            if (!IsModelPreflightPassed)
            {
                return "Необходимые модели не прошли проверку.";
            }

            if (SelectedMicrophone is null)
            {
                return "Выберите микрофон.";
            }

            if (
                SelectedOutputMode is OutputMode.Physical or OutputMode.Both
                && SelectedPhysicalOutput is null
            )
            {
                return "Выберите устройство физического вывода.";
            }

            if (
                SelectedOutputMode is OutputMode.VirtualCable or OutputMode.Both
                && SelectedVirtualOutput is null
            )
            {
                return VirtualOutputs.Count == 0
                    ? "Виртуальный аудиокабель не найден. Установите VB-CABLE, VoiceMeeter или аналог."
                    : "Выберите виртуальный выход для приложений.";
            }

            if (
                SelectedOutputMode is OutputMode.VirtualCable or OutputMode.Both
                && !OutputChannelTestPassed
            )
            {
                return "Проверьте канал вывода перед использованием виртуального кабеля.";
            }

            if (SelectedTargetLanguage is null)
            {
                return "Выберите язык перевода.";
            }

            if (SelectedVoiceProfile is null)
            {
                return string.IsNullOrWhiteSpace(VoiceProfileName)
                    ? "Выберите голосовой профиль или создайте новый."
                    : "Запишите образец нового голосового профиля.";
            }

            return $"Готово к запуску. Профиль «{SelectedVoiceProfile.Name}» выбран.";
        }
    }

    public ICommand StartCommand => startCommand;

    public ICommand StopCommand => stopCommand;

    public ICommand NewSessionCommand => newSessionCommand;

    public ICommand NewVoiceProfileCommand => newVoiceProfileCommand;

    public ICommand RenameVoiceProfileCommand => renameVoiceProfileCommand;

    public ICommand DeleteVoiceProfileCommand => deleteVoiceProfileCommand;

    public ICommand StartVoiceProfileRecordingCommand =>
        startVoiceProfileRecordingCommand;

    public ICommand StopVoiceProfileRecordingCommand =>
        stopVoiceProfileRecordingCommand;

    public void ApplyVoiceProfiles(
        IReadOnlyList<VoiceProfile> profiles,
        Guid? selectedProfileId = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        Guid? currentId = selectedProfileId ?? SelectedVoiceProfile?.Id;
        voiceProfiles = profiles.ToArray();
        selectedVoiceProfile = currentId is Guid id
            ? voiceProfiles.FirstOrDefault(profile => profile.Id == id)
            : voiceProfiles.Length > 0 ? voiceProfiles[0] : null;
        voiceProfileName = selectedVoiceProfile?.Name ?? string.Empty;
        OnPropertyChanged(nameof(VoiceProfiles));
        OnPropertyChanged(nameof(SelectedVoiceProfile));
        OnPropertyChanged(nameof(VoiceProfileName));
        UpdateReadinessAndCommands();
    }

    public void ReportVoiceProfileRecordingProgress(
        int secondsRemaining,
        double inputLevelPercent)
    {
        VoiceProfileRecordingSecondsRemaining = secondsRemaining;
        InputLevelPercent = inputLevelPercent;
        int elapsed = VoiceProfileRecordingLimitSeconds - secondsRemaining;
        ReportTranslationProgress(
            elapsed * 100 / VoiceProfileRecordingLimitSeconds,
            "Запись голосового профиля");
    }

    public void CompleteVoiceProfileRecording(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        IsVoiceProfileRecording = false;
        VoiceProfileRecordingSecondsRemaining =
            VoiceProfileRecordingLimitSeconds;
        InputLevelPercent = 0;
        ActivityMessage = message;
        ReportTranslationProgress(0, "Ожидание");
    }

    public void UpdateDevices(
        IReadOnlyList<AudioDeviceInfo> captureDevices,
        IReadOnlyList<AudioDeviceInfo> renderDevices)
    {
        ArgumentNullException.ThrowIfNull(captureDevices);
        ArgumentNullException.ThrowIfNull(renderDevices);

        microphones = captureDevices;
        physicalOutputs = renderDevices.ToArray();
        AudioDeviceInfo[] detectedVirtualOutputs = renderDevices
            .Where(device => device.IsVirtual)
            .ToArray();
        virtualOutputs = detectedVirtualOutputs;
        OnPropertyChanged(nameof(Microphones));
        OnPropertyChanged(nameof(PhysicalOutputs));
        OnPropertyChanged(nameof(VirtualOutputs));

        if (
            SelectedMicrophone is not null
            && !microphones.Contains(SelectedMicrophone)
        )
        {
            SelectedMicrophone = null;
        }
        if (
            SelectedPhysicalOutput is not null
            && !physicalOutputs.Contains(SelectedPhysicalOutput)
        )
        {
            SelectedPhysicalOutput = null;
        }
        if (
            SelectedVirtualOutput is not null
            && !virtualOutputs.Contains(SelectedVirtualOutput)
        )
        {
            SelectedVirtualOutput = null;
        }

        SelectedMicrophone ??= SelectDefaultDevice(microphones);
        SelectedPhysicalOutput ??= SelectDefaultDevice(physicalOutputs);
        SelectedVirtualOutput ??= SelectDefaultDevice(virtualOutputs);
    }

    public void ApplyPreflight(WorkerPreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        failureMessage = null;
        ActivityMessage = "Обработчик готов. Выберите устройства и нажмите «Запустить».";
        var availableCodes = report.AvailableLanguages.ToHashSet(
            StringComparer.Ordinal);
        targetLanguages = TargetLanguage.All
            .Where(language => availableCodes.Contains(language.Code))
            .ToArray();
        OnPropertyChanged(nameof(TargetLanguages));
        if (
            SelectedTargetLanguage is not null
            && !targetLanguages.Contains(SelectedTargetLanguage)
        )
        {
            SelectedTargetLanguage = null;
        }
        IsWorkerReady = true;
        PerformanceProfile = report.PerformanceProfile;
        SelectedPerformanceProfile = PerformanceProfiles.FirstOrDefault(
            option => string.Equals(
                option.Code,
                report.PerformanceProfile,
                StringComparison.Ordinal)) ?? BalancedProfile;
        ModelInventorySummary = report.MissingModels.Count == 0
            ? $"Все модели проверены. Доступно языков: {targetLanguages.Count}."
            : "Отсутствуют модели: " + string.Join(", ", report.MissingModels);
        IsModelPreflightPassed = report.Ready;
    }

    public void ApplyOutputChannelTest(OutputChannelTestResult result)
    {
        OutputChannelTestPassed = result.Passed;
        OutputWarning = result.Warning;
    }

    public void ReportWorkerFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        failureMessage = message;
        IsWorkerReady = false;
        State = SessionState.Faulted;
        ActivityMessage = "Обработчик остановлен.";
        ReportTranslationProgress(0, "Ошибка");
    }

    public void ReportInputLevel(double percent) =>
        InputLevelPercent = percent;

    public void ReportOutputLevel(double percent) =>
        OutputLevelPercent = percent;

    public void ReportActivity(string message) =>
        ActivityMessage = message;

    public void ReportTranslationProgress(int percent, string label)
    {
        TranslationProgressPercent = percent;
        TranslationProgressLabel = label;
    }

    private bool CanStart()
    {
        return State == SessionState.Ready
            && !IsVoiceProfileRecording
            && IsModelPreflightPassed
            && SelectedMicrophone is not null
            && (
                SelectedOutputMode == OutputMode.VirtualCable
                || SelectedPhysicalOutput is not null
            )
            && (
                SelectedOutputMode == OutputMode.Physical
                || SelectedVirtualOutput is not null
            )
            && (
                SelectedOutputMode == OutputMode.Physical
                || OutputChannelTestPassed
            )
            && SelectedTargetLanguage is not null
            && SelectedVoiceProfile is not null
            && IsWorkerReady;
    }

    private void Start()
    {
        if (!CanStart())
        {
            return;
        }

        State = SessionState.Listening;
        ActivityMessage =
            $"Профиль «{SelectedVoiceProfile!.Name}» готов. Слушаю речь для перевода.";
        ReportTranslationProgress(0, "Ожидание речи");
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Stop()
    {
        if (State != SessionState.Listening)
        {
            return;
        }

        State = SessionState.Stopped;
        InputLevelPercent = 0;
        OutputLevelPercent = 0;
        ActivityMessage = "Остановлено.";
        ReportTranslationProgress(0, "Остановлено");
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BeginNewSession()
    {
        if (State != SessionState.Stopped)
        {
            return;
        }

        State = SessionState.Draft;
        InputLevelPercent = 0;
        OutputLevelPercent = 0;
        ActivityMessage = SelectedVoiceProfile is null
            ? "Выберите или создайте голосовой профиль."
            : $"Профиль «{SelectedVoiceProfile.Name}» сохранён для новой сессии.";
        ReportTranslationProgress(0, "Ожидание");
        UpdateReadinessAndCommands();
    }

    private void BeginNewVoiceProfile()
    {
        if (State == SessionState.Listening || IsVoiceProfileRecording)
        {
            return;
        }

        SelectedVoiceProfile = null;
        VoiceProfileName = string.Empty;
        ActivityMessage = "Введите имя нового голосового профиля.";
    }

    private bool CanRenameVoiceProfile()
    {
        return State != SessionState.Listening
            && !IsVoiceProfileRecording
            && SelectedVoiceProfile is not null
            && !string.IsNullOrWhiteSpace(VoiceProfileName)
            && !string.Equals(
                SelectedVoiceProfile.Name,
                VoiceProfileName.Trim(),
                StringComparison.Ordinal);
    }

    private void RenameVoiceProfile()
    {
        if (!CanRenameVoiceProfile())
        {
            return;
        }

        RenameVoiceProfileRequested?.Invoke(
            SelectedVoiceProfile!,
            VoiceProfileName.Trim());
    }

    private void DeleteVoiceProfile()
    {
        if (State == SessionState.Listening
            || SelectedVoiceProfile is null)
        {
            return;
        }

        DeleteVoiceProfileRequested?.Invoke(SelectedVoiceProfile);
    }

    private bool CanStartVoiceProfileRecording()
    {
        return State != SessionState.Listening
            && !IsVoiceProfileRecording
            && SelectedMicrophone is not null
            && SelectedVoiceProfile is null
            && !string.IsNullOrWhiteSpace(VoiceProfileName);
    }

    private void StartVoiceProfileRecording()
    {
        if (!CanStartVoiceProfileRecording())
        {
            return;
        }

        IsVoiceProfileRecording = true;
        VoiceProfileRecordingSecondsRemaining =
            VoiceProfileRecordingLimitSeconds;
        ActivityMessage = "Говорите обычным голосом. Нажмите «Завершить», когда закончите.";
        ReportTranslationProgress(0, "Запись голосового профиля");
        StartVoiceProfileRecordingRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StopVoiceProfileRecording()
    {
        if (!IsVoiceProfileRecording)
        {
            return;
        }

        ActivityMessage = "Завершаю запись и сохраняю профиль.";
        StopVoiceProfileRecordingRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetPrerequisite<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);

        if (propertyName == nameof(IsModelPreflightPassed))
        {
            OnPropertyChanged(nameof(ModelPreflightState));
        }

        UpdateReadinessAndCommands();
    }

    private void UpdateReadinessAndCommands()
    {

        if (State is SessionState.Draft or SessionState.Ready)
        {
            SessionState readinessState =
                HasAllPrerequisites() ? SessionState.Ready : SessionState.Draft;

            if (state != readinessState)
            {
                state = readinessState;
                OnPropertyChanged(nameof(State));
            }
        }

        OnPropertyChanged(nameof(StatusMessage));
        RaiseCommandStates();
    }

    private bool HasAllPrerequisites()
    {
        return IsModelPreflightPassed
            && !IsVoiceProfileRecording
            && SelectedMicrophone is not null
            && (
                SelectedOutputMode == OutputMode.VirtualCable
                || SelectedPhysicalOutput is not null
            )
            && (
                SelectedOutputMode == OutputMode.Physical
                || SelectedVirtualOutput is not null
            )
            && (
                SelectedOutputMode == OutputMode.Physical
                || OutputChannelTestPassed
            )
            && SelectedTargetLanguage is not null
            && SelectedVoiceProfile is not null
            && IsWorkerReady;
    }

    private void RaiseCommandStates()
    {
        startCommand.RaiseCanExecuteChanged();
        stopCommand.RaiseCanExecuteChanged();
        newSessionCommand.RaiseCanExecuteChanged();
        newVoiceProfileCommand.RaiseCanExecuteChanged();
        renameVoiceProfileCommand.RaiseCanExecuteChanged();
        deleteVoiceProfileCommand.RaiseCanExecuteChanged();
        startVoiceProfileRecordingCommand.RaiseCanExecuteChanged();
        stopVoiceProfileRecordingCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 100);
    }

    private static AudioDeviceInfo? SelectDefaultDevice(
        IReadOnlyList<AudioDeviceInfo> devices)
    {
        foreach (AudioDeviceInfo device in devices)
        {
            if (device.IsDefault)
            {
                return device;
            }
        }

        return devices.Count > 0 ? devices[0] : null;
    }

    private sealed class RelayCommand(
        Action execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute();
        }

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
