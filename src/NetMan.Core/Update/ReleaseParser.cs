using System.Text.Json;

namespace NetMan.Core;

public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record ReleaseInfo(Version Version, Uri ExeUrl, Uri ShaUrl);

/// <summary>Reads the GitHub "latest release" document and the release's checksum file.</summary>
public static class ReleaseParser
{
    public const string ExeAsset = "NetMan.exe";
    public const string ShaAsset = "NetMan.exe.sha256";
    private const int Sha256HexLength = 64;

    /// <summary>The release, or null when it is a draft, a prerelease, or lacks the exe or its checksum.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new UpdateException("Release information is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tag_name", out var tag)
                || tag.ValueKind != JsonValueKind.String
                || !AppVersion.TryParse(tag.GetString(), out var version))
            {
                throw new UpdateException("Release has no recognised version tag.");
            }

            if (IsTrue(root, "draft") || IsTrue(root, "prerelease"))
            {
                return null;
            }

            var exe = AssetUrl(root, ExeAsset);
            var sha = AssetUrl(root, ShaAsset);
            return exe is null || sha is null ? null : new ReleaseInfo(version, exe, sha);
        }
    }

    /// <summary>Accepts "hex" or "hex  filename" as written by sha256sum and Get-FileHash.</summary>
    public static string ParseSha256(string text)
    {
        var token = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (token.Length != Sha256HexLength || !token.All(char.IsAsciiHexDigit))
        {
            throw new UpdateException("Checksum file is not a SHA-256 hash.");
        }

        return token.ToLowerInvariant();
    }

    private static bool IsTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static Uri? AssetUrl(JsonElement root, string name)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out var n) && n.GetString() == name
                && asset.TryGetProperty("browser_download_url", out var u)
                && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var url)
                && url.Scheme == Uri.UriSchemeHttps)
            {
                return url;
            }
        }

        return null;
    }
}
