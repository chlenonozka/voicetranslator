using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VoiceTranslator.Domain.Audio;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Sessions;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Infrastructure.Audio.Devices;

namespace VoiceTranslator.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly RelayCommand startCommand;
    private readonly RelayCommand stopCommand;
    private readonly RelayCommand newSessionCommand;
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
    private bool speakerConsentAccepted;
    private bool outputChannelTestPassed;
    private bool isModelPreflightPassed;
    private bool isWorkerReady;
    private SessionState state = SessionState.Draft;
    private string performanceProfile = "Unavailable";
    private string modelInventorySummary = "Model inventory unavailable";
    private string? outputWarning;
    private string? failureMessage;

    public MainViewModel()
    {
        startCommand = new RelayCommand(Start, CanStart);
        stopCommand = new RelayCommand(Stop, () => State == SessionState.Listening);
        newSessionCommand = new RelayCommand(
            BeginNewSession,
            () => State == SessionState.Stopped);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? StartRequested;

    public event EventHandler? StopRequested;

    public IReadOnlyList<TargetLanguage> TargetLanguages => targetLanguages;

    public IReadOnlyList<AudioDeviceInfo> Microphones => microphones;

    public IReadOnlyList<AudioDeviceInfo> PhysicalOutputs => physicalOutputs;

    public IReadOnlyList<AudioDeviceInfo> VirtualOutputs => virtualOutputs;

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

    public bool SpeakerConsentAccepted
    {
        get => speakerConsentAccepted;
        set => SetPrerequisite(ref speakerConsentAccepted, value);
    }

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

    public string ModelPreflightState =>
        IsModelPreflightPassed ? "Models verified" : "Models require preflight";

    public string StatusMessage
    {
        get
        {
            if (State == SessionState.Faulted)
            {
                return failureMessage ?? "The local worker failed.";
            }

            if (State == SessionState.Listening)
            {
                return "Listening for Russian speech.";
            }

            if (State == SessionState.Stopped)
            {
                return "Translation stopped. Start a new session to continue.";
            }

            if (!IsWorkerReady && !IsModelPreflightPassed)
            {
                return "Local worker and required models are unavailable.";
            }

            if (!IsWorkerReady)
            {
                return "Local worker is unavailable.";
            }

            if (!IsModelPreflightPassed)
            {
                return "Required models have not passed preflight.";
            }

            if (SelectedMicrophone is null)
            {
                return "Select a microphone.";
            }

            if (
                SelectedOutputMode is OutputMode.Physical or OutputMode.Both
                && SelectedPhysicalOutput is null
            )
            {
                return "Select a physical output device.";
            }

            if (
                SelectedOutputMode is OutputMode.VirtualCable or OutputMode.Both
                && SelectedVirtualOutput is null
            )
            {
                return "Select a virtual cable output device.";
            }

            if (
                SelectedOutputMode is OutputMode.VirtualCable or OutputMode.Both
                && !OutputChannelTestPassed
            )
            {
                return "Run the output channel test before using virtual output.";
            }

            if (SelectedTargetLanguage is null)
            {
                return "Select a target language.";
            }

            if (!SpeakerConsentAccepted)
            {
                return "Accept speaker timbre consent for this session.";
            }

            return "Ready to start.";
        }
    }

    public ICommand StartCommand => startCommand;

    public ICommand StopCommand => stopCommand;

    public ICommand NewSessionCommand => newSessionCommand;

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
        virtualOutputs = detectedVirtualOutputs.Length == 0
            ? renderDevices.ToArray()
            : detectedVirtualOutputs;
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
    }

    public void ApplyPreflight(WorkerPreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        failureMessage = null;
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
        ModelInventorySummary = report.MissingModels.Count == 0
            ? $"All models verified. {targetLanguages.Count} languages available."
            : "Missing models: " + string.Join(", ", report.MissingModels);
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
    }

    private bool CanStart()
    {
        return State == SessionState.Ready
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
            && SpeakerConsentAccepted
            && IsWorkerReady;
    }

    private void Start()
    {
        if (!CanStart())
        {
            return;
        }

        State = SessionState.Listening;
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Stop()
    {
        if (State != SessionState.Listening)
        {
            return;
        }

        SpeakerConsentAccepted = false;
        State = SessionState.Stopped;
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BeginNewSession()
    {
        if (State != SessionState.Stopped)
        {
            return;
        }

        SpeakerConsentAccepted = false;
        State = SessionState.Draft;
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
            && SpeakerConsentAccepted
            && IsWorkerReady;
    }

    private void RaiseCommandStates()
    {
        startCommand.RaiseCanExecuteChanged();
        stopCommand.RaiseCanExecuteChanged();
        newSessionCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
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
