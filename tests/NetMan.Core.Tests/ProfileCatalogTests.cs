namespace NetMan.Core.Tests;

public class ProfileCatalogTests
{
    private static readonly AdapterInfo Eth2 = new(Guid.NewGuid(), "Ethernet 2", "", AdapterKind.Ethernet, true);
    private static readonly AdapterInfo Usb = new(Guid.NewGuid(), "USB Ethernet", "", AdapterKind.Ethernet, true);
    private static readonly Guid Gone = Guid.NewGuid();

    private static Profile P(string name, Guid adapter) => new() { Name = name, AdapterId = adapter, AdapterName = "x", Mode = AddressMode.Dhcp };

    private static readonly Profile A = P("Lab switch", Eth2.Id);
    private static readonly Profile B = P("PLC bench", Eth2.Id);
    private static readonly Profile C = P("Camera", Usb.Id);
    private static readonly Profile D = P("Old dongle", Gone);

    private static readonly ProfileCatalog Catalog = new([A, B, C, D]);

    [Fact]
    public void Groups_by_adapter_in_stored_order()
    {
        Assert.Equal([A, B], Catalog.ForAdapter(Eth2.Id));
        Assert.Equal([C], Catalog.ForAdapter(Usb.Id));
    }

    [Fact]
    public void Orphans_are_profiles_whose_adapter_is_not_present()
    {
        Assert.Equal([D], Catalog.Orphans([Eth2.Id, Usb.Id]));
        Assert.Equal([C, D], Catalog.Orphans([Eth2.Id]));
    }

    [Fact]
    public void Upsert_replaces_in_place_or_appends()
    {
        var renamed = B with { Name = "PLC rack" };
        Assert.Equal([A, renamed, C, D], Catalog.Upsert(renamed).All);

        var added = P("New", Usb.Id);
        Assert.Equal([A, B, C, D, added], Catalog.Upsert(added).All);
        Assert.Equal([A, B, C, D], Catalog.All);
    }

    [Fact]
    public void Remove_drops_by_id()
    {
        Assert.Equal([A, C, D], Catalog.Remove(B.Id).All);
    }

    [Fact]
    public void Duplicate_inserts_after_the_source()
    {
        var (next, copy) = Catalog.Duplicate(A.Id);

        Assert.Equal("Lab switch (2)", copy.Name);
        Assert.Equal([A, copy, B, C, D], next.All);
    }

    [Fact]
    public void CopyTo_appends_on_the_target_adapter()
    {
        var (next, copy) = Catalog.CopyTo(D.Id, Usb);

        Assert.Equal(Usb.Id, copy.AdapterId);
        Assert.Equal("USB Ethernet", copy.AdapterName);
        Assert.Equal([C, copy], next.ForAdapter(Usb.Id));
        Assert.Equal(D, next.Find(D.Id));
    }

    [Fact]
    public void Unknown_id_throws()
    {
        Assert.Throws<KeyNotFoundException>(() => Catalog.Remove(Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => Catalog.Duplicate(Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => Catalog.CopyTo(Guid.NewGuid(), Usb));
        Assert.Null(Catalog.Find(Guid.NewGuid()));
    }

    [Fact]
    public void NameTaken_checks_the_same_adapter_without_case_and_skips_the_edited_profile()
    {
        Assert.True(Catalog.NameTaken("lab SWITCH", Eth2.Id, exceptId: null));
        Assert.False(Catalog.NameTaken("Lab switch", Eth2.Id, exceptId: A.Id));
        Assert.False(Catalog.NameTaken("Lab switch", Usb.Id, exceptId: null));
        Assert.True(Catalog.NameTaken(" Lab switch ", Eth2.Id, exceptId: null));
    }

    [Fact]
    public void SyncAdapterNames_updates_stored_names_of_present_adapters()
    {
        var renamed = Eth2 with { Name = "Ethernet 3" };

        var next = Catalog.SyncAdapterNames([renamed, Usb]);

        Assert.All(next.ForAdapter(Eth2.Id), p => Assert.Equal("Ethernet 3", p.AdapterName));
        Assert.Equal("USB Ethernet", next.Find(C.Id)!.AdapterName);
        Assert.Equal("x", next.Find(D.Id)!.AdapterName);
        Assert.True(next.Differs(Catalog));
        Assert.False(next.SyncAdapterNames([renamed, Usb]).Differs(next));
    }
}
