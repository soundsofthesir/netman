namespace NetMan.Core.Tests;

public class DisplayTextTests
{
    private static readonly AdapterInfo Eth2 = new(Guid.NewGuid(), "Ethernet 2", "", AdapterKind.Ethernet, true);

    [Fact]
    public void AdapterLine_shows_first_address_or_state()
    {
        var live = new LiveConfig(true, false, [new("10.10.1.20", 24, DadState.Preferred), new("10.10.1.21", 24, DadState.Preferred)], [], []);

        Assert.Equal("10.10.1.20/24", DisplayText.AdapterLine(Eth2, live));
        Assert.Equal("Disconnected", DisplayText.AdapterLine(Eth2 with { IsConnected = false }, live));
        Assert.Equal("No address", DisplayText.AdapterLine(Eth2, new LiveConfig(true, true, [], [], [])));
        Assert.Equal("No address", DisplayText.AdapterLine(Eth2, null));
    }

    [Fact]
    public void ProfileAddress()
    {
        var p = new Profile { Mode = AddressMode.Static, Ip = " 10.10.1.20", Mask = "255.255.255.0" };

        Assert.Equal("10.10.1.20/24", DisplayText.ProfileAddress(p));
        Assert.Equal("10.10.1.20/24  gw 10.10.1.1", DisplayText.ProfileAddress(p with { Gateway = "10.10.1.1" }));
        Assert.Equal("Automatic", DisplayText.ProfileAddress(p with { Mode = AddressMode.Dhcp }));
        Assert.Equal("bad", DisplayText.ProfileAddress(p with { Ip = "bad", Mask = "x" }));
    }

    [Theory]
    [InlineData(ApplyOutcome.Applied, "Applied")]
    [InlineData(ApplyOutcome.Saved, "Saved")]
    [InlineData(ApplyOutcome.NoReply, "No reply")]
    [InlineData(ApplyOutcome.Failed, "Failed")]
    public void Outcome(ApplyOutcome outcome, string text)
    {
        Assert.Equal(text, DisplayText.Outcome(outcome));
    }

    private static ApplyReport Report(IReadOnlyList<CheckResult> checks, PingResult? ping = null, string target = "") =>
        new("PLC bench", Eth2.Id, "Ethernet 2", ApplyOutcome.Applied, "", [], new VerifyResult(checks, true), target, ping, null,
            new DateTimeOffset(2026, 10, 24, 14, 32, 7, TimeSpan.Zero));

    [Fact]
    public void Rows_hide_a_passing_mode_and_label_each_check()
    {
        var rows = DisplayText.Rows(Report(
        [
            new(CheckKind.Mode, CheckStatus.Pass, "Static", "Static"),
            new(CheckKind.Ip, CheckStatus.Pass, "10.10.1.20/24", "10.10.1.20/24"),
            new(CheckKind.Gateway, CheckStatus.Fail, "10.10.1.1", "None"),
            new(CheckKind.Dns, CheckStatus.Pass, "1.1.1.1", "1.1.1.1"),
            new(CheckKind.Conflict, CheckStatus.Fail, "None", Verifier.InUse),
        ]));

        Assert.Equal(
            [
                new ResultRow("IP", "10.10.1.20/24", CheckStatus.Pass, ""),
                new ResultRow("Gateway", "None", CheckStatus.Fail, "Expected 10.10.1.1"),
                new ResultRow("DNS", "1.1.1.1", CheckStatus.Pass, ""),
                new ResultRow("Conflict", Verifier.InUse, CheckStatus.Fail, ""),
            ],
            rows);
    }

    [Fact]
    public void Failed_mode_and_lease_rows()
    {
        var rows = DisplayText.Rows(Report(
        [
            new(CheckKind.Mode, CheckStatus.Fail, "DHCP", "Static"),
            new(CheckKind.Lease, CheckStatus.Fail, "DHCP lease", "169.254.12.7/16"),
        ]));

        Assert.Equal(
            [
                new ResultRow("Mode", "Static", CheckStatus.Fail, "Expected DHCP"),
                new ResultRow("DHCP", "169.254.12.7/16", CheckStatus.Fail, ""),
            ],
            rows);
    }

    [Fact]
    public void Ping_row()
    {
        Assert.Equal(
            new ResultRow("Ping", "10.10.1.10  2 ms", CheckStatus.Pass, ""),
            DisplayText.Rows(Report([], new PingResult(3, 3, 2), "10.10.1.10")).Single());
        Assert.Equal(
            new ResultRow("Ping", "10.10.1.10  No reply", CheckStatus.Fail, ""),
            DisplayText.Rows(Report([], new PingResult(3, 0, null), "10.10.1.10")).Single());
    }

    [Fact]
    public void No_verify_means_no_rows()
    {
        var report = Report([]) with { Verify = null };
        Assert.Empty(DisplayText.Rows(report));
    }

    [Fact]
    public void Time_is_hours_minutes_seconds()
    {
        Assert.Equal("14:32:07", DisplayText.Time(Report([])));
    }
}
