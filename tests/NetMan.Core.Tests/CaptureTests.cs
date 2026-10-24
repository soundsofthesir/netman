namespace NetMan.Core.Tests;

public class CaptureTests
{
    private static readonly Guid Adapter = Guid.NewGuid();

    [Fact]
    public void Static_config_becomes_static_profile()
    {
        var live = new LiveConfig(true, false,
            [new("169.254.3.3", 16, DadState.Preferred), new("10.10.1.20", 24, DadState.Preferred), new("10.10.1.21", 24, DadState.Preferred)],
            ["10.10.1.1"],
            ["1.1.1.1", "8.8.8.8", "9.9.9.9"]);

        var p = Capture.ToProfile(live, Adapter, "Ethernet 2", "Site A");

        Assert.Equal("Site A", p.Name);
        Assert.Equal(Adapter, p.AdapterId);
        Assert.Equal("Ethernet 2", p.AdapterName);
        Assert.Equal(AddressMode.Static, p.Mode);
        Assert.Equal("10.10.1.20", p.Ip);
        Assert.Equal("255.255.255.0", p.Mask);
        Assert.Equal("10.10.1.1", p.Gateway);
        Assert.Equal("1.1.1.1", p.Dns1);
        Assert.Equal("8.8.8.8", p.Dns2);
    }

    [Fact]
    public void Captured_profile_matches_the_config_it_came_from()
    {
        var live = new LiveConfig(true, false, [new("10.10.1.20", 24, DadState.Preferred)], ["10.10.1.1"], ["1.1.1.1", "8.8.8.8"]);
        Assert.True(Verifier.Matches(Capture.ToProfile(live, Adapter, "Ethernet 2", "x"), live));
    }

    [Fact]
    public void Link_local_is_used_when_it_is_the_only_address()
    {
        var live = new LiveConfig(true, false, [new("169.254.3.3", 16, DadState.Preferred)], [], []);
        var p = Capture.ToProfile(live, Adapter, "Ethernet 2", "x");
        Assert.Equal(("169.254.3.3", "255.255.0.0", "", "", ""), (p.Ip, p.Mask, p.Gateway, p.Dns1, p.Dns2));
    }

    [Fact]
    public void Dhcp_config_becomes_dhcp_profile()
    {
        var live = new LiveConfig(true, true, [new("192.168.1.23", 24, DadState.Preferred)], ["192.168.1.1"], ["192.168.1.1"]);
        var p = Capture.ToProfile(live, Adapter, "Wi-Fi", "Home");
        Assert.Equal((AddressMode.Dhcp, "", "", ""), (p.Mode, p.Ip, p.Gateway, p.Dns1));
    }

    [Fact]
    public void Static_with_no_address_leaves_fields_blank()
    {
        var p = Capture.ToProfile(new LiveConfig(false, false, [], [], []), Adapter, "Ethernet", "x");
        Assert.Equal((AddressMode.Static, "", ""), (p.Mode, p.Ip, p.Mask));
    }
}
