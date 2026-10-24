using System.Globalization;

namespace NetMan.Core;

/// <summary>One line in the result panel. <see cref="ToolTip"/> holds the expected value for a mismatch.</summary>
public sealed record ResultRow(string Label, string Value, CheckStatus Status, string ToolTip);

/// <summary>The strings the window shows, kept here so they are tested.</summary>
public static class DisplayText
{
    public const string Disconnected = "Disconnected";
    public const string NoAddress = "No address";
    public const string Automatic = "Automatic";

    public static string AdapterLine(AdapterInfo adapter, LiveConfig? live)
    {
        if (!adapter.IsConnected)
        {
            return Disconnected;
        }

        var first = live?.Addresses.FirstOrDefault();
        return first is null ? NoAddress : Cidr(first.Ip, first.Prefix);
    }

    public static string ProfileAddress(Profile profile)
    {
        if (profile.Mode == AddressMode.Dhcp)
        {
            return Automatic;
        }

        var ip = profile.Ip.Trim();
        var address = Ipv4.TryParseMask(profile.Mask, out var prefix) ? Cidr(ip, prefix) : ip;
        return string.IsNullOrWhiteSpace(profile.Gateway) ? address : $"{address}  gw {profile.Gateway.Trim()}";
    }

    public static string Outcome(ApplyOutcome outcome) => outcome switch
    {
        ApplyOutcome.Applied => "Applied",
        ApplyOutcome.Saved => "Saved",
        ApplyOutcome.NoReply => "No reply",
        _ => "Failed",
    };

    public static string Time(ApplyReport report) => report.Finished.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static IReadOnlyList<ResultRow> Rows(ApplyReport report)
    {
        var rows = (report.Verify?.Checks ?? [])
            .Where(c => !(c.Kind == CheckKind.Mode && c.Status == CheckStatus.Pass))
            .Select(c => new ResultRow(Label(c.Kind), c.Actual, c.Status, ExpectedTip(c)))
            .ToList();

        if (report.Ping is { } ping)
        {
            var value = ping.AverageMs is { } ms
                ? $"{report.PingTarget}  {ms.ToString(CultureInfo.InvariantCulture)} ms"
                : $"{report.PingTarget}  No reply";
            rows.Add(new ResultRow("Ping", value, ping.Reachable ? CheckStatus.Pass : CheckStatus.Fail, ""));
        }

        return rows.AsReadOnly();
    }

    private static string Label(CheckKind kind) => kind switch
    {
        CheckKind.Mode => "Mode",
        CheckKind.Ip => "IP",
        CheckKind.Gateway => "Gateway",
        CheckKind.Dns => "DNS",
        CheckKind.Conflict => "Conflict",
        _ => "DHCP",
    };

    // Conflict and lease failures already say what is wrong; the others need the expected value.
    private static string ExpectedTip(CheckResult check) =>
        check.Status == CheckStatus.Fail && check.Kind is not (CheckKind.Conflict or CheckKind.Lease)
            ? $"Expected {check.Expected}"
            : "";

    private static string Cidr(string ip, int prefix) => string.Create(CultureInfo.InvariantCulture, $"{ip}/{prefix}");
}
