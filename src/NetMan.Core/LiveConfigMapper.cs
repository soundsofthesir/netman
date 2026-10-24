using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetMan.Core;

/// <summary>Builds a <see cref="LiveConfig"/> from the values Windows reports for a connected adapter. IPv4 only.</summary>
public static class LiveConfigMapper
{
    public static LiveConfig FromInterface(
        bool isConnected,
        bool isDhcp,
        IEnumerable<(IPAddress Address, int Prefix, DuplicateAddressDetectionState State)> addresses,
        IEnumerable<IPAddress> gateways,
        IEnumerable<IPAddress> dnsServers) =>
        new(
            isConnected,
            isDhcp,
            addresses.Where(a => IsV4(a.Address)).Select(a => new LiveAddress(a.Address.ToString(), a.Prefix, Map(a.State))).ToList().AsReadOnly(),
            gateways.Where(g => IsV4(g) && !g.Equals(IPAddress.Any)).Select(g => g.ToString()).ToList().AsReadOnly(),
            dnsServers.Where(IsV4).Select(d => d.ToString()).ToList().AsReadOnly());

    public static DadState Map(DuplicateAddressDetectionState state) => state switch
    {
        DuplicateAddressDetectionState.Preferred => DadState.Preferred,
        DuplicateAddressDetectionState.Tentative => DadState.Tentative,
        DuplicateAddressDetectionState.Duplicate => DadState.Duplicate,
        DuplicateAddressDetectionState.Deprecated => DadState.Deprecated,
        _ => DadState.Invalid,
    };

    private static bool IsV4(IPAddress address) => address.AddressFamily == AddressFamily.InterNetwork;
}
