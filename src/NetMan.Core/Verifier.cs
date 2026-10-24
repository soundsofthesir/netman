using System.Globalization;

namespace NetMan.Core;

public enum CheckKind
{
    Mode,
    Ip,
    Gateway,
    Dns,
    Conflict,
    Lease,
}

/// <summary>Pending means Windows has not finished yet (address detection, DHCP lease); keep polling.</summary>
public enum CheckStatus
{
    Pass,
    Pending,
    Fail,
}

public sealed record CheckResult(CheckKind Kind, CheckStatus Status, string Expected, string Actual);

public sealed record VerifyResult(IReadOnlyList<CheckResult> Checks, bool AdapterConnected)
{
    public CheckStatus Status =>
        Checks.Any(c => c.Status == CheckStatus.Fail) ? CheckStatus.Fail
        : Checks.Any(c => c.Status == CheckStatus.Pending) ? CheckStatus.Pending
        : CheckStatus.Pass;
}

/// <summary>Compares a profile to an adapter's live settings, one check per value.</summary>
public static class Verifier
{
    public const string None = "None";
    public const string InUse = "In use on the network";
    public const string Checking = "Checking";

    /// <param name="clearBlankDns">Expect no DNS servers when both DNS fields are blank. Matches <see cref="NetshBuilder.ForProfile"/>.</param>
    public static VerifyResult Compare(Profile expected, LiveConfig live, bool clearBlankDns = false)
    {
        if (!ProfileValidator.IsValid(expected))
        {
            throw new ArgumentException("Profile is not valid.", nameof(expected));
        }

        var checks = new List<CheckResult> { ModeCheck(expected, live) };
        if (expected.Mode == AddressMode.Static)
        {
            checks.AddRange(StaticChecks(expected, live, clearBlankDns));
        }
        else if (live.IsConnected)
        {
            checks.Add(LeaseCheck(live));
        }

        return new VerifyResult(checks.AsReadOnly(), live.IsConnected);
    }

    /// <summary>True when the adapter is configured as the profile says. Ignores address detection and DHCP lease, which are transient.</summary>
    public static bool Matches(Profile profile, LiveConfig live, bool clearBlankDns = false) =>
        ProfileValidator.IsValid(profile)
        && Compare(profile, live, clearBlankDns).Checks
            .Where(c => c.Kind is not (CheckKind.Conflict or CheckKind.Lease))
            .All(c => c.Status == CheckStatus.Pass);

    private static CheckResult ModeCheck(Profile expected, LiveConfig live)
    {
        var want = expected.Mode == AddressMode.Static ? "Static" : "DHCP";
        var have = live.IsDhcp ? "DHCP" : "Static";
        return new(CheckKind.Mode, want == have ? CheckStatus.Pass : CheckStatus.Fail, want, have);
    }

    private static IEnumerable<CheckResult> StaticChecks(Profile expected, LiveConfig live, bool clearBlankDns)
    {
        Ipv4.TryParse(expected.Ip, out var ip);
        Ipv4.TryParseMask(expected.Mask, out var prefix);

        var match = live.Addresses.FirstOrDefault(a => a.Prefix == prefix && Same(a.Ip, ip));
        yield return new(CheckKind.Ip, match is null ? CheckStatus.Fail : CheckStatus.Pass,
            Cidr(Ipv4.Format(ip), prefix), JoinAddresses(live.Addresses));

        var wantGateways = string.IsNullOrWhiteSpace(expected.Gateway) ? [] : new[] { Canonical(expected.Gateway) };
        yield return ListCheck(CheckKind.Gateway, wantGateways, live.Gateways, ordered: false);

        var wantDns = new[] { expected.Dns1, expected.Dns2 }.Where(d => !string.IsNullOrWhiteSpace(d)).Select(Canonical).ToArray();
        if (wantDns.Length > 0 || clearBlankDns)
        {
            yield return ListCheck(CheckKind.Dns, wantDns, live.DnsServers, ordered: true);
        }

        if (live.IsConnected && match is not null)
        {
            yield return match.State switch
            {
                DadState.Duplicate => new(CheckKind.Conflict, CheckStatus.Fail, None, InUse),
                DadState.Tentative => new(CheckKind.Conflict, CheckStatus.Pending, None, Checking),
                _ => new(CheckKind.Conflict, CheckStatus.Pass, None, None),
            };
        }
    }

    private static CheckResult LeaseCheck(LiveConfig live)
    {
        var lease = live.Addresses.FirstOrDefault(a =>
            a.State == DadState.Preferred && Ipv4.TryParse(a.Ip, out var v) && !Ipv4.IsLinkLocal(v));
        var shown = lease ?? live.Addresses.FirstOrDefault();
        var actual = shown is null ? None : Cidr(shown.Ip, shown.Prefix);
        return new(CheckKind.Lease, lease is null ? CheckStatus.Pending : CheckStatus.Pass, "DHCP lease", actual);
    }

    private static CheckResult ListCheck(CheckKind kind, IReadOnlyList<string> want, IReadOnlyList<string> have, bool ordered)
    {
        var haveCanonical = have.Select(Canonical).ToArray();
        var pass = ordered
            ? want.SequenceEqual(haveCanonical)
            : want.Count == haveCanonical.Length && want.ToHashSet().SetEquals(haveCanonical);
        return new(kind, pass ? CheckStatus.Pass : CheckStatus.Fail, Join(want), Join(have));
    }

    private static bool Same(string text, uint value) => Ipv4.TryParse(text, out var v) && v == value;

    // Unparsable live values are kept as-is so they still show, and never equal a parsed expected value.
    private static string Canonical(string text) => Ipv4.TryParse(text, out var v) ? Ipv4.Format(v) : text.Trim();

    private static string Cidr(string ip, int prefix) => string.Create(CultureInfo.InvariantCulture, $"{ip}/{prefix}");

    private static string JoinAddresses(IReadOnlyList<LiveAddress> addresses) =>
        Join(addresses.Select(a => Cidr(a.Ip, a.Prefix)).ToArray());

    private static string Join(IReadOnlyList<string> values) => values.Count == 0 ? None : string.Join(", ", values);
}
