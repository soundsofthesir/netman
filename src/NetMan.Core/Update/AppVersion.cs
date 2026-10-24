using System.Globalization;

namespace NetMan.Core;

/// <summary>Release versions are three numbers, tagged "v1.2.3". Version 0.0.0 marks a development build, which never updates.</summary>
public static class AppVersion
{
    public static bool TryParse(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        var text = tag?.Trim() ?? "";
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var parts = text.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0 || p.Length > 9 || !p.All(char.IsAsciiDigit)))
        {
            return false;
        }

        version = new Version(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture));
        return true;
    }

    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));

    public static bool IsDevelopment(Version version) => Normalize(version) == new Version(0, 0, 0);
}
