using FluentAssertions;
using VoiceTranslator.Infrastructure.Audio.Devices;

namespace VoiceTranslator.IntegrationTests.Audio;

public sealed class AudioDeviceCatalogTests
{
    [Fact]
    public void RefreshDetectsDeviceChangesAndClassifiesVirtualCable()
    {
        var source = new FakeEndpointSource
        {
            Captures =
            [
                new AudioDeviceInfo("mic-1", "Microphone", false),
            ],
            Renders =
            [
                new AudioDeviceInfo(
                    "cable-1",
                    "CABLE Input (VB-Audio Virtual Cable)",
                    true),
            ],
        };
        using var catalog = new WasapiDeviceCatalog(source);
        var changes = 0;
        catalog.DevicesChanged += (_, _) => changes++;

        catalog.Refresh();
        source.Captures = [];
        catalog.Refresh();

        changes.Should().Be(2);
        catalog.CaptureDevices.Should().BeEmpty();
        catalog.RenderDevices.Should().ContainSingle()
            .Which.IsVirtual.Should().BeTrue();
    }

    private sealed class FakeEndpointSource : IAudioEndpointSource
    {

        private readonly object _lock = new();
        private IReadOnlyList<AudioDeviceInfo> _captures = [];
        public IReadOnlyList<AudioDeviceInfo> Captures
        {
            get { lock (_lock) return _captures; }
            set { lock (_lock) _captures = value; }
        }


        private IReadOnlyList<AudioDeviceInfo> _renders = [];
        public IReadOnlyList<AudioDeviceInfo> Renders
        {
            get { lock (_lock) return _renders; }
            set { lock (_lock) _renders = value; }
        }


        public IReadOnlyList<AudioDeviceInfo> EnumerateCaptureDevices() =>
            Captures;

        public IReadOnlyList<AudioDeviceInfo> EnumerateRenderDevices() =>
            Renders;

        public void Dispose()
        {
        }
    }
}
