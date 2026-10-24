namespace NetMan.Core.Tests;

public class AdapterFilterTests
{
    [Theory]
    [InlineData(0x84, true)]   // Typical PCI or USB NIC: physical plus has-UI flags.
    [InlineData(0x4, true)]
    [InlineData(0x1, false)]   // Hyper-V and VPN virtual adapters.
    [InlineData(0x0, false)]
    [InlineData(0xC, false)]   // Physical but hidden.
    public void IsPhysical(int characteristics, bool expected)
    {
        Assert.Equal(expected, AdapterFilter.IsPhysical(characteristics));
    }
}
