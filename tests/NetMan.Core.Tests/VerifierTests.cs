namespace NetMan.Core.Tests;

public class VerifierTests
{
    private static Profile Static(string gateway = "", string dns1 = "", string dns2 = "") => new()
    {
        Name = "PLC bench",
        AdapterId = Guid.NewGuid(),
        AdapterName = "Ethernet 2",
        Mode = AddressMode.Static,
        Ip = "10.10.1.20",
        Mask = "255.255.255.0",
        Gateway = gateway,
        Dns1 = dns1,
        Dns2 = dns2,
    };

    private static readonly Profile Dhcp = Static() with { Mode = AddressMode.Dhcp };

    private static LiveConfig Live(
        bool dhcp = false,
        bool connected = true,
        LiveAddress[]? addresses = null,
        string[]? gateways = null,
        string[]? dns = null) =>
        new(connected, dhcp, addresses ?? [new("10.10.1.20", 24, DadState.Preferred)], gateways ?? [], dns ?? []);

    private static CheckResult Check(VerifyResult r, CheckKind kind) => r.Checks.Single(c => c.Kind == kind);

    [Fact]
    public void Matching_static_config_passes()
    {
        var r = Verifier.Compare(Static("10.10.1.1", "1.1.1.1", "8.8.8.8"), Live(gateways: ["10.10.1.1"], dns: ["1.1.1.1", "8.8.8.8"]));

        Assert.Equal(CheckStatus.Pass, r.Status);
        Assert.Equal([CheckKind.Mode, CheckKind.Ip, CheckKind.Gateway, CheckKind.Dns, CheckKind.Conflict], r.Checks.Select(c => c.Kind));
        Assert.True(r.AdapterConnected);
    }

    [Fact]
    public void Ip_check_reports_expected_and_actual()
    {
        var r = Verifier.Compare(Static(), Live(addresses: [new("10.10.1.20", 16, DadState.Preferred)]));
        var ip = Check(r, CheckKind.Ip);

        Assert.Equal(CheckStatus.Fail, ip.Status);
        Assert.Equal("10.10.1.20/24", ip.Expected);
        Assert.Equal("10.10.1.20/16", ip.Actual);
        Assert.Equal(CheckStatus.Fail, r.Status);
    }

    [Fact]
    public void Missing_address_shows_none_and_skips_conflict()
    {
        var r = Verifier.Compare(Static(), Live(addresses: []));

        Assert.Equal("None", Check(r, CheckKind.Ip).Actual);
        Assert.DoesNotContain(r.Checks, c => c.Kind == CheckKind.Conflict);
    }

    [Fact]
    public void Mode_mismatch_fails()
    {
        var mode = Check(Verifier.Compare(Static(), Live(dhcp: true)), CheckKind.Mode);
        Assert.Equal((CheckStatus.Fail, "Static", "DHCP"), (mode.Status, mode.Expected, mode.Actual));
    }

    [Fact]
    public void Blank_gateway_expects_none()
    {
        var gw = Check(Verifier.Compare(Static(), Live(gateways: ["10.10.1.1"])), CheckKind.Gateway);
        Assert.Equal((CheckStatus.Fail, "None", "10.10.1.1"), (gw.Status, gw.Expected, gw.Actual));
    }

    [Fact]
    public void Extra_gateway_fails()
    {
        var gw = Check(Verifier.Compare(Static("10.10.1.1"), Live(gateways: ["10.10.1.1", "10.10.1.254"])), CheckKind.Gateway);
        Assert.Equal(CheckStatus.Fail, gw.Status);
        Assert.Equal("10.10.1.1, 10.10.1.254", gw.Actual);
    }

    [Fact]
    public void Dns_order_matters()
    {
        var dns = Check(Verifier.Compare(Static(dns1: "1.1.1.1", dns2: "8.8.8.8"), Live(dns: ["8.8.8.8", "1.1.1.1"])), CheckKind.Dns);
        Assert.Equal(CheckStatus.Fail, dns.Status);
        Assert.Equal("1.1.1.1, 8.8.8.8", dns.Expected);
    }

    [Fact]
    public void Blank_dns_is_not_checked_by_default()
    {
        var r = Verifier.Compare(Static(), Live(dns: ["9.9.9.9"]));
        Assert.DoesNotContain(r.Checks, c => c.Kind == CheckKind.Dns);
    }

    [Fact]
    public void Blank_dns_expects_none_when_cleared()
    {
        var dns = Check(Verifier.Compare(Static(), Live(dns: ["9.9.9.9"]), clearBlankDns: true), CheckKind.Dns);
        Assert.Equal((CheckStatus.Fail, "None", "9.9.9.9"), (dns.Status, dns.Expected, dns.Actual));
    }

    [Theory]
    [InlineData(DadState.Preferred, CheckStatus.Pass, "None")]
    [InlineData(DadState.Tentative, CheckStatus.Pending, "Checking")]
    [InlineData(DadState.Duplicate, CheckStatus.Fail, "In use on the network")]
    public void Conflict_follows_duplicate_address_detection(DadState state, CheckStatus status, string actual)
    {
        var c = Check(Verifier.Compare(Static(), Live(addresses: [new("10.10.1.20", 24, state)])), CheckKind.Conflict);
        Assert.Equal((status, actual), (c.Status, c.Actual));
    }

    [Fact]
    public void Fail_outranks_pending()
    {
        var r = Verifier.Compare(Static("10.10.1.1"), Live(addresses: [new("10.10.1.20", 24, DadState.Tentative)]));
        Assert.Equal(CheckStatus.Fail, r.Status);
    }

    [Fact]
    public void Pending_when_nothing_failed()
    {
        var r = Verifier.Compare(Static(), Live(addresses: [new("10.10.1.20", 24, DadState.Tentative)]));
        Assert.Equal(CheckStatus.Pending, r.Status);
    }

    [Fact]
    public void Disconnected_adapter_skips_conflict()
    {
        var r = Verifier.Compare(Static(), Live(connected: false));
        Assert.Equal(CheckStatus.Pass, r.Status);
        Assert.False(r.AdapterConnected);
        Assert.DoesNotContain(r.Checks, c => c.Kind == CheckKind.Conflict);
    }

    [Fact]
    public void Dhcp_with_lease_passes()
    {
        var r = Verifier.Compare(Dhcp, Live(dhcp: true, addresses: [new("192.168.1.23", 24, DadState.Preferred)], gateways: ["192.168.1.1"], dns: ["192.168.1.1"]));

        Assert.Equal(CheckStatus.Pass, r.Status);
        Assert.Equal([CheckKind.Mode, CheckKind.Lease], r.Checks.Select(c => c.Kind));
        Assert.Equal("192.168.1.23/24", Check(r, CheckKind.Lease).Actual);
    }

    [Fact]
    public void Dhcp_with_link_local_only_is_pending()
    {
        var lease = Check(Verifier.Compare(Dhcp, Live(dhcp: true, addresses: [new("169.254.12.7", 16, DadState.Preferred)])), CheckKind.Lease);
        Assert.Equal((CheckStatus.Pending, "169.254.12.7/16"), (lease.Status, lease.Actual));
    }

    [Fact]
    public void Dhcp_with_no_address_is_pending()
    {
        var lease = Check(Verifier.Compare(Dhcp, Live(dhcp: true, addresses: [])), CheckKind.Lease);
        Assert.Equal((CheckStatus.Pending, "None"), (lease.Status, lease.Actual));
    }

    [Fact]
    public void Dhcp_disconnected_checks_mode_only()
    {
        var r = Verifier.Compare(Dhcp, Live(dhcp: true, connected: false, addresses: []));
        Assert.Equal([CheckKind.Mode], r.Checks.Select(c => c.Kind));
        Assert.Equal(CheckStatus.Pass, r.Status);
    }

    [Fact]
    public void Matches_ignores_conflict_and_lease()
    {
        Assert.True(Verifier.Matches(Static(), Live(addresses: [new("10.10.1.20", 24, DadState.Duplicate)])));
        Assert.True(Verifier.Matches(Dhcp, Live(dhcp: true, addresses: [])));
        Assert.False(Verifier.Matches(Static(), Live(dhcp: true)));
    }

    [Fact]
    public void Invalid_profile_throws()
    {
        Assert.Throws<ArgumentException>(() => Verifier.Compare(Static() with { Mask = "bad" }, Live()));
    }
}
