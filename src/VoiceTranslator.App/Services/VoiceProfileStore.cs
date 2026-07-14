using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using NAudio.Wave;

namespace VoiceTranslator.App.Services;

public sealed record VoiceProfile(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt);

public sealed class VoiceProfileStore : IDisposable
{
    private const int MaximumReferenceBytes = 2 * 1024 * 1024;
    private const int MaximumNameLength = 60;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string profileDirectory;
    private readonly SemaphoreSlim gate = new(1, 1);

    public VoiceProfileStore(string? profileDirectory = null)
    {
        this.profileDirectory = profileDirectory ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VoiceTranslator",
            "VoiceProfiles");
    }

    public async Task<IReadOnlyList<VoiceProfile>> LoadProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadProfilesCoreAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<VoiceProfile> CreateAsync(
        string name,
        byte[] referenceWav,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizeName(name);
        ValidateReferenceWave(referenceWav);
        byte[] protectedReference =
            CurrentUserDataProtector.Protect(referenceWav);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<VoiceProfile> profiles =
                await LoadProfilesCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
            EnsureUniqueName(profiles, normalizedName, exceptId: null);

            Directory.CreateDirectory(profileDirectory);
            var profile = new VoiceProfile(
                Guid.NewGuid(),
                normalizedName,
                DateTimeOffset.UtcNow);
            string referencePath = GetReferencePath(profile.Id);
            string metadataPath = GetMetadataPath(profile.Id);
            string referenceTemporaryPath = referencePath + ".tmp";
            string metadataTemporaryPath = metadataPath + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(
                        referenceTemporaryPath,
                        protectedReference,
                        cancellationToken)
                    .ConfigureAwait(false);
                await WriteMetadataAsync(
                        metadataTemporaryPath,
                        profile,
                        cancellationToken)
                    .ConfigureAwait(false);
                File.Move(referenceTemporaryPath, referencePath);
                File.Move(metadataTemporaryPath, metadataPath);
                return profile;
            }
            catch
            {
                DeleteIfPresent(referenceTemporaryPath);
                DeleteIfPresent(metadataTemporaryPath);
                DeleteIfPresent(referencePath);
                DeleteIfPresent(metadataPath);
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedReference);
            gate.Release();
        }
    }

    public async Task<VoiceProfile> RenameAsync(
        Guid profileId,
        string name,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = NormalizeName(name);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<VoiceProfile> profiles =
                await LoadProfilesCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
            VoiceProfile current = profiles.SingleOrDefault(
                profile => profile.Id == profileId)
                ?? throw new KeyNotFoundException(
                    "Голосовой профиль не найден.");
            EnsureUniqueName(profiles, normalizedName, profileId);
            VoiceProfile renamed = current with { Name = normalizedName };
            string metadataPath = GetMetadataPath(profileId);
            string temporaryPath = metadataPath + ".tmp";
            await WriteMetadataAsync(
                    temporaryPath,
                    renamed,
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, metadataPath, overwrite: true);
            return renamed;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DeleteIfPresent(GetMetadataPath(profileId));
            DeleteIfPresent(GetReferencePath(profileId));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<byte[]> LoadReferenceAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[] protectedReference = [];
        try
        {
            string path = GetReferencePath(profileId);
            if (!File.Exists(path))
            {
                throw new KeyNotFoundException(
                    "Образец голоса для профиля не найден.");
            }

            protectedReference = await File
                .ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            byte[] reference =
                CurrentUserDataProtector.Unprotect(protectedReference);
            ValidateReferenceWave(reference);
            return reference;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedReference);
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<VoiceProfile>> LoadProfilesCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(profileDirectory))
        {
            return [];
        }

        var profiles = new List<VoiceProfile>();
        foreach (string metadataPath in Directory.EnumerateFiles(
            profileDirectory,
            "*.json",
            SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using FileStream stream = File.OpenRead(metadataPath);
                VoiceProfile? profile = await JsonSerializer
                    .DeserializeAsync<VoiceProfile>(
                        stream,
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (profile is null)
                {
                    continue;
                }

                if (File.Exists(GetReferencePath(profile.Id)))
                {
                    profiles.Add(profile);
                }
            }
            catch (JsonException)
            {
            }
        }

        return profiles
            .OrderBy(
                profile => profile.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task WriteMetadataAsync(
        string path,
        VoiceProfile profile,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
                stream,
                profile,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = name.Trim();
        if (normalized.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"Имя профиля не должно превышать {MaximumNameLength} символов.",
                nameof(name));
        }

        return normalized;
    }

    private static void EnsureUniqueName(
        IReadOnlyList<VoiceProfile> profiles,
        string name,
        Guid? exceptId)
    {
        if (profiles.Any(profile =>
            profile.Id != exceptId
            && string.Equals(
                profile.Name,
                name,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Голосовой профиль с таким именем уже существует.");
        }
    }

    private static void ValidateReferenceWave(byte[] referenceWav)
    {
        ArgumentNullException.ThrowIfNull(referenceWav);
        if (referenceWav.Length == 0
            || referenceWav.Length > MaximumReferenceBytes)
        {
            throw new InvalidDataException(
                "Недопустимый размер образца голоса.");
        }

        try
        {
            using var stream = new MemoryStream(referenceWav, writable: false);
            using var reader = new WaveFileReader(stream);
            if (reader.WaveFormat.SampleRate != 16_000
                || reader.WaveFormat.BitsPerSample != 16
                || reader.WaveFormat.Channels != 1
                || reader.Length == 0)
            {
                throw new InvalidDataException(
                    "Образец голоса должен быть моно PCM 16 кГц, 16 бит.");
            }
        }
        catch (Exception error)
            when (error is FormatException or EndOfStreamException)
        {
            throw new InvalidDataException(
                "Образец голоса не является корректным WAV-файлом.",
                error);
        }
    }

    private string GetMetadataPath(Guid profileId) =>
        Path.Combine(profileDirectory, $"{profileId:D}.json");

    private string GetReferencePath(Guid profileId) =>
        Path.Combine(profileDirectory, $"{profileId:D}.voiceprofile");

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void Dispose()
    {
        gate.Dispose();
    }
}
