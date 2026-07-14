using FluentAssertions;
using VoiceTranslator.App.ViewModels;
using VoiceTranslator.Domain.Audio;
using VoiceTranslator.Domain.Languages;
using VoiceTranslator.Domain.Sessions;
using VoiceTranslator.Infrastructure.Audio.Devices;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.App.Services;

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
            candidate => candidate.ApplyVoiceProfiles([]),
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
    public void StopKeepsSelectedVoiceProfile()
    {
        MainViewModel viewModel = CreateReadyViewModel();
        viewModel.StartCommand.Execute(null);

        viewModel.StopCommand.Execute(null);

        viewModel.SelectedVoiceProfile.Should().NotBeNull();
    }

    [Fact]
    public void StartingAgainRequiresAnExplicitNewSessionButKeepsProfile()
    {
        MainViewModel viewModel = CreateReadyViewModel();
        viewModel.StartCommand.Execute(null);
        viewModel.StopCommand.Execute(null);

        viewModel.State.Should().Be(SessionState.Stopped);
        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
        viewModel.NewSessionCommand.CanExecute(null).Should().BeTrue();
        viewModel.StatusMessage.Should().Be(
            "Перевод остановлен. Создайте новую сессию, чтобы продолжить.");

        viewModel.NewSessionCommand.Execute(null);

        viewModel.State.Should().Be(SessionState.Ready);
        viewModel.SelectedVoiceProfile.Should().NotBeNull();
        viewModel.StartCommand.CanExecute(null).Should().BeTrue();
        viewModel.NewSessionCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void NewProfileNameEnablesStartWithoutSavedProfile()
    {
        MainViewModel viewModel = CreateReadyViewModel();

        viewModel.NewVoiceProfileCommand.Execute(null);
        viewModel.VoiceProfileName = "Новый голос";

        viewModel.SelectedVoiceProfile.Should().BeNull();
        viewModel.State.Should().Be(SessionState.Ready);
        viewModel.StartCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ProfileCommandsRequestRenameAndDelete()
    {
        MainViewModel viewModel = CreateReadyViewModel();
        VoiceProfile selected = viewModel.SelectedVoiceProfile!;
        (VoiceProfile Profile, string Name)? rename = null;
        VoiceProfile? deleted = null;
        viewModel.RenameVoiceProfileRequested +=
            (profile, name) => rename = (profile, name);
        viewModel.DeleteVoiceProfileRequested += profile => deleted = profile;

        viewModel.VoiceProfileName = "Основной голос";
        viewModel.RenameVoiceProfileCommand.Execute(null);
        viewModel.DeleteVoiceProfileCommand.Execute(null);

        rename.Should().Be((selected, "Основной голос"));
        deleted.Should().Be(selected);
    }

    [Fact]
    public void DefaultsExplainThatWorkerAndModelsAreUnavailable()
    {
        MainViewModel viewModel = new();

        viewModel.TargetLanguages.Should().BeEquivalentTo(TargetLanguage.All);
        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
        viewModel.StatusMessage.Should().Contain("обработчик");
        viewModel.StatusMessage.Should().Contain("модели");
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
    public void UpdateDevicesSelectsSystemDefaultInputAndOutput()
    {
        MainViewModel viewModel = new();
        var microphone = new AudioDeviceInfo(
            "mic-default",
            "Default microphone",
            false,
            IsDefault: true);
        var speakers = new AudioDeviceInfo(
            "out-default",
            "Default speakers",
            false,
            IsDefault: true);

        viewModel.UpdateDevices([microphone], [speakers]);

        viewModel.SelectedMicrophone.Should().Be(microphone);
        viewModel.SelectedPhysicalOutput.Should().Be(speakers);
    }

    [Fact]
    public void LevelIndicatorsClampToPercentRange()
    {
        MainViewModel viewModel = new();

        viewModel.ReportInputLevel(125);
        viewModel.ReportOutputLevel(double.NaN);
        viewModel.ReportActivity("Обрабатываю фразу.");
        viewModel.ReportTranslationProgress(125, "Озвучивание");

        viewModel.InputLevelPercent.Should().Be(100);
        viewModel.OutputLevelPercent.Should().Be(0);
        viewModel.ActivityMessage.Should().Be("Обрабатываю фразу.");
        viewModel.TranslationProgressPercent.Should().Be(100);
        viewModel.TranslationProgressLabel.Should().Be("Озвучивание");
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
        viewModel.ModelInventorySummary.Should().Contain("Все модели проверены");
        viewModel.PerformanceProfileDisplay.Should().Be("Сбалансированный");
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
        viewModel.StatusMessage.Should().Contain("виртуального кабеля");

        viewModel.SelectedVirtualOutput =
            new AudioDeviceInfo("virtual", "VB-CABLE", true);

        viewModel.StartCommand.CanExecute(null).Should().BeFalse();
        viewModel.StatusMessage.Should().Contain("канал вывода");

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
        var viewModel = new MainViewModel
        {
            IsModelPreflightPassed = true,
            SelectedMicrophone =
                new AudioDeviceInfo("mic", "Test microphone", false),
            SelectedPhysicalOutput =
                new AudioDeviceInfo("out", "Test speakers", false),
            SelectedTargetLanguage = TargetLanguage.English,
            IsWorkerReady = true,
        };
        viewModel.ApplyVoiceProfiles(
        [
            new VoiceProfile(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Основной",
                DateTimeOffset.UtcNow),
        ]);
        return viewModel;
    }
}
