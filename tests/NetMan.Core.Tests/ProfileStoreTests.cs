namespace NetMan.Core.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netman-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "profiles.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static Profile Sample(string name) => new()
    {
        Name = name,
        Notes = "rack 3",
        AdapterId = Guid.NewGuid(),
        AdapterName = "Ethernet 2",
        Mode = AddressMode.Static,
        Ip = "10.10.1.20",
        Mask = "255.255.255.0",
        Gateway = "10.10.1.1",
        Dns1 = "1.1.1.1",
        PingTarget = "10.10.1.10",
    };

    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        Assert.Empty(new ProfileStore(FilePath).Load());
    }

    [Fact]
    public void Save_then_load_round_trips_and_creates_directory()
    {
        var store = new ProfileStore(FilePath);
        var profiles = new[] { Sample("A"), Sample("B") with { Mode = AddressMode.Dhcp } };

        store.Save(profiles);

        Assert.Equal(profiles, store.Load());
    }

    [Fact]
    public void Save_keeps_previous_file_as_backup_and_leaves_no_temp_file()
    {
        var store = new ProfileStore(FilePath);
        store.Save([Sample("A")]);
        store.Save([Sample("B")]);

        Assert.True(File.Exists(FilePath + ".bak"));
        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Equal("A", new ProfileStore(FilePath + ".bak").Load().Single().Name);
        Assert.Equal("B", store.Load().Single().Name);
    }

    [Fact]
    public void Mode_is_stored_as_text()
    {
        new ProfileStore(FilePath).Save([Sample("A")]);
        Assert.Contains("\"Static\"", File.ReadAllText(FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Null_and_missing_fields_load_as_empty_strings()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"version":1,"profiles":[{"name":"A","notes":null,"mode":"Dhcp"}]}""");

        var p = new ProfileStore(FilePath).Load().Single();

        Assert.Equal("A", p.Name);
        Assert.Equal("", p.Notes);
        Assert.Equal("", p.Ip);
        Assert.Equal("", p.AdapterName);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":2,"profiles":[]}""")]
    [InlineData("""{"version":1,"profiles":[null]}""")]
    [InlineData("null")]
    public void Load_throws_store_exception_for_bad_files(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);
        Assert.Throws<ProfileStoreException>(() => new ProfileStore(FilePath).Load());
    }
}
