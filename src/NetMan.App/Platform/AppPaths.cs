using NetMan.Core;

namespace NetMan.App.Platform;

internal static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetMan");

    public static string Profiles => Path.Combine(DataDirectory, "profiles.json");

    public static string Log => Path.Combine(DataDirectory, "log.txt");

    // Downloads go under LocalAppData so they never roam with the user profile.
    public static string Updates { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetMan", "updates");

    public static readonly TimeSpan NetshTimeout = TimeSpan.FromSeconds(30);

    // Full path so a netsh.exe elsewhere on PATH or in the working directory is never picked up.
    public static ICommandRunner CreateNetsh() =>
        new ProcessCommandRunner(Path.Combine(Environment.SystemDirectory, "netsh.exe"), NetshTimeout);
}
