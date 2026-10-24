namespace NetMan.Core.Tests;

public class ProfileValidatorTests
{
    private static Profile Static(string ip = "10.10.1.20", string mask = "/24", string gateway = "") => new()
    {
        Name = "PLC bench",
        AdapterId = Guid.NewGuid(),
        AdapterName = "Ethernet 2",
        Mode = AddressMode.Static,
        Ip = ip,
        Mask = mask,
        Gateway = gateway,
    };

    private static ValidationError? ErrorFor(Profile p, ProfileField field) =>
        ProfileValidator.Validate(p).SingleOrDefault(e => e.Field == field);

    [Fact]
    public void Valid_static_profile_has_no_errors()
    {
        Assert.Empty(ProfileValidator.Validate(Static(gateway: "10.10.1.1") with { Dns1 = "1.1.1.1", Dns2 = "8.8.8.8", PingTarget = "10.10.1.10" }));
    }

    [Fact]
    public void Dhcp_profile_ignores_address_fields()
    {
        var p = Static(ip: "junk", mask: "junk", gateway: "junk") with { Mode = AddressMode.Dhcp, Dns1 = "junk" };
        Assert.Empty(ProfileValidator.Validate(p));
    }

    [Fact]
    public void Dhcp_profile_still_checks_ping_target()
    {
        var p = Static() with { Mode = AddressMode.Dhcp, PingTarget = "nope" };
        Assert.Equal("Not a valid IPv4 address", ErrorFor(p, ProfileField.PingTarget)?.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string name)
    {
        Assert.Equal("Required", ErrorFor(Static() with { Name = name }, ProfileField.Name)?.Message);
    }

    [Fact]
    public void Adapter_is_required()
    {
        Assert.Equal("Required", ErrorFor(Static() with { AdapterId = Guid.Empty }, ProfileField.Adapter)?.Message);
    }

    [Theory]
    [InlineData("", "Required")]
    [InlineData("10.10.1", "Not a valid IPv4 address")]
    [InlineData("10.10.1.0", "Network address")]
    [InlineData("10.10.1.255", "Broadcast address")]
    [InlineData("127.0.0.1", "Reserved address")]
    [InlineData("0.0.0.0", "Reserved address")]
    [InlineData("224.0.0.5", "Reserved address")]
    [InlineData("255.255.255.255", "Reserved address")]
    public void Ip_errors(string ip, string message)
    {
        Assert.Equal(message, ErrorFor(Static(ip: ip), ProfileField.Ip)?.Message);
    }

    [Fact]
    public void Link_local_ip_is_allowed()
    {
        Assert.Empty(ProfileValidator.Validate(Static(ip: "169.254.1.10", mask: "/16")));
    }

    [Theory]
    [InlineData("10.10.1.0", 31)]
    [InlineData("10.10.1.1", 31)]
    [InlineData("10.10.1.0", 32)]
    public void Point_to_point_prefixes_have_no_network_or_broadcast(string ip, int prefix)
    {
        Assert.Null(ErrorFor(Static(ip: ip, mask: prefix.ToString(System.Globalization.CultureInfo.InvariantCulture)), ProfileField.Ip));
    }

    [Theory]
    [InlineData("", "Required")]
    [InlineData("255.0.255.0", "Not a valid mask")]
    public void Mask_errors(string mask, string message)
    {
        Assert.Equal(message, ErrorFor(Static(mask: mask), ProfileField.Mask)?.Message);
    }

    [Theory]
    [InlineData("10.10.2.1", "Outside subnet")]
    [InlineData("10.10.1.20", "Same as IP")]
    [InlineData("10.10.1.0", "Network address")]
    [InlineData("10.10.1.255", "Broadcast address")]
    [InlineData("x", "Not a valid IPv4 address")]
    public void Gateway_errors(string gateway, string message)
    {
        Assert.Equal(message, ErrorFor(Static(gateway: gateway), ProfileField.Gateway)?.Message);
    }

    [Fact]
    public void Gateway_is_not_checked_against_subnet_when_ip_or_mask_is_invalid()
    {
        var errors = ProfileValidator.Validate(Static(mask: "bad", gateway: "10.10.2.1"));
        Assert.DoesNotContain(errors, e => e.Field == ProfileField.Gateway);
    }

    [Fact]
    public void Dns_values_must_be_valid()
    {
        var p = Static() with { Dns1 = "1.1.1", Dns2 = "x" };
        Assert.Equal("Not a valid IPv4 address", ErrorFor(p, ProfileField.Dns1)?.Message);
        Assert.Equal("Not a valid IPv4 address", ErrorFor(p, ProfileField.Dns2)?.Message);
    }

    [Fact]
    public void Dns2_without_dns1_is_an_error_on_dns1()
    {
        Assert.Equal("Required", ErrorFor(Static() with { Dns2 = "8.8.8.8" }, ProfileField.Dns1)?.Message);
    }
}
