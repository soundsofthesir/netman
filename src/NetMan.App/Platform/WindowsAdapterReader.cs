using System.Net.NetworkInformation;
using Microsoft.Win32;
using NetMan.Core;

namespace NetMan.App.Platform;

/// <summary>Reads adapters from Windows. Live settings for connected adapters, stored registry settings otherwise.</summary>
internal sealed class WindowsAdapterReader : IAdapterReader
{
    private const string InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    // The network adapter device class. Each subkey has NetCfgInstanceId (the adapter GUID) and Characteristics.
    private const string NetworkClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    private static readonly HashSet<NetworkInterfaceType> EthernetTypes =
    [
        NetworkInterfaceType.Ethernet,
        NetworkInterfaceType.GigabitEthernet,
        NetworkInterfaceType.FastEthernetT,
        NetworkInterfaceType.FastEthernetFx,
        NetworkInterfaceType.Ethernet3Megabit,
    ];

    public IReadOnlyList<AdapterInfo> List()
    {
        var characteristics = ReadCharacteristics();
        return Interfaces()
            .Where(x => !characteristics.TryGetValue(x.Id, out var flags) || AdapterFilter.IsPhysical(flags))
            .Select(x => new AdapterInfo(
                x.Id,
                x.Nic.Name,
                x.Nic.Description,
                x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? AdapterKind.WiFi : AdapterKind.Ethernet,
                x.Nic.OperationalStatus == OperationalStatus.Up))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    public LiveConfig? Read(Guid adapterId)
    {
        var nic = Interfaces().FirstOrDefault(x => x.Id == adapterId).Nic;
        if (nic is null)
        {
            return null;
        }

        if (nic.OperationalStatus != OperationalStatus.Up)
        {
            return ReadStored(adapterId);
        }

        try
        {
            var props = nic.GetIPProperties();
            return LiveConfigMapper.FromInterface(
                isConnected: true,
                isDhcp: props.GetIPv4Properties().IsDhcpEnabled,
                props.UnicastAddresses.Select(u => (u.Address, u.PrefixLength, u.DuplicateAddressDetectionState)),
                props.GatewayAddresses.Select(g => g.Address),
                props.DnsAddresses);
        }
        catch (NetworkInformationException)
        {
            // IPv4 is not bound to this adapter. The stored settings are the best available answer.
            return ReadStored(adapterId);
        }
    }

    private static LiveConfig ReadStored(Guid adapterId)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{InterfacesKey}\{adapterId:B}");
        return StoredConfigParser.Parse(name => key?.GetValue(name));
    }

    private static IEnumerable<(Guid Id, NetworkInterface Nic)> Interfaces() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => EthernetTypes.Contains(n.NetworkInterfaceType) || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .Select(n => (Ok: Guid.TryParse(n.Id, out var id), Id: id, Nic: n))
            .Where(x => x.Ok)
            .Select(x => (x.Id, x.Nic));

    private static Dictionary<Guid, int> ReadCharacteristics()
    {
        var result = new Dictionary<Guid, int>();
        using var root = Registry.LocalMachine.OpenSubKey(NetworkClassKey);
        if (root is null)
        {
            return result;
        }

        foreach (var name in root.GetSubKeyNames())
        {
            try
            {
                using var sub = root.OpenSubKey(name);
                if (sub?.GetValue("NetCfgInstanceId") is string idText
                    && Guid.TryParse(idText, out var id)
                    && sub.GetValue("Characteristics") is int flags)
                {
                    result[id] = flags;
                }
            }
            catch (System.Security.SecurityException)
            {
                // The "Properties" subkey is not readable even by administrators. Skip it.
            }
        }

        return result;
    }
}
