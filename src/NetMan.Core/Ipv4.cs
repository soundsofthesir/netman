using System.Globalization;
using System.Numerics;

namespace NetMan.Core;

/// <summary>Strict IPv4 parsing and subnet arithmetic on host-order <see cref="uint"/> values.</summary>
public static class Ipv4
{
    public const int MinPrefix = 1;
    public const int MaxPrefix = 32;

    /// <summary>Parses a dotted quad. Rejects leading zeros, signs, whitespace inside the value and any suffix.</summary>
    public static bool TryParse(string? text, out uint value)
    {
        value = 0;
        if (text is null)
        {
            return false;
        }

        var parts = text.Trim().Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!TryParseOctet(part, out var octet))
            {
                value = 0;
                return false;
            }

            value = (value << 8) | octet;
        }

        return true;
    }

    public static string Format(uint value) =>
        string.Create(CultureInfo.InvariantCulture, $"{value >> 24}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}");

    /// <summary>Parses "255.255.255.0", "/24" or "24" into a prefix length from 1 to 32.</summary>
    public static bool TryParseMask(string? text, out int prefix)
    {
        prefix = 0;
        if (text is null)
        {
            return false;
        }

        var trimmed = text.Trim();
        var digits = trimmed.StartsWith('/') ? trimmed[1..] : trimmed;
        if (digits.Length is 1 or 2 && digits.All(char.IsAsciiDigit))
        {
            var n = int.Parse(digits, CultureInfo.InvariantCulture);
            if (n is < MinPrefix or > MaxPrefix)
            {
                return false;
            }

            prefix = n;
            return true;
        }

        if (trimmed.StartsWith('/') || !TryParse(trimmed, out var mask) || mask == 0)
        {
            return false;
        }

        var inverted = ~mask;
        if ((inverted & (inverted + 1)) != 0)
        {
            return false;
        }

        prefix = BitOperations.PopCount(mask);
        return true;
    }

    public static uint PrefixToMask(int prefix) => prefix <= 0 ? 0 : uint.MaxValue << (32 - prefix);

    public static string PrefixToMaskString(int prefix) => Format(PrefixToMask(prefix));

    public static uint Network(uint ip, int prefix) => ip & PrefixToMask(prefix);

    public static uint Broadcast(uint ip, int prefix) => ip | ~PrefixToMask(prefix);

    public static bool SameSubnet(uint a, uint b, int prefix) => Network(a, prefix) == Network(b, prefix);

    public static bool IsLinkLocal(uint ip) => (ip & 0xFFFF0000) == 0xA9FE0000;

    /// <summary>0.x, 127.x, multicast and above. Never valid as a host, gateway or DNS address.</summary>
    public static bool IsReserved(uint ip)
    {
        var first = ip >> 24;
        return first == 0 || first == 127 || first >= 224;
    }

    private static bool TryParseOctet(string part, out uint octet)
    {
        octet = 0;
        if (part.Length is 0 or > 3 || !part.All(char.IsAsciiDigit) || (part.Length > 1 && part[0] == '0'))
        {
            return false;
        }

        octet = uint.Parse(part, CultureInfo.InvariantCulture);
        return octet <= 255;
    }
}
