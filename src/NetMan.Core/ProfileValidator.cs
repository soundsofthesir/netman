namespace NetMan.Core;

public enum ProfileField
{
    Name,
    Adapter,
    Ip,
    Mask,
    Gateway,
    Dns1,
    Dns2,
    PingTarget,
}

public sealed record ValidationError(ProfileField Field, string Message);

public static class ProfileValidator
{
    public const string Required = "Required";
    public const string NotAnAddress = "Not a valid IPv4 address";
    public const string NotAMask = "Not a valid mask";
    public const string Reserved = "Reserved address";
    public const string NetworkAddress = "Network address";
    public const string BroadcastAddress = "Broadcast address";
    public const string OutsideSubnet = "Outside subnet";
    public const string SameAsIp = "Same as IP";

    /// <summary>Highest prefix that still has distinct network and broadcast addresses.</summary>
    private const int MaxPrefixWithBroadcast = 30;

    public static IReadOnlyList<ValidationError> Validate(Profile profile)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add(new(ProfileField.Name, Required));
        }

        if (profile.AdapterId == Guid.Empty)
        {
            errors.Add(new(ProfileField.Adapter, Required));
        }

        if (profile.Mode == AddressMode.Static)
        {
            errors.AddRange(ValidateStatic(profile));
        }

        if (!string.IsNullOrWhiteSpace(profile.PingTarget) && !Ipv4.TryParse(profile.PingTarget, out _))
        {
            errors.Add(new(ProfileField.PingTarget, NotAnAddress));
        }

        return errors.AsReadOnly();
    }

    public static bool IsValid(Profile profile) => Validate(profile).Count == 0;

    private static IEnumerable<ValidationError> ValidateStatic(Profile p)
    {
        var maskOk = Ipv4.TryParseMask(p.Mask, out var prefix);
        if (string.IsNullOrWhiteSpace(p.Mask))
        {
            yield return new(ProfileField.Mask, Required);
        }
        else if (!maskOk)
        {
            yield return new(ProfileField.Mask, NotAMask);
        }

        var ipError = HostError(p.Ip, required: true, maskOk, prefix);
        if (ipError is not null)
        {
            yield return new(ProfileField.Ip, ipError);
        }

        var subnetKnown = ipError is null && maskOk;
        Ipv4.TryParse(p.Ip, out var ip);

        if (!string.IsNullOrWhiteSpace(p.Gateway))
        {
            var gwError = GatewayError(p.Gateway, subnetKnown, ip, prefix);
            if (gwError is not null)
            {
                yield return new(ProfileField.Gateway, gwError);
            }
        }

        var dns1Blank = string.IsNullOrWhiteSpace(p.Dns1);
        var dns2Blank = string.IsNullOrWhiteSpace(p.Dns2);
        if (dns1Blank && !dns2Blank)
        {
            yield return new(ProfileField.Dns1, Required);
        }
        else if (!dns1Blank && AddressError(p.Dns1) is { } dns1Error)
        {
            yield return new(ProfileField.Dns1, dns1Error);
        }

        if (!dns2Blank && AddressError(p.Dns2) is { } dns2Error)
        {
            yield return new(ProfileField.Dns2, dns2Error);
        }
    }

    private static string? HostError(string text, bool required, bool maskOk, int prefix)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return required ? Required : null;
        }

        if (AddressError(text) is { } error)
        {
            return error;
        }

        Ipv4.TryParse(text, out var ip);
        if (maskOk && prefix <= MaxPrefixWithBroadcast)
        {
            if (ip == Ipv4.Network(ip, prefix))
            {
                return NetworkAddress;
            }

            if (ip == Ipv4.Broadcast(ip, prefix))
            {
                return BroadcastAddress;
            }
        }

        return null;
    }

    private static string? GatewayError(string text, bool subnetKnown, uint ip, int prefix)
    {
        if (AddressError(text) is { } error)
        {
            return error;
        }

        if (!subnetKnown)
        {
            return null;
        }

        Ipv4.TryParse(text, out var gw);
        if (gw == ip)
        {
            return SameAsIp;
        }

        if (!Ipv4.SameSubnet(gw, ip, prefix))
        {
            return OutsideSubnet;
        }

        return HostError(text, required: false, maskOk: true, prefix);
    }

    private static string? AddressError(string text)
    {
        if (!Ipv4.TryParse(text, out var value))
        {
            return NotAnAddress;
        }

        return Ipv4.IsReserved(value) ? Reserved : null;
    }
}
