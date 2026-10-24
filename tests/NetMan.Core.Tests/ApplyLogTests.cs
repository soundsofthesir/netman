namespace NetMan.Core.Tests;

public sealed class ApplyLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "netman-log-" + Guid.NewGuid().ToString("N"));
    private string LogPath => Path.Combine(_dir, "log.txt");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 10, 24, 14, 32, 7, 120, TimeSpan.Zero));

    [Fact]
    public void Writes_timestamped_lines_and_creates_directory()
    {
        var log = new ApplyLog(LogPath, Clock);

        Assert.True(log.Write("netsh interface ipv4 set address"));
        Assert.True(log.Write("exit 0"));

        Assert.Equal(
            ["2026-10-24 14:32:07.120  netsh interface ipv4 set address", "2026-10-24 14:32:07.120  exit 0"],
            File.ReadAllLines(LogPath));
    }

    [Fact]
    public void Multi_line_text_is_indented_under_one_timestamp()
    {
        new ApplyLog(LogPath, Clock).Write("Ok.\r\n\r\nDone.");

        Assert.Equal(["2026-10-24 14:32:07.120  Ok.", "                         Done."], File.ReadAllLines(LogPath));
    }

    [Fact]
    public void Rotates_when_over_the_size_limit()
    {
        var log = new ApplyLog(LogPath, Clock, maxBytes: 100);
        log.Write(new string('a', 120));
        log.Write("after");

        Assert.Contains("aaaa", File.ReadAllText(LogPath + ".1"), StringComparison.Ordinal);
        Assert.Equal(["2026-10-24 14:32:07.120  after"], File.ReadAllLines(LogPath));
    }

    [Fact]
    public void Returns_false_when_the_file_cannot_be_written()
    {
        Directory.CreateDirectory(LogPath);

        Assert.False(new ApplyLog(LogPath, Clock).Write("x"));
    }
}
