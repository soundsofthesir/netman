using System.Text.Json;

namespace NetMan.Core;

/// <summary>
/// Swaps the running exe for a downloaded one. Windows lets a running exe be renamed but not overwritten, so the
/// current file moves to NetMan.exe.old, the new one is written into place, and the next launch deletes the old file.
/// </summary>
/// <remarks>
/// The updates folder is writable by the user's unelevated programs, and NetMan runs elevated. Nothing read from that
/// folder is trusted: every install reads the file once, checks its signature against the built-in public key, and
/// writes exactly the bytes it checked.
/// </remarks>
public static class UpdateInstaller
{
    public const string MarkerName = "pending.json";
    public const string OldSuffix = ".old";

    public static void WriteMarker(string directory, PendingUpdate pending)
    {
        Directory.CreateDirectory(directory);
        var marker = new Marker(pending.Version.ToString(), Path.GetFileName(pending.FilePath), pending.Signature);
        var path = Path.Combine(directory, MarkerName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(marker));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    /// <summary>The staged update when it is newer than <paramref name="current"/> and carries a valid signature.</summary>
    public static PendingUpdate? FindPending(string directory, Version current, string publicKey)
    {
        var path = Path.Combine(directory, MarkerName);
        if (!File.Exists(path))
        {
            return null;
        }

        Marker? marker;
        try
        {
            marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }

        if (marker?.File is null || marker.Signature is null || !AppVersion.TryParse(marker.Version, out var version)
            || version <= AppVersion.Normalize(current))
        {
            return null;
        }

        // Only a bare file name inside the updates folder, never a path from the marker.
        var file = Path.Combine(directory, Path.GetFileName(marker.File));
        return File.Exists(file) && UpdateSignature.Verify(File.ReadAllBytes(file), version, marker.Signature, publicKey)
            ? new PendingUpdate(version, file, marker.Signature)
            : null;
    }

    public static void Install(PendingUpdate pending, string exePath, string directory, string publicKey)
    {
        var bytes = File.ReadAllBytes(pending.FilePath);
        if (!UpdateSignature.Verify(bytes, pending.Version, pending.Signature, publicKey))
        {
            throw new UpdateException("Update signature is not valid.");
        }

        var old = exePath + OldSuffix;
        if (File.Exists(old))
        {
            File.Delete(old);
        }

        File.Move(exePath, old);
        try
        {
            File.WriteAllBytes(exePath, bytes);
        }
        catch
        {
            if (File.Exists(exePath))
            {
                File.Delete(exePath);
            }

            File.Move(old, exePath);
            throw;
        }

        // The new exe is in place. A leftover download is harmless: its version is no longer newer, so it is ignored.
        TryDelete(pending.FilePath);
        TryDelete(Path.Combine(directory, MarkerName));
    }

    /// <summary>Deletes NetMan.exe.old. False while the old process still holds it; the next launch tries again.</summary>
    public static bool CleanupOld(string exePath) => TryDelete(exePath + OldSuffix);

    public static void DeleteOtherDownloads(string directory, string keep)
    {
        foreach (var file in Directory.GetFiles(directory, "NetMan-*.exe*"))
        {
            if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(file);
            }
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record Marker(string? Version, string? File, string? Signature);
}
