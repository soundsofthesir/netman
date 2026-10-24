namespace NetMan.Core.Tests;

public class Ipv4Tests
{
    [Theory]
    [InlineData("192.168.0.50", 0xC0A80032u)]
    [InlineData("0.0.0.0", 0u)]
    [InlineData("255.255.255.255", 0xFFFFFFFFu)]
    [InlineData(" 10.0.0.1 ", 0x0A000001u)]
    public void TryParse_accepts_dotted_quads(string text, uint expected)
    {
        Assert.True(Ipv4.TryParse(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.4.5")]
    [InlineData("256.1.1.1")]
    [InlineData("1.2.3.-4")]
    [InlineData("1.2.3.04")]
    [InlineData("1..3.4")]
    [InlineData("a.b.c.d")]
    [InlineData("1.2.3.4/24")]
    [InlineData("1.2.3.+4")]
    public void TryParse_rejects_anything_else(string? text)
    {
        Assert.False(Ipv4.TryParse(text, out _));
    }

    [Fact]
    public void Format_round_trips()
    {
        Assert.Equal("172.16.40.5", Ipv4.Format(0xAC102805u));
    }

    [Theory]
    [InlineData("255.255.255.0", 24)]
    [InlineData("255.255.255.255", 32)]
    [InlineData("128.0.0.0", 1)]
    [InlineData("255.255.252.0", 22)]
    [InlineData("/24", 24)]
    [InlineData("24", 24)]
    [InlineData(" /16 ", 16)]
    [InlineData("32", 32)]
    public void TryParseMask_accepts_dotted_and_prefix(string text, int expected)
    {
        Assert.True(Ipv4.TryParseMask(text, out var prefix));
        Assert.Equal(expected, prefix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.0.0.0")]
    [InlineData("/0")]
    [InlineData("0")]
    [InlineData("33")]
    [InlineData("/33")]
    [InlineData("255.0.255.0")]
    [InlineData("255.255.255.1")]
    [InlineData("/2a")]
    [InlineData("-8")]
    public void TryParseMask_rejects_invalid_masks(string? text)
    {
        Assert.False(Ipv4.TryParseMask(text, out _));
    }

    [Theory]
    [InlineData(24, "255.255.255.0")]
    [InlineData(32, "255.255.255.255")]
    [InlineData(1, "128.0.0.0")]
    [InlineData(20, "255.255.240.0")]
    public void PrefixToMaskString(int prefix, string expected)
    {
        Assert.Equal(expected, Ipv4.PrefixToMaskString(prefix));
    }

    [Fact]
    public void Network_and_broadcast()
    {
        Ipv4.TryParse("10.10.1.20", out var ip);
        Assert.Equal("10.10.1.0", Ipv4.Format(Ipv4.Network(ip, 24)));
        Assert.Equal("10.10.1.255", Ipv4.Format(Ipv4.Broadcast(ip, 24)));
        Assert.Equal("10.10.1.20", Ipv4.Format(Ipv4.Network(ip, 32)));
    }

    [Fact]
    public void SameSubnet()
    {
        Ipv4.TryParse("10.10.1.20", out var a);
        Ipv4.TryParse("10.10.1.1", out var b);
        Ipv4.TryParse("10.10.2.1", out var c);
        Assert.True(Ipv4.SameSubnet(a, b, 24));
        Assert.False(Ipv4.SameSubnet(a, c, 24));
        Assert.True(Ipv4.SameSubnet(a, c, 16));
    }

    [Theory]
    [InlineData("169.254.12.7", true)]
    [InlineData("169.255.0.1", false)]
    [InlineData("192.168.0.1", false)]
    public void IsLinkLocal(string text, bool expected)
    {
        Ipv4.TryParse(text, out var ip);
        Assert.Equal(expected, Ipv4.IsLinkLocal(ip));
    }
}
