using FluentAssertions;
using NAudio.Wave;
using VoiceTranslator.App.Services;

namespace VoiceTranslator.UnitTests.Services;

public sealed class VoiceProfileStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "voice-translator-profile-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateLoadRenameAndDeleteProfile()
    {
        using var store = new VoiceProfileStore(directory);
        byte[] reference = CreateWave();

        VoiceProfile created = await store.CreateAsync("Основной", reference);
        IReadOnlyList<VoiceProfile> profiles =
            await store.LoadProfilesAsync();
        byte[] loadedReference = await store.LoadReferenceAsync(created.Id);
        VoiceProfile renamed = await store.RenameAsync(
            created.Id,
            "Для игр");
        await store.DeleteAsync(created.Id);

        profiles.Should().ContainSingle().Which.Should().Be(created);
        loadedReference.Should().Equal(reference);
        renamed.Name.Should().Be("Для игр");
        (await store.LoadProfilesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ReferenceIsEncryptedForTheCurrentWindowsUser()
    {
        using var store = new VoiceProfileStore(directory);
        byte[] reference = CreateWave();

        VoiceProfile created = await store.CreateAsync("Основной", reference);
        string protectedPath = Path.Combine(
            directory,
            $"{created.Id:D}.voiceprofile");
        byte[] protectedBytes = await File.ReadAllBytesAsync(protectedPath);

        protectedBytes.Should().NotEqual(reference);
        protectedBytes.Take(4).Should().NotEqual(
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F']);
    }

    [Fact]
    public async Task DuplicateNamesAreRejectedIgnoringCase()
    {
        using var store = new VoiceProfileStore(directory);
        await store.CreateAsync("Основной", CreateWave());

        Func<Task> duplicate = () => store.CreateAsync(
            "основной",
            CreateWave());

        await duplicate.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InvalidReferenceIsNotPersisted()
    {
        using var store = new VoiceProfileStore(directory);

        Func<Task> create = () => store.CreateAsync("Профиль", [1, 2, 3]);

        await create.Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(directory).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CreateWave()
    {
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(
            stream,
            new WaveFormat(16_000, 16, 1)))
        {
            writer.Write(new byte[16_000]);
        }
        return stream.ToArray();
    }
}
