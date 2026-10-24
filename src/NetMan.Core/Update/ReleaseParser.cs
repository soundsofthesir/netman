using System.Text.Json;

namespace NetMan.Core;

public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record ReleaseInfo(Version Version, Uri ExeUrl, Uri SigUrl);

/// <summary>Reads the GitHub "latest release" document.</summary>
public static class ReleaseParser
{
    public const string ExeAsset = "NetMan.exe";
    public const string SigAsset = "NetMan.exe.sig";

    /// <summary>The release, or null when it is a draft, a prerelease, or lacks the exe or its signature.</summary>
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
            var sig = AssetUrl(root, SigAsset);
            return exe is null || sig is null ? null : new ReleaseInfo(version, exe, sig);
        }
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
