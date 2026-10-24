using System.ComponentModel;
using System.Diagnostics;
using NetMan.Core;

namespace NetMan.App.Platform;

/// <summary>Background update check, and the install that runs at the next launch or when the user asks.</summary>
internal sealed class Updater
{
    public static readonly UpdateSource Source = new("soundsofthesir", "netman");

    // Public half of the release signing key. The private half is the NETMAN_SIGNING_KEY Actions secret.
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAECrfIVuDirm6CZvlKF8/bcHNjTrybYXajBh4ve6VFC2F5HdkBR8g1EuoCUEf6Xv2jpLda92wnc3ocxiFK0Uf0yg==";

    private const string ExeName = "NetMan.exe";
    private const int CleanupAttempts = 10;
    private static readonly TimeSpan CleanupDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromMinutes(10);

    private readonly UpdateService _service;
    private readonly ApplyLog _log;

    private Updater(UpdateService service, string exePath, Version current, ApplyLog log)
    {
        _service = service;
        ExePath = exePath;
        Current = current;
        _log = log;
    }

    public string ExePath { get; }

    public Version Current { get; }

    /// <summary>Null for development builds and when not running as the published NetMan.exe, so those never update.</summary>
    public static Updater? Create(ApplyLog log)
    {
        var exe = Environment.ProcessPath;
        var version = typeof(Updater).Assembly.GetName().Version;
        if (exe is null || version is null || AppVersion.IsDevelopment(version)
            || !string.Equals(Path.GetFileName(exe), ExeName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var http = new HttpClient { Timeout = HttpTimeout };
        return new Updater(new UpdateService(http, Source, AppPaths.Updates, PublicKey), exe, AppVersion.Normalize(version), log);
    }

    /// <summary>Runs before the window opens. True when the new version has started and this process should exit.</summary>
    public bool InstallPendingAndRestart()
    {
        RemoveOldExe();
        var pending = UpdateInstaller.FindPending(AppPaths.Updates, Current, PublicKey);
        if (pending is null)
        {
            return false;
        }

        try
        {
            InstallAndRestart(pending);
            return true;
        }
        catch (Exception ex) when (IsInstallError(ex))
        {
            _log.Write($"Update {pending.Version} not installed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Swaps the exe and starts the new version. The caller closes this process.</summary>
    public void InstallAndRestart(PendingUpdate pending)
    {
        UpdateInstaller.Install(pending, ExePath, AppPaths.Updates, PublicKey);
        _log.Write($"Installed update {pending.Version}");

        // Started from this elevated process, the new one inherits elevation without a second UAC prompt.
        using var _ = Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = false });
    }

    public async Task<PendingUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pending = await _service.CheckAsync(Current, cancellationToken).ConfigureAwait(false);
            if (pending is not null)
            {
                _log.Write($"Update {pending.Version} downloaded");
            }

            return pending;
        }
        catch (Exception ex) when (ex is UpdateException or IOException or UnauthorizedAccessException)
        {
            _log.Write($"Update check failed: {ex.Message}");
            return null;
        }
    }

    public static bool IsInstallError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or UpdateException or Win32Exception;

    // Right after an update the previous process may still be exiting and holding the old file.
    private void RemoveOldExe()
    {
        for (var i = 0; i < CleanupAttempts; i++)
        {
            if (UpdateInstaller.CleanupOld(ExePath))
            {
                return;
            }

            Thread.Sleep(CleanupDelay);
        }

        _log.Write($"Could not delete {Path.GetFileName(ExePath)}{UpdateInstaller.OldSuffix}");
    }
}
