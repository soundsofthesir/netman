namespace NetMan.Core.Tests;

public class ProfileOperationsTests
{
    private static readonly Guid Eth2 = Guid.NewGuid();
    private static readonly Guid Usb = Guid.NewGuid();

    private static Profile Make(string name, Guid adapter, string adapterName = "Ethernet 2") => new()
    {
        Name = name,
        AdapterId = adapter,
        AdapterName = adapterName,
        Mode = AddressMode.Static,
        Ip = "10.10.1.20",
        Mask = "255.255.255.0",
        Notes = "rack 3",
    };

    [Fact]
    public void Duplicate_gets_new_id_and_numbered_name_on_same_adapter()
    {
        var source = Make("Lab switch", Eth2);
        var copy = ProfileOperations.Duplicate(source, [source]);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Lab switch (2)", copy.Name);
        Assert.Equal(source with { Id = copy.Id, Name = copy.Name }, copy);
    }

    [Fact]
    public void Duplicate_skips_taken_numbers()
    {
        var a = Make("Lab switch", Eth2);
        var b = Make("Lab switch (2)", Eth2);
        Assert.Equal("Lab switch (3)", ProfileOperations.Duplicate(a, [a, b]).Name);
    }

    [Fact]
    public void Duplicate_of_a_numbered_copy_does_not_stack_suffixes()
    {
        var a = Make("Lab switch", Eth2);
        var b = Make("Lab switch (2)", Eth2);
        Assert.Equal("Lab switch (3)", ProfileOperations.Duplicate(b, [a, b]).Name);
    }

    [Fact]
    public void Names_compare_without_case()
    {
        var a = Make("Lab switch", Eth2);
        var b = Make("LAB SWITCH (2)", Eth2);
        Assert.Equal("Lab switch (3)", ProfileOperations.Duplicate(a, [a, b]).Name);
    }

    [Fact]
    public void CopyTo_keeps_name_when_free_on_target_adapter()
    {
        var source = Make("Lab switch", Eth2);
        var copy = ProfileOperations.CopyTo(source, Usb, "USB Ethernet", [source]);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Lab switch", copy.Name);
        Assert.Equal(Usb, copy.AdapterId);
        Assert.Equal("USB Ethernet", copy.AdapterName);
        Assert.Equal(source.Ip, copy.Ip);
        Assert.Equal(source.Notes, copy.Notes);
    }

    [Fact]
    public void CopyTo_numbers_name_when_taken_on_target_adapter()
    {
        var source = Make("Lab switch", Eth2);
        var existing = Make("Lab switch", Usb, "USB Ethernet");
        Assert.Equal("Lab switch (2)", ProfileOperations.CopyTo(source, Usb, "USB Ethernet", [source, existing]).Name);
    }

    [Fact]
    public void CopyTo_rejects_empty_adapter()
    {
        var source = Make("Lab switch", Eth2);
        Assert.Throws<ArgumentException>(() => ProfileOperations.CopyTo(source, Guid.Empty, "x", [source]));
    }
}
