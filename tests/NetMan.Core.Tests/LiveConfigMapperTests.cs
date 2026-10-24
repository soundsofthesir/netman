using System.Net;
using System.Net.NetworkInformation;

namespace NetMan.Core.Tests;

public class LiveConfigMapperTests
{
    private static (IPAddress, int, DuplicateAddressDetectionState) Addr(string ip, int prefix, DuplicateAddressDetectionState state = DuplicateAddressDetectionState.Preferred) =>
        (IPAddress.Parse(ip), prefix, state);

    [Fact]
    public void Keeps_ipv4_only_and_maps_state()
    {
        var live = LiveConfigMapper.FromInterface(
            isConnected: true,
            isDhcp: false,
            addresses: [Addr("fe80::1", 64), Addr("10.10.1.20", 24, DuplicateAddressDetectionState.Duplicate), Addr("169.254.3.3", 16, DuplicateAddressDetectionState.Tentative)],
            gateways: [IPAddress.Parse("fe80::1"), IPAddress.Parse("10.10.1.1")],
            dnsServers: [IPAddress.Parse("fec0:0:0:ffff::1"), IPAddress.Parse("1.1.1.1")]);

        Assert.True(live.IsConnected);
        Assert.False(live.IsDhcp);
        Assert.Equal([new LiveAddress("10.10.1.20", 24, DadState.Duplicate), new LiveAddress("169.254.3.3", 16, DadState.Tentative)], live.Addresses);
        Assert.Equal(["10.10.1.1"], live.Gateways);
        Assert.Equal(["1.1.1.1"], live.DnsServers);
    }

    [Fact]
    public void Drops_unspecified_gateway()
    {
        var live = LiveConfigMapper.FromInterface(true, true, [], [IPAddress.Any], []);
        Assert.Empty(live.Gateways);
    }

    [Theory]
    [InlineData(DuplicateAddressDetectionState.Preferred, DadState.Preferred)]
    [InlineData(DuplicateAddressDetectionState.Tentative, DadState.Tentative)]
    [InlineData(DuplicateAddressDetectionState.Duplicate, DadState.Duplicate)]
    [InlineData(DuplicateAddressDetectionState.Deprecated, DadState.Deprecated)]
    [InlineData(DuplicateAddressDetectionState.Invalid, DadState.Invalid)]
    public void Maps_every_state(DuplicateAddressDetectionState from, DadState to)
    {
        Assert.Equal(to, LiveConfigMapper.Map(from));
    }
}
