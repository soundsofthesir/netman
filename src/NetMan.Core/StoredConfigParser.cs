namespace NetMan.Core;

/// <summary>
/// Reads an adapter's stored IPv4 settings from its registry values under
/// <c>HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}</c>.
/// Used to verify an apply on a disconnected adapter, where Windows reports no live addresses.
/// </summary>
public static class StoredConfigParser
{
    private static readonly char[] NameServerSeparators = [',', ' '];

    /// <param name="getValue">Returns a registry value by name, or null when it is missing. Matches <c>RegistryKey.GetValue</c>.</param>
    public static LiveConfig Parse(Func<string, object?> getValue)
    {
        var dhcp = getValue("EnableDHCP") switch
        {
            int i => i != 0,
            long l => l != 0,
            _ => true,
        };

        if (dhcp)
        {
            return new LiveConfig(false, true, [], [], []);
        }

        var ips = Strings(getValue("IPAddress"));
        var masks = Strings(getValue("SubnetMask"));
        var addresses = ips.Length == masks.Length
            ? ips.Zip(masks)
                .Where(pair => Ipv4.TryParse(pair.First, out var ip) && ip != 0)
                .Select(pair => Ipv4.TryParseMask(pair.Second, out var prefix) ? new LiveAddress(pair.First.Trim(), prefix, DadState.Preferred) : null)
                .OfType<LiveAddress>()
                .ToList()
            : [];

        var gateways = Strings(getValue("DefaultGateway")).Where(g => Ipv4.TryParse(g, out _)).Select(g => g.Trim()).ToList();

        var dns = (getValue("NameServer") as string ?? "")
            .Split(NameServerSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new LiveConfig(false, false, addresses.AsReadOnly(), gateways.AsReadOnly(), dns.AsReadOnly());
    }

    private static string[] Strings(object? value) => value switch
    {
        string[] many => many,
        string one when one.Length > 0 => [one],
        _ => [],
    };
}
