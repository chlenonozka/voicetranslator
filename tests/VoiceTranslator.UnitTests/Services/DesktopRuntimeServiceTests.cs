using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using VoiceTranslator.App.Services;
using VoiceTranslator.App.ViewModels;
using VoiceTranslator.Application.Orchestration;
using VoiceTranslator.Application.Ports;
using VoiceTranslator.Domain.Audio;
using VoiceTranslator.Domain.Sessions;
using Xunit;

namespace VoiceTranslator.UnitTests.Services;


// Make sure to access the internal interface from App.Services
public sealed class DesktopRuntimeServiceTests
{
    [Fact]
    public async Task GpuOomCleanupCompletesWithoutDeadlock()
    {
        var viewModel = new MainViewModel();
        var store = new VoiceProfileStore();
        var service = new DesktopRuntimeService(viewModel, store);
        var session = new FakeSessionHost();
        service.SessionFactory = new FakeSessionFactory(session);
        service.DispatcherFunc = action => { action(); return Task.CompletedTask; };

        // Act
        var cleanupTask = service.OnSessionFailureAsync(SessionFailure.GpuMemoryExhausted, CancellationToken.None);

        // Assert
        var timeout = Task.Delay(TimeSpan.FromSeconds(2));
        var taskToWait = service.ActiveCleanupTask ?? cleanupTask;
        var completed = await Task.WhenAny(taskToWait, timeout);
        completed.Should().Be(taskToWait, "cleanup task should not deadlock");
        completed.Should().Be(cleanupTask, "cleanup task should not deadlock");
    }

    [Fact]
    public async Task ConcurrentOrRecursiveFailureIsHandledOnlyOnce()
    {
        var viewModel = new MainViewModel();
        var store = new VoiceProfileStore();
        var service = new DesktopRuntimeService(viewModel, store);
        var session = new FakeSessionHost();
        service.SessionFactory = new FakeSessionFactory(session);
        service.DispatcherFunc = action => { action(); return Task.CompletedTask; };

        // Force the session to exist
        await session.StartAsync(service);

        // Act
        var task1 = service.OnSessionFailureAsync(SessionFailure.GpuMemoryExhausted, CancellationToken.None);
        var task2 = service.OnSessionFailureAsync(SessionFailure.WorkerExited, CancellationToken.None);

        await Task.WhenAll(task1, task2);
        if (service.ActiveCleanupTask != null) await service.ActiveCleanupTask;

        // Assert
        session.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task GpuOomCleanupReleasesAllSessionResources()
    {
        var viewModel = new MainViewModel();
        var store = new VoiceProfileStore();
        var service = new DesktopRuntimeService(viewModel, store);
        var session = new FakeSessionHost();
        service.SessionFactory = new FakeSessionFactory(session);
        service.DispatcherFunc = action => { action(); return Task.CompletedTask; };

        await session.StartAsync(service);

        // Act
        await service.OnSessionFailureAsync(SessionFailure.GpuMemoryExhausted, CancellationToken.None);
        if (service.ActiveCleanupTask != null) await service.ActiveCleanupTask;

        // Assert
        session.DisposeCount.Should().Be(1);
        session.StopCount.Should().Be(1);
    }

    [Fact]
    public async Task CleanupExceptionsAreObserved()
    {
        var viewModel = new MainViewModel();
        var store = new VoiceProfileStore();
        var service = new DesktopRuntimeService(viewModel, store);
        var session = new FakeSessionHost { ThrowOnDispose = true };
        service.SessionFactory = new FakeSessionFactory(session);
        service.DispatcherFunc = action => { action(); return Task.CompletedTask; };

        await session.StartAsync(service);

        // Act
        await service.OnSessionFailureAsync(SessionFailure.GpuMemoryExhausted, CancellationToken.None);

        Func<Task> act = async () => await service.DisposeAsync();

        // Ensure exception is observed in DisposeAsync
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class FakeSessionHost : ISessionHost, ISessionStopper
    {
        public int DisposeCount { get; private set; }
        public int StopCount { get; private set; }
        public bool ThrowOnDispose { get; set; }

        public event Action<Exception>? Failed { add { } remove { } }
        public event Action<double>? InputLevelChanged { add { } remove { } }
        public event Action<double>? OutputLevelChanged { add { } remove { } }
        public event Action<string>? ActivityChanged { add { } remove { } }
        public event Action<int, string>? ProgressChanged { add { } remove { } }

        public ISessionStopper SessionStopper => this;
        public void Start() { }

        public Task StartAsync(DesktopRuntimeService service)
        {
            var viewModel = new MainViewModel();
            viewModel.SelectedMicrophone = new VoiceTranslator.Infrastructure.Audio.Devices.AudioDeviceInfo("mic", "mic", false, false);
            viewModel.SelectedPhysicalOutput = new VoiceTranslator.Infrastructure.Audio.Devices.AudioDeviceInfo("out", "out", false, false);
            viewModel.SelectedTargetLanguage = new VoiceTranslator.Domain.Languages.TargetLanguage("en", "English", "eng", "eng_Latn");
            viewModel.SelectedVoiceProfile = new VoiceTranslator.App.Services.VoiceProfile(Guid.NewGuid(), "Test", DateTimeOffset.UtcNow);
            // Set service reflection properties
            var prop = typeof(DesktopRuntimeService).GetField("translationSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            prop?.SetValue(service, this);
            return Task.CompletedTask;
        }

        public Task StopSessionAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("Test exception");
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSessionFactory : ISessionHostFactory
    {
        private readonly FakeSessionHost host;

        public FakeSessionFactory(FakeSessionHost host)
        {
            this.host = host;
        }

        public ISessionHost Create(
            ILocalTranslationWorker worker,
            VoiceTranslator.Infrastructure.Audio.Devices.AudioDeviceInfo microphone,
            VoiceTranslator.Infrastructure.Audio.Devices.AudioDeviceInfo? physicalOutput,
            VoiceTranslator.Infrastructure.Audio.Devices.AudioDeviceInfo? virtualOutput,
            OutputMode outputMode,
            string targetLanguage,
            string performanceProfile,
            byte[]? referenceWav,
            ISessionFailureObserver failureObserver)
        {
            return host;
        }
    }
}
