using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace NetMan.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V10.0.12", 10, 0, 12)]
    public void Parses_release_tags(string tag, int major, int minor, int build)
    {
        Assert.True(AppVersion.TryParse(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3.4")]
    [InlineData("v1.2.3-beta")]
    [InlineData("latest")]
    public void Rejects_other_tags(string tag)
    {
        Assert.False(AppVersion.TryParse(tag, out _));
    }

    [Fact]
    public void Normalize_drops_the_revision()
    {
        Assert.Equal(new Version(1, 2, 3), AppVersion.Normalize(new Version(1, 2, 3, 0)));
        Assert.Equal(new Version(1, 2, 0), AppVersion.Normalize(new Version(1, 2)));
    }

    [Fact]
    public void Development_build_is_version_zero()
    {
        Assert.True(AppVersion.IsDevelopment(new Version(0, 0, 0, 0)));
        Assert.False(AppVersion.IsDevelopment(new Version(0, 1, 0)));
    }
}

/// <summary>A signing key pair standing in for the release key.</summary>
internal static class TestKeys
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static readonly string PublicKey = Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());

    public static readonly string OtherPublicKey = Convert.ToBase64String(ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportSubjectPublicKeyInfo());

    public static string Sign(byte[] exe, Version version) =>
        Convert.ToBase64String(Key.SignData(UpdateSignature.Message(exe, version), HashAlgorithmName.SHA256));
}

public class UpdateSignatureTests
{
    private static readonly byte[] Exe = Encoding.ASCII.GetBytes("MZ fake executable");
    private static readonly Version V140 = new(1, 4, 0);

    [Fact]
    public void Message_binds_version_and_hash()
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Exe));
        Assert.Equal($"NetMan 1.4.0\n{hash}", Encoding.UTF8.GetString(UpdateSignature.Message(Exe, V140)));
    }

    [Fact]
    public void Valid_signature_verifies()
    {
        Assert.True(UpdateSignature.Verify(Exe, V140, TestKeys.Sign(Exe, V140), TestKeys.PublicKey));
    }

    [Fact]
    public void Changed_file_fails()
    {
        Assert.False(UpdateSignature.Verify(Encoding.ASCII.GetBytes("tampered"), V140, TestKeys.Sign(Exe, V140), TestKeys.PublicKey));
    }

    [Fact]
    public void Different_version_fails()
    {
        Assert.False(UpdateSignature.Verify(Exe, new Version(2, 0, 0), TestKeys.Sign(Exe, V140), TestKeys.PublicKey));
    }

    [Fact]
    public void Different_key_fails()
    {
        Assert.False(UpdateSignature.Verify(Exe, V140, TestKeys.Sign(Exe, V140), TestKeys.OtherPublicKey));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("")]
    [InlineData("AAAA")]
    public void Malformed_signature_fails(string signature)
    {
        Assert.False(UpdateSignature.Verify(Exe, V140, signature, TestKeys.PublicKey));
    }

    [Fact]
    public void Malformed_public_key_fails()
    {
        Assert.False(UpdateSignature.Verify(Exe, V140, TestKeys.Sign(Exe, V140), "AAAA"));
    }
}

public class ReleaseParserTests
{
    internal static string Json(string tag, bool draft = false, bool prerelease = false, bool withExe = true, bool withSig = true)
    {
        var assets = new List<string>();
        if (withExe)
        {
            assets.Add("""{"name":"NetMan.exe","browser_download_url":"https://example.test/NetMan.exe","size":10}""");
        }

        if (withSig)
        {
            assets.Add("""{"name":"NetMan.exe.sig","browser_download_url":"https://example.test/NetMan.exe.sig","size":90}""");
        }

        return $$"""{"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},"assets":[{{string.Join(",", assets)}}]}""";
    }

    [Fact]
    public void Reads_version_and_asset_urls()
    {
        var release = ReleaseParser.Parse(Json("v1.4.0"));

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 4, 0), release.Version);
        Assert.Equal("https://example.test/NetMan.exe", release.ExeUrl.ToString());
        Assert.Equal("https://example.test/NetMan.exe.sig", release.SigUrl.ToString());
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    public void Skips_drafts_prereleases_and_incomplete_releases(bool draft, bool prerelease, bool exe, bool sig)
    {
        Assert.Null(ReleaseParser.Parse(Json("v1.4.0", draft, prerelease, exe, sig)));
    }

    [Fact]
    public void Http_asset_urls_are_ignored()
    {
        Assert.Null(ReleaseParser.Parse(Json("v1.4.0").Replace("https://example.test/NetMan.exe\"", "http://example.test/NetMan.exe\"", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"tag_name":"nightly","assets":[]}""")]
    public void Bad_documents_throw(string json)
    {
        Assert.Throws<UpdateException>(() => ReleaseParser.Parse(json));
    }
}

public sealed class UpdateServiceTests : IDisposable
{
    private static readonly byte[] Exe = Encoding.ASCII.GetBytes("MZ fake executable");
    private static readonly Version V140 = new(1, 4, 0);
    private static readonly UpdateSource Source = new("soundsofthesir", "netman");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netman-upd-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };

    private static FakeHttp Server(string tag = "v1.4.0", string? signature = null, byte[]? exe = null, HttpStatusCode latest = HttpStatusCode.OK) => new(request =>
    {
        var url = request.RequestUri!.ToString();
        if (url.EndsWith("/releases/latest", StringComparison.Ordinal))
        {
            return latest == HttpStatusCode.OK ? Ok(ReleaseParserTests.Json(tag)) : new HttpResponseMessage(latest);
        }

        if (url.EndsWith(".sig", StringComparison.Ordinal))
        {
            return Ok((signature ?? TestKeys.Sign(Exe, V140)) + "\n");
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(exe ?? Exe) };
    });

    private UpdateService Service(FakeHttp http) => new(new HttpClient(http), Source, _dir, TestKeys.PublicKey);

    [Fact]
    public async Task Downloads_and_verifies_a_newer_release()
    {
        var http = Server();

        var pending = await Service(http).CheckAsync(new Version(1, 3, 9), CancellationToken.None);

        Assert.NotNull(pending);
        Assert.Equal(V140, pending.Version);
        Assert.Equal(Exe, File.ReadAllBytes(pending.FilePath));
        Assert.Equal("https://api.github.com/repos/soundsofthesir/netman/releases/latest", http.Requests[0]);
        Assert.Equal(pending, UpdateInstaller.FindPending(_dir, new Version(1, 3, 9), TestKeys.PublicKey));
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
    }

    [Fact]
    public async Task Sends_a_user_agent()
    {
        HttpRequestMessage? seen = null;
        var http = new FakeHttp(r =>
        {
            seen ??= r;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        await Service(http).CheckAsync(new Version(1, 0, 0), CancellationToken.None);

        Assert.Contains("NetMan", seen!.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 4, 0)]
    [InlineData(2, 0, 0)]
    public async Task Same_or_older_release_does_nothing(int major, int minor, int build)
    {
        var http = Server();

        Assert.Null(await Service(http).CheckAsync(new Version(major, minor, build), CancellationToken.None));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task No_releases_yet_does_nothing()
    {
        Assert.Null(await Service(Server(latest: HttpStatusCode.NotFound)).CheckAsync(new Version(1, 0, 0), CancellationToken.None));
    }

    [Fact]
    public async Task Server_error_throws()
    {
        await Assert.ThrowsAsync<UpdateException>(() => Service(Server(latest: HttpStatusCode.InternalServerError)).CheckAsync(new Version(1, 0, 0), CancellationToken.None));
    }

    [Fact]
    public async Task Tampered_download_throws_and_leaves_nothing()
    {
        var http = Server(exe: Encoding.ASCII.GetBytes("tampered"));

        await Assert.ThrowsAsync<UpdateException>(() => Service(http).CheckAsync(new Version(1, 0, 0), CancellationToken.None));

        Assert.Empty(Directory.Exists(_dir) ? Directory.GetFiles(_dir) : []);
    }

    [Fact]
    public async Task Signature_for_another_version_throws()
    {
        var http = Server(signature: TestKeys.Sign(Exe, new Version(1, 3, 0)));
        await Assert.ThrowsAsync<UpdateException>(() => Service(http).CheckAsync(new Version(1, 0, 0), CancellationToken.None));
    }

    [Fact]
    public async Task Already_downloaded_release_is_not_downloaded_again()
    {
        var http = Server();
        await Service(http).CheckAsync(new Version(1, 0, 0), CancellationToken.None);
        http.Requests.Clear();

        var pending = await Service(http).CheckAsync(new Version(1, 0, 0), CancellationToken.None);

        Assert.NotNull(pending);
        Assert.Single(http.Requests);
    }
}

public sealed class UpdateInstallerTests : IDisposable
{
    private static readonly Version V140 = new(1, 4, 0);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netman-inst-" + Guid.NewGuid().ToString("N"));
    private string Updates => Path.Combine(_dir, "updates");
    private string ExePath => Path.Combine(_dir, "app", "NetMan.exe");

    public UpdateInstallerTests()
    {
        Directory.CreateDirectory(Updates);
        Directory.CreateDirectory(Path.GetDirectoryName(ExePath)!);
        File.WriteAllText(ExePath, "old");
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    private PendingUpdate Stage(string content = "new", string? signature = null)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var file = Path.Combine(Updates, "NetMan-1.4.0.exe");
        File.WriteAllBytes(file, bytes);
        var pending = new PendingUpdate(V140, file, signature ?? TestKeys.Sign(bytes, V140));
        UpdateInstaller.WriteMarker(Updates, pending);
        return pending;
    }

    [Fact]
    public void Install_swaps_the_exe_and_keeps_the_old_one_until_cleanup()
    {
        var pending = Stage();

        UpdateInstaller.Install(pending, ExePath, Updates, TestKeys.PublicKey);

        Assert.Equal("new", File.ReadAllText(ExePath));
        Assert.Equal("old", File.ReadAllText(ExePath + ".old"));
        Assert.Null(UpdateInstaller.FindPending(Updates, new Version(1, 0, 0), TestKeys.PublicKey));
        Assert.False(File.Exists(pending.FilePath));

        Assert.True(UpdateInstaller.CleanupOld(ExePath));
        Assert.False(File.Exists(ExePath + ".old"));
    }

    [Fact]
    public void Install_refuses_a_file_without_a_valid_signature_and_leaves_the_exe()
    {
        var pending = Stage();
        File.WriteAllText(pending.FilePath, "planted");

        Assert.Throws<UpdateException>(() => UpdateInstaller.Install(pending, ExePath, Updates, TestKeys.PublicKey));

        Assert.Equal("old", File.ReadAllText(ExePath));
        Assert.False(File.Exists(ExePath + ".old"));
    }

    [Fact]
    public void FindPending_ignores_versions_not_newer_than_current()
    {
        Stage();
        Assert.Null(UpdateInstaller.FindPending(Updates, V140, TestKeys.PublicKey));
    }

    [Fact]
    public void FindPending_rejects_a_file_changed_after_download()
    {
        var pending = Stage();
        File.WriteAllText(pending.FilePath, "changed");

        Assert.Null(UpdateInstaller.FindPending(Updates, new Version(1, 0, 0), TestKeys.PublicKey));
    }

    [Fact]
    public void FindPending_rejects_a_planted_update_signed_with_another_key()
    {
        Stage();
        Assert.Null(UpdateInstaller.FindPending(Updates, new Version(1, 0, 0), TestKeys.OtherPublicKey));
    }

    [Fact]
    public void FindPending_with_no_marker_or_a_bad_marker_is_null()
    {
        Assert.Null(UpdateInstaller.FindPending(Updates, new Version(1, 0, 0), TestKeys.PublicKey));
        File.WriteAllText(Path.Combine(Updates, UpdateInstaller.MarkerName), "junk");
        Assert.Null(UpdateInstaller.FindPending(Updates, new Version(1, 0, 0), TestKeys.PublicKey));
        Assert.Null(UpdateInstaller.FindPending(Path.Combine(_dir, "missing"), new Version(1, 0, 0), TestKeys.PublicKey));
    }

    [Fact]
    public void CleanupOld_without_an_old_file_is_true()
    {
        Assert.True(UpdateInstaller.CleanupOld(ExePath));
    }
}
