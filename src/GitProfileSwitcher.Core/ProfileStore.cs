using System.Text;
using System.Text.Json;

namespace GitProfileSwitcher.Core;

public sealed record ProfilesFile(string? ActiveProfileId, List<Profile> Profiles);

public sealed record ProfileLoadResult(ProfilesFile Data, string? Error)
{
    public bool Ok => Error is null;
}

public sealed class ProfileStore
{
    public const string FileName = "profiles.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public ProfileStore(string directoryPath) => DirectoryPath = directoryPath;

    public ProfileStore() : this(AppPaths.ConfigDirectory()) { }

    public string DirectoryPath { get; }

    public string FilePath => Path.Combine(DirectoryPath, FileName);

    public ProfileLoadResult Load()
    {
        if (!File.Exists(FilePath))
            return new ProfileLoadResult(new ProfilesFile(null, []), null);

        try
        {
            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<ProfilesFile>(json, JsonOptions);
            if (data is null)
                return new ProfileLoadResult(new ProfilesFile(null, []),
                    "profiles.json esta vacio o no tiene el formato esperado.");

            return new ProfileLoadResult(new ProfilesFile(data.ActiveProfileId, data.Profiles ?? []), null);
        }
        catch (JsonException ex)
        {
            return new ProfileLoadResult(new ProfilesFile(null, []),
                $"No se pudo leer profiles.json: {ex.Message}");
        }
        catch (IOException ex)
        {
            return new ProfileLoadResult(new ProfilesFile(null, []),
                $"No se pudo leer profiles.json: {ex.Message}");
        }
    }

    public void Save(IReadOnlyList<Profile> profiles, string? activeProfileId)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(new ProfilesFile(activeProfileId, profiles.ToList()), JsonOptions);
        var tempPath = Path.Combine(DirectoryPath, $".profiles-{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
