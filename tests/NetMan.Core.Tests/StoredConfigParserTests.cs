namespace NetMan.Core.Tests;

public class StoredConfigParserTests
{
    private static Func<string, object?> Values(Dictionary<string, object> values) =>
        name => values.TryGetValue(name, out var v) ? v : null;

    [Fact]
    public void Static_values_become_a_disconnected_config()
    {
        var live = StoredConfigParser.Parse(Values(new()
        {
            ["EnableDHCP"] = 0,
            ["IPAddress"] = new[] { "10.10.1.20", "10.10.1.21" },
            ["SubnetMask"] = new[] { "255.255.255.0", "255.255.0.0" },
            ["DefaultGateway"] = new[] { "10.10.1.1" },
            ["NameServer"] = "1.1.1.1,8.8.8.8",
        }));

        Assert.False(live.IsConnected);
        Assert.False(live.IsDhcp);
        Assert.Equal([new LiveAddress("10.10.1.20", 24, DadState.Preferred), new LiveAddress("10.10.1.21", 16, DadState.Preferred)], live.Addresses);
        Assert.Equal(["10.10.1.1"], live.Gateways);
        Assert.Equal(["1.1.1.1", "8.8.8.8"], live.DnsServers);
    }

    [Fact]
    public void Space_separated_name_servers_and_blank_entries()
    {
        var live = StoredConfigParser.Parse(Values(new()
        {
            ["EnableDHCP"] = 0,
            ["IPAddress"] = new[] { "0.0.0.0", "10.10.1.20" },
            ["SubnetMask"] = new[] { "0.0.0.0", "255.255.255.0" },
            ["DefaultGateway"] = new[] { "" },
            ["NameServer"] = " 1.1.1.1  8.8.8.8 ",
        }));

        Assert.Equal([new LiveAddress("10.10.1.20", 24, DadState.Preferred)], live.Addresses);
        Assert.Empty(live.Gateways);
        Assert.Equal(["1.1.1.1", "8.8.8.8"], live.DnsServers);
    }

    [Fact]
    public void Mismatched_or_bad_masks_are_skipped()
    {
        var live = StoredConfigParser.Parse(Values(new()
        {
            ["EnableDHCP"] = 0,
            ["IPAddress"] = new[] { "10.10.1.20", "10.10.1.21" },
            ["SubnetMask"] = new[] { "255.0.255.0" },
        }));

        Assert.Empty(live.Addresses);
    }

    [Fact]
    public void Dhcp_reports_mode_only()
    {
        var live = StoredConfigParser.Parse(Values(new()
        {
            ["EnableDHCP"] = 1,
            ["IPAddress"] = new[] { "10.10.1.20" },
            ["SubnetMask"] = new[] { "255.255.255.0" },
            ["DhcpIPAddress"] = "192.168.1.23",
        }));

        Assert.True(live.IsDhcp);
        Assert.False(live.IsConnected);
        Assert.Empty(live.Addresses);
        Assert.Empty(live.Gateways);
        Assert.Empty(live.DnsServers);
    }

    [Fact]
    public void Missing_values_mean_dhcp_with_nothing_stored()
    {
        var live = StoredConfigParser.Parse(_ => null);
        Assert.True(live.IsDhcp);
        Assert.Empty(live.Addresses);
    }
}
