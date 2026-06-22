using FluentAssertions;
using VoiceTranslator.App.ViewModels;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Sessions;

namespace VoiceTranslator.UnitTests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void StartIsDisabledWhenAnyPrerequisiteIsMissing()
    {
        MainViewModel viewModel = CreateReadyViewModel();

        Action<MainViewModel>[] removePrerequisite =
        [
            candidate => candidate.IsModelPreflightPassed = false,
            candidate => candidate.SelectedMicrophone = null,
            candidate => candidate.SelectedPhysicalOutput = null,
            candidate => candidate.SelectedTargetLanguage = null,
            candidate => candidate.SpeakerConsentAccepted = false,
            candidate => candidate.IsWorkerReady = false,
        ];

        foreach (Action<MainViewModel> remove in removePrerequisite)
        {
            viewModel = CreateReadyViewModel();
            remove(viewModel);

            viewModel.StartCommand.CanExecute(null).Should().BeFalse();
        }
    }

    [Fact]
    public void StartIsEnabledWhenEveryPrerequisiteIsPresent()
    {
        MainViewModel viewModel = CreateReadyViewModel();

        viewModel.StartCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ChangingAPrerequisiteReevaluatesStartCommand()
    {
        MainViewModel viewModel = CreateReadyViewModel();
        int notifications = 0;
        viewModel.StartCommand.CanExecuteChanged += (_, _) => notifications++;

        viewModel.IsWorkerReady = false;

        notifications.Should().Be(1);
        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void StartMovesReadyViewModelToListening()
    {
        MainViewModel viewModel = CreateReadyViewModel();

        viewModel.StartCommand.Execute(null);

        viewModel.State.Should().Be(SessionState.Listening);
        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
        viewModel.StopCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void StopMovesListeningViewModelToStopped()
    {
        MainViewModel viewModel = CreateReadyViewModel();
        viewModel.StartCommand.Execute(null);

        viewModel.StopCommand.Execute(null);

        viewModel.State.Should().Be(SessionState.Stopped);
        viewModel.StopCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void StopClearsPerSessionSpeakerConsent()
    {
        MainViewModel viewModel = CreateReadyViewModel();
        viewModel.StartCommand.Execute(null);

        viewModel.StopCommand.Execute(null);

        viewModel.SpeakerConsentAccepted.Should().BeFalse();
        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void StartingAgainRequiresAnExplicitNewSessionAndFreshConsent()
    {
        MainViewModel viewModel = CreateReadyViewModel();
        viewModel.StartCommand.Execute(null);
        viewModel.StopCommand.Execute(null);

        viewModel.SpeakerConsentAccepted = true;

        viewModel.State.Should().Be(SessionState.Stopped);
        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
        viewModel.NewSessionCommand.CanExecute(null).Should().BeTrue();
        viewModel.StatusMessage.Should().Be("Translation stopped. Start a new session to continue.");

        viewModel.NewSessionCommand.Execute(null);

        viewModel.State.Should().Be(SessionState.Draft);
        viewModel.SpeakerConsentAccepted.Should().BeFalse();
        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
        viewModel.NewSessionCommand.CanExecute(null).Should().BeFalse();

        viewModel.SpeakerConsentAccepted = true;

        viewModel.State.Should().Be(SessionState.Ready);
        viewModel.StartCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void DefaultsExplainThatWorkerAndModelsAreUnavailable()
    {
        MainViewModel viewModel = new();

        viewModel.TargetLanguages.Should().BeEquivalentTo(TargetLanguage.All);
        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
        viewModel.StatusMessage.Should().Contain("worker");
        viewModel.StatusMessage.Should().Contain("model");
    }

    private static MainViewModel CreateReadyViewModel()
    {
        return new MainViewModel
        {
            IsModelPreflightPassed = true,
            SelectedMicrophone = "Test microphone",
            SelectedPhysicalOutput = "Test speakers",
            SelectedTargetLanguage = TargetLanguage.English,
            SpeakerConsentAccepted = true,
            IsWorkerReady = true,
        };
    }
}
