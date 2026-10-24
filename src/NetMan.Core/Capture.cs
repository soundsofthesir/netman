namespace NetMan.Core;

/// <summary>Turns an adapter's live settings into a profile. Used by Capture and by the Undo snapshot.</summary>
public static class Capture
{
    public static Profile ToProfile(LiveConfig live, Guid adapterId, string adapterName, string name)
    {
        var profile = new Profile { Name = name, AdapterId = adapterId, AdapterName = adapterName };
        if (live.IsDhcp)
        {
            return profile with { Mode = AddressMode.Dhcp };
        }

        var address = live.Addresses.FirstOrDefault(a => Ipv4.TryParse(a.Ip, out var v) && !Ipv4.IsLinkLocal(v))
            ?? live.Addresses.FirstOrDefault();

        return profile with
        {
            Mode = AddressMode.Static,
            Ip = address?.Ip ?? "",
            Mask = address is null ? "" : Ipv4.PrefixToMaskString(address.Prefix),
            Gateway = live.Gateways.FirstOrDefault() ?? "",
            Dns1 = live.DnsServers.ElementAtOrDefault(0) ?? "",
            Dns2 = live.DnsServers.ElementAtOrDefault(1) ?? "",
        };
    }
}
