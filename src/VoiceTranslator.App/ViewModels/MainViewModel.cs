using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Sessions;

namespace VoiceTranslator.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly RelayCommand startCommand;
    private readonly RelayCommand stopCommand;
    private readonly RelayCommand newSessionCommand;
    private TargetLanguage? selectedTargetLanguage;
    private string? selectedMicrophone;
    private string? selectedPhysicalOutput;
    private bool speakerConsentAccepted;
    private bool isModelPreflightPassed;
    private bool isWorkerReady;
    private SessionState state = SessionState.Draft;
    private string performanceProfile = "Unavailable";

    public MainViewModel()
    {
        startCommand = new RelayCommand(Start, CanStart);
        stopCommand = new RelayCommand(Stop, () => State == SessionState.Listening);
        newSessionCommand = new RelayCommand(
            BeginNewSession,
            () => State == SessionState.Stopped);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<TargetLanguage> TargetLanguages { get; } =
        TargetLanguage.All;

    public IReadOnlyList<string> Microphones { get; } = [];

    public IReadOnlyList<string> PhysicalOutputs { get; } = [];

    public TargetLanguage? SelectedTargetLanguage
    {
        get => selectedTargetLanguage;
        set => SetPrerequisite(ref selectedTargetLanguage, value);
    }

    public string? SelectedMicrophone
    {
        get => selectedMicrophone;
        set => SetPrerequisite(ref selectedMicrophone, value);
    }

    public string? SelectedPhysicalOutput
    {
        get => selectedPhysicalOutput;
        set => SetPrerequisite(ref selectedPhysicalOutput, value);
    }

    public bool SpeakerConsentAccepted
    {
        get => speakerConsentAccepted;
        set => SetPrerequisite(ref speakerConsentAccepted, value);
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

    public string ModelPreflightState =>
        IsModelPreflightPassed ? "Models verified" : "Models require preflight";

    public string StatusMessage
    {
        get
        {
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

            if (string.IsNullOrWhiteSpace(SelectedMicrophone))
            {
                return "Select a microphone.";
            }

            if (string.IsNullOrWhiteSpace(SelectedPhysicalOutput))
            {
                return "Select a physical output device.";
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

    private bool CanStart()
    {
        return State == SessionState.Ready
            && IsModelPreflightPassed
            && !string.IsNullOrWhiteSpace(SelectedMicrophone)
            && !string.IsNullOrWhiteSpace(SelectedPhysicalOutput)
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
    }

    private void Stop()
    {
        if (State != SessionState.Listening)
        {
            return;
        }

        SpeakerConsentAccepted = false;
        State = SessionState.Stopped;
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
            && !string.IsNullOrWhiteSpace(SelectedMicrophone)
            && !string.IsNullOrWhiteSpace(SelectedPhysicalOutput)
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
