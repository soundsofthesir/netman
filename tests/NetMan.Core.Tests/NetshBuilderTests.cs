namespace NetMan.Core.Tests;

public class NetshBuilderTests
{
    private static Profile Static(string gateway = "", string dns1 = "", string dns2 = "") => new()
    {
        Name = "PLC bench",
        AdapterId = Guid.NewGuid(),
        AdapterName = "old name",
        Mode = AddressMode.Static,
        Ip = " 10.10.1.20 ",
        Mask = "/24",
        Gateway = gateway,
        Dns1 = dns1,
        Dns2 = dns2,
    };

    private static string[][] Args(IReadOnlyList<NetshCommand> commands) =>
        commands.Select(c => c.Arguments.ToArray()).ToArray();

    [Fact]
    public void Static_without_gateway_or_dns_sets_address_only()
    {
        var commands = NetshBuilder.ForProfile(Static(), "Ethernet 2");

        Assert.Equal(
            [["interface", "ipv4", "set", "address", "name=Ethernet 2", "source=static", "address=10.10.1.20", "mask=255.255.255.0", "gateway=none"]],
            Args(commands));
    }

    [Fact]
    public void Static_with_gateway_and_two_dns()
    {
        var commands = NetshBuilder.ForProfile(Static("10.10.1.1", "1.1.1.1", "8.8.8.8"), "Ethernet 2");

        Assert.Equal(
            [
                ["interface", "ipv4", "set", "address", "name=Ethernet 2", "source=static", "address=10.10.1.20", "mask=255.255.255.0", "gateway=10.10.1.1"],
                ["interface", "ipv4", "set", "dnsservers", "name=Ethernet 2", "source=static", "address=1.1.1.1", "register=primary", "validate=no"],
                ["interface", "ipv4", "add", "dnsservers", "name=Ethernet 2", "address=8.8.8.8", "index=2", "validate=no"],
            ],
            Args(commands));
    }

    [Fact]
    public void Static_with_blank_dns_clears_dns_when_asked()
    {
        var commands = NetshBuilder.ForProfile(Static(), "Ethernet 2", clearBlankDns: true);

        Assert.Equal(
            ["interface", "ipv4", "set", "dnsservers", "name=Ethernet 2", "source=static", "address=none", "validate=no"],
            commands[^1].Arguments);
    }

    [Fact]
    public void Dhcp_sets_address_and_dns_to_dhcp()
    {
        var commands = NetshBuilder.ForProfile(Static() with { Mode = AddressMode.Dhcp }, "Wi-Fi");

        Assert.Equal(
            [
                ["interface", "ipv4", "set", "address", "name=Wi-Fi", "source=dhcp"],
                ["interface", "ipv4", "set", "dnsservers", "name=Wi-Fi", "source=dhcp"],
            ],
            Args(commands));
    }

    [Fact]
    public void ResetToDhcp_matches_a_dhcp_profile()
    {
        Assert.Equal(
            Args(NetshBuilder.ForProfile(Static() with { Mode = AddressMode.Dhcp }, "Ethernet")),
            Args(NetshBuilder.ResetToDhcp("Ethernet")));
    }

    [Fact]
    public void Invalid_profile_throws()
    {
        Assert.Throws<ArgumentException>(() => NetshBuilder.ForProfile(Static() with { Ip = "nope" }, "Ethernet 2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Eth\"ernet")]
    [InlineData("Eth\nernet")]
    public void Bad_adapter_names_throw(string name)
    {
        Assert.Throws<ArgumentException>(() => NetshBuilder.ResetToDhcp(name));
    }
}
