namespace NetMan.Core;

/// <summary>Windows duplicate address detection state for an address.</summary>
public enum DadState
{
    Preferred,
    Tentative,
    Duplicate,
    Deprecated,
    Invalid,
}

public sealed record LiveAddress(string Ip, int Prefix, DadState State);

/// <summary>
/// An adapter's IPv4 settings as Windows reports them. For a disconnected adapter these come from the stored
/// configuration, and address states are reported as <see cref="DadState.Preferred"/>.
/// </summary>
public sealed record LiveConfig(
    bool IsConnected,
    bool IsDhcp,
    IReadOnlyList<LiveAddress> Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers);
