namespace NetMan.Core;

/// <summary>One netsh invocation. Arguments go to the process as a list, never through a shell.</summary>
public sealed record NetshCommand(IReadOnlyList<string> Arguments);

public static class NetshBuilder
{
    /// <param name="adapterName">The adapter's current name, resolved from its id at apply time.</param>
    /// <param name="clearBlankDns">Clear DNS when both DNS fields are blank. Used by Undo; a normal apply leaves DNS alone.</param>
    public static IReadOnlyList<NetshCommand> ForProfile(Profile profile, string adapterName, bool clearBlankDns = false)
    {
        CheckAdapterName(adapterName);
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            throw new ArgumentException(
                "Invalid profile: " + string.Join("; ", errors.Select(e => $"{e.Field} {e.Message}")),
                nameof(profile));
        }

        if (profile.Mode == AddressMode.Dhcp)
        {
            return Dhcp(adapterName);
        }

        var name = "name=" + adapterName;
        Ipv4.TryParseMask(profile.Mask, out var prefix);
        var gateway = string.IsNullOrWhiteSpace(profile.Gateway) ? "none" : Normalize(profile.Gateway);

        var commands = new List<NetshCommand>
        {
            new(["interface", "ipv4", "set", "address", name, "source=static",
                "address=" + Normalize(profile.Ip), "mask=" + Ipv4.PrefixToMaskString(prefix), "gateway=" + gateway]),
        };

        if (!string.IsNullOrWhiteSpace(profile.Dns1))
        {
            commands.Add(new(["interface", "ipv4", "set", "dnsservers", name, "source=static",
                "address=" + Normalize(profile.Dns1), "register=primary", "validate=no"]));

            if (!string.IsNullOrWhiteSpace(profile.Dns2))
            {
                commands.Add(new(["interface", "ipv4", "add", "dnsservers", name,
                    "address=" + Normalize(profile.Dns2), "index=2", "validate=no"]));
            }
        }
        else if (clearBlankDns)
        {
            commands.Add(new(["interface", "ipv4", "set", "dnsservers", name, "source=static", "address=none", "validate=no"]));
        }

        return commands.AsReadOnly();
    }

    /// <summary>Address and DNS back to automatic.</summary>
    public static IReadOnlyList<NetshCommand> ResetToDhcp(string adapterName)
    {
        CheckAdapterName(adapterName);
        return Dhcp(adapterName);
    }

    private static IReadOnlyList<NetshCommand> Dhcp(string adapterName) =>
    [
        new(["interface", "ipv4", "set", "address", "name=" + adapterName, "source=dhcp"]),
        new(["interface", "ipv4", "set", "dnsservers", "name=" + adapterName, "source=dhcp"]),
    ];

    private static string Normalize(string address)
    {
        Ipv4.TryParse(address, out var value);
        return Ipv4.Format(value);
    }

    private static void CheckAdapterName(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName) || adapterName.Any(c => c == '"' || char.IsControl(c)))
        {
            throw new ArgumentException("Adapter name is empty or contains quotes or control characters.", nameof(adapterName));
        }
    }
}
