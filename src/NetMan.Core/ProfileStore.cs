using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetMan.Core;

public sealed class ProfileStoreException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Reads and writes profiles as JSON. Saves go to a temp file first and replace the old file in one step.</summary>
public sealed class ProfileStore(string path)
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; } = path;

    public IReadOnlyList<Profile> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        ProfileFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(FilePath), Options);
        }
        catch (JsonException ex)
        {
            throw new ProfileStoreException($"{FilePath} is not valid JSON.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProfileStoreException($"Cannot read {FilePath}.", ex);
        }

        if (file is null || file.Version != CurrentVersion)
        {
            throw new ProfileStoreException($"{FilePath} has an unsupported format.");
        }

        var profiles = file.Profiles ?? [];
        if (profiles.Any(p => p is null))
        {
            throw new ProfileStoreException($"{FilePath} contains an empty profile.");
        }

        return profiles.Select(p => Normalize(p!)).ToList().AsReadOnly();
    }

    public void Save(IReadOnlyList<Profile> profiles)
    {
        var json = JsonSerializer.Serialize(new ProfileFile(CurrentVersion, profiles), Options);
        var temp = FilePath + ".tmp";

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(FilePath))
            {
                File.Replace(temp, FilePath, FilePath + ".bak");
            }
            else
            {
                File.Move(temp, FilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProfileStoreException($"Cannot write {FilePath}.", ex);
        }
    }

    // System.Text.Json assigns JSON nulls to non-nullable strings, so map them back to empty.
    private static Profile Normalize(Profile p) => p with
    {
        Name = p.Name ?? "",
        Notes = p.Notes ?? "",
        AdapterName = p.AdapterName ?? "",
        Ip = p.Ip ?? "",
        Mask = p.Mask ?? "",
        Gateway = p.Gateway ?? "",
        Dns1 = p.Dns1 ?? "",
        Dns2 = p.Dns2 ?? "",
        PingTarget = p.PingTarget ?? "",
    };

    private sealed record ProfileFile(int Version, IReadOnlyList<Profile?>? Profiles);
}
