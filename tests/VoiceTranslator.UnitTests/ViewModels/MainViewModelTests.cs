using FluentAssertions;
using VoiceTranslator.App.ViewModels;
using VoiceTranslator.Domain.Audio;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Sessions;
using VoiceTranslator.Infrastructure.Audio.Devices;
using VoiceTranslator.Application.Ports;

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

    [Fact]
    public void UpdateDevicesExposesSelectableEndpointIds()
    {
        MainViewModel viewModel = new();
        var microphone = new AudioDeviceInfo("mic-1", "Microphone", false);
        var speakers = new AudioDeviceInfo("out-1", "Speakers", false);

        viewModel.UpdateDevices([microphone], [speakers]);

        viewModel.Microphones.Should().ContainSingle().Which
            .Should().Be(microphone);
        viewModel.PhysicalOutputs.Should().ContainSingle().Which
            .Should().Be(speakers);
    }

    [Fact]
    public void ApplyPreflightUpdatesWorkerAndModelReadiness()
    {
        MainViewModel viewModel = new();

        viewModel.ApplyPreflight(
            new WorkerPreflightReport(
                Ready: true,
                CudaAvailable: true,
                DeviceName: "RTX 3070",
                TotalVramBytes: 8 * 1024L * 1024L * 1024L,
                FreeVramBytes: 6 * 1024L * 1024L * 1024L,
                PerformanceProfile: "balanced",
                MissingModels: [],
                AvailableLanguages: ["en"]));

        viewModel.IsWorkerReady.Should().BeTrue();
        viewModel.IsModelPreflightPassed.Should().BeTrue();
        viewModel.PerformanceProfile.Should().Be("balanced");
        viewModel.ModelInventorySummary.Should().Contain("All models verified");
    }

    [Fact]
    public void ApplyPreflightExposesOnlyPassingTargetLanguages()
    {
        MainViewModel viewModel = new();

        viewModel.ApplyPreflight(
            new WorkerPreflightReport(
                Ready: true,
                CudaAvailable: true,
                DeviceName: "RTX 3070",
                TotalVramBytes: 8 * 1024L * 1024L * 1024L,
                FreeVramBytes: 6 * 1024L * 1024L * 1024L,
                PerformanceProfile: "balanced",
                MissingModels: [],
                AvailableLanguages: ["en", "fr"]));

        viewModel.TargetLanguages
            .Select(language => language.Code)
            .Should().Equal("en", "fr");
    }

    [Fact]
    public void ReportWorkerFailureMovesViewModelToFaulted()
    {
        MainViewModel viewModel = CreateReadyViewModel();

        viewModel.ReportWorkerFailure("Worker heartbeat timed out.");

        viewModel.State.Should().Be(SessionState.Faulted);
        viewModel.IsWorkerReady.Should().BeFalse();
        viewModel.StatusMessage.Should().Contain("heartbeat");
    }

    [Fact]
    public void VirtualOutputRequiresVirtualDeviceAndChannelTest()
    {
        MainViewModel viewModel = CreateReadyViewModel();

        viewModel.SelectedOutputMode = OutputMode.VirtualCable;
        viewModel.SelectedPhysicalOutput = null;

        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
        viewModel.StatusMessage.Should().Contain("virtual cable");

        viewModel.SelectedVirtualOutput =
            new AudioDeviceInfo("virtual", "VB-CABLE", true);

        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
        viewModel.StatusMessage.Should().Contain("output channel test");

        viewModel.ApplyOutputChannelTest(
            new OutputChannelTestResult(Passed: true, Warning: null));

        viewModel.StartCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void DeviceListKeepsAllRenderDevicesSelectableForPhysicalOutput()
    {
        MainViewModel viewModel = new();
        var speakers = new AudioDeviceInfo("out-1", "Speakers", false);
        var cable = new AudioDeviceInfo("out-2", "VB-CABLE", true);

        viewModel.UpdateDevices([], [speakers, cable]);

        viewModel.PhysicalOutputs.Should().Equal(speakers, cable);
        viewModel.VirtualOutputs.Should().ContainSingle().Which
            .Should().Be(cable);
    }

    [Fact]
    public void DeviceListFallsBackToAllRenderDevicesWhenNoVirtualCableIsDetected()
    {
        MainViewModel viewModel = new();
        var speakers = new AudioDeviceInfo("out-1", "Speakers", false);

        viewModel.UpdateDevices([], [speakers]);

        viewModel.PhysicalOutputs.Should().ContainSingle().Which
            .Should().Be(speakers);
        viewModel.VirtualOutputs.Should().ContainSingle().Which
            .Should().Be(speakers);
    }

    [Fact]
    public void StartAndStopCommandsNotifyDesktopRuntime()
    {
        MainViewModel viewModel = CreateReadyViewModel();
        int starts = 0;
        int stops = 0;
        viewModel.StartRequested += (_, _) => starts++;
        viewModel.StopRequested += (_, _) => stops++;

        viewModel.StartCommand.Execute(null);
        viewModel.StopCommand.Execute(null);

        starts.Should().Be(1);
        stops.Should().Be(1);
    }

    private static MainViewModel CreateReadyViewModel()
    {
        return new MainViewModel
        {
            IsModelPreflightPassed = true,
            SelectedMicrophone =
                new AudioDeviceInfo("mic", "Test microphone", false),
            SelectedPhysicalOutput =
                new AudioDeviceInfo("out", "Test speakers", false),
            SelectedTargetLanguage = TargetLanguage.English,
            SpeakerConsentAccepted = true,
            IsWorkerReady = true,
        };
    }
}
