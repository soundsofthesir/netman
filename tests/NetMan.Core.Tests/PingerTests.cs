namespace NetMan.Core.Tests;

public class PingerTests
{
    private readonly Pinger _pinger = new();

    [Fact]
    public async Task Loopback_replies()
    {
        var result = await _pinger.PingAsync("127.0.0.1", 2, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(2, result.Sent);
        Assert.Equal(2, result.Received);
        Assert.NotNull(result.AverageMs);
        Assert.True(result.Reachable);
    }

    [Fact]
    public async Task Documentation_address_does_not_reply()
    {
        var result = await _pinger.PingAsync("192.0.2.1", 1, TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.Equal(1, result.Sent);
        Assert.Equal(0, result.Received);
        Assert.Null(result.AverageMs);
        Assert.False(result.Reachable);
    }

    [Theory]
    [InlineData("nope", 1)]
    [InlineData("127.0.0.1", 0)]
    public async Task Bad_arguments_throw(string target, int count)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _pinger.PingAsync(target, count, TimeSpan.FromSeconds(1), CancellationToken.None));
    }
}
