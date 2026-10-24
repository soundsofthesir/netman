namespace NetMan.Core.Tests;

public sealed class ApplyPipelineTests : IDisposable
{
    private static readonly AdapterInfo Eth2 = new(Guid.NewGuid(), "Ethernet 2", "Intel I219", AdapterKind.Ethernet, true);
    private static readonly AdapterInfo Usb = new(Guid.NewGuid(), "USB Ethernet", "Realtek USB", AdapterKind.Ethernet, true);

    private static readonly ApplyTimings Fast = new(
        PollInterval: TimeSpan.FromMilliseconds(1),
        VerifyTimeout: TimeSpan.FromMilliseconds(60),
        DhcpTimeout: TimeSpan.FromMilliseconds(60),
        PingCount: 3,
        PingTimeout: TimeSpan.FromMilliseconds(10));

    private static readonly LiveConfig DhcpLease = Live(dhcp: true, addresses: [new("192.168.1.23", 24, DadState.Preferred)], gateways: ["192.168.1.1"], dns: ["192.168.1.1"]);
    private static readonly LiveConfig PlcBench = Live(addresses: [new("10.10.1.20", 24, DadState.Preferred)]);

    private readonly string _logPath = Path.Combine(Path.GetTempPath(), "netman-apply-" + Guid.NewGuid().ToString("N") + ".txt");

    public void Dispose()
    {
        File.Delete(_logPath);
    }

    private static LiveConfig Live(
        bool dhcp = false,
        bool connected = true,
        LiveAddress[]? addresses = null,
        string[]? gateways = null,
        string[]? dns = null) =>
        new(connected, dhcp, addresses ?? [], gateways ?? [], dns ?? []);

    private static Profile Plc(string ping = "") => new()
    {
        Name = "PLC bench",
        AdapterId = Eth2.Id,
        AdapterName = "stale name",
        Mode = AddressMode.Static,
        Ip = "10.10.1.20",
        Mask = "/24",
        PingTarget = ping,
    };

    private static ApplyPipeline Pipeline(FakeAdapters adapters, FakeRunner? runner = null, FakePinger? pinger = null, ApplyLog? log = null) =>
        new(adapters, runner ?? new FakeRunner(), pinger ?? new FakePinger(), log, TimeProvider.System, Fast);

    [Fact]
    public async Task Applies_verifies_and_pings()
    {
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease, PlcBench);
        var runner = new FakeRunner();
        var pinger = new FakePinger();

        var report = await Pipeline(adapters, runner, pinger).ApplyAsync(Plc(ping: "10.10.1.10"), CancellationToken.None);

        Assert.Equal(ApplyOutcome.Applied, report.Outcome);
        Assert.Equal("", report.Error);
        Assert.Equal("Ethernet 2", report.AdapterName);
        Assert.Equal(DhcpLease, report.Before);
        Assert.Equal(CheckStatus.Pass, report.Verify!.Status);
        Assert.Equal(["10.10.1.10"], pinger.Targets);
        Assert.True(report.Ping!.Reachable);
        Assert.True(report.CanUndo);
        Assert.Equal("name=Ethernet 2", runner.Calls.Single()[4]);
    }

    [Fact]
    public async Task Keeps_polling_until_settings_match()
    {
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease, DhcpLease, DhcpLease, Live(addresses: [new("10.10.1.20", 24, DadState.Tentative)]), PlcBench);

        var report = await Pipeline(adapters).ApplyAsync(Plc(), CancellationToken.None);

        Assert.Equal(ApplyOutcome.Applied, report.Outcome);
        Assert.Equal(5, adapters.Reads.Count(id => id == Eth2.Id));
    }

    [Fact]
    public async Task Times_out_with_the_last_mismatch()
    {
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease);

        var report = await Pipeline(adapters).ApplyAsync(Plc(), CancellationToken.None);

        Assert.Equal(ApplyOutcome.Failed, report.Outcome);
        var mode = report.Verify!.Checks.Single(c => c.Kind == CheckKind.Mode);
        Assert.Equal((CheckStatus.Fail, "DHCP"), (mode.Status, mode.Actual));
    }

    [Fact]
    public async Task Pending_checks_become_failures_at_timeout()
    {
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease, Live(addresses: [new("10.10.1.20", 24, DadState.Tentative)]));

        var report = await Pipeline(adapters).ApplyAsync(Plc(), CancellationToken.None);

        Assert.Equal(ApplyOutcome.Failed, report.Outcome);
        Assert.Equal(CheckStatus.Fail, report.Verify!.Checks.Single(c => c.Kind == CheckKind.Conflict).Status);
    }

    [Fact]
    public async Task Duplicate_address_stops_polling_immediately_and_skips_ping()
    {
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease, Live(addresses: [new("10.10.1.20", 24, DadState.Duplicate)]));
        var pinger = new FakePinger();

        var report = await Pipeline(adapters, pinger: pinger).ApplyAsync(Plc(ping: "10.10.1.10"), CancellationToken.None);

        Assert.Equal(ApplyOutcome.Failed, report.Outcome);
        Assert.Equal(2, adapters.Reads.Count(id => id == Eth2.Id));
        Assert.Equal(Verifier.InUse, report.Verify!.Checks.Single(c => c.Kind == CheckKind.Conflict).Actual);
        Assert.Empty(pinger.Targets);
    }

    [Fact]
    public async Task No_ping_reply_is_its_own_outcome()
    {
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease, PlcBench);
        var pinger = new FakePinger(new PingResult(3, 0, null));

        var report = await Pipeline(adapters, pinger: pinger).ApplyAsync(Plc(ping: "10.10.1.10"), CancellationToken.None);

        Assert.Equal(ApplyOutcome.NoReply, report.Outcome);
    }

    [Fact]
    public async Task Disconnected_adapter_is_saved_and_not_pinged()
    {
        var unplugged = Eth2 with { IsConnected = false };
        var adapters = new FakeAdapters().Add(unplugged, Live(dhcp: true, connected: false), Live(connected: false, addresses: [new("10.10.1.20", 24, DadState.Preferred)]));
        var pinger = new FakePinger();

        var report = await Pipeline(adapters, pinger: pinger).ApplyAsync(Plc(ping: "10.10.1.10"), CancellationToken.None);

        Assert.Equal(ApplyOutcome.Saved, report.Outcome);
        Assert.Empty(pinger.Targets);
    }

    [Fact]
    public async Task Dhcp_waits_for_a_lease()
    {
        var linkLocal = Live(dhcp: true, addresses: [new("169.254.12.7", 16, DadState.Preferred)]);
        var adapters = new FakeAdapters().Add(Eth2, PlcBench, linkLocal, linkLocal, DhcpLease);

        var report = await Pipeline(adapters).ApplyAsync(Plc() with { Mode = AddressMode.Dhcp }, CancellationToken.None);

        Assert.Equal(ApplyOutcome.Applied, report.Outcome);
    }

    [Fact]
    public async Task Dhcp_without_a_lease_fails()
    {
        var linkLocal = Live(dhcp: true, addresses: [new("169.254.12.7", 16, DadState.Preferred)]);
        var adapters = new FakeAdapters().Add(Eth2, PlcBench, linkLocal);

        var report = await Pipeline(adapters).ApplyAsync(Plc() with { Mode = AddressMode.Dhcp }, CancellationToken.None);

        Assert.Equal(ApplyOutcome.Failed, report.Outcome);
        var lease = report.Verify!.Checks.Single(c => c.Kind == CheckKind.Lease);
        Assert.Equal((CheckStatus.Fail, "169.254.12.7/16"), (lease.Status, lease.Actual));
    }

    [Fact]
    public async Task Invalid_profile_fails_without_running_netsh()
    {
        var runner = new FakeRunner();
        var report = await Pipeline(new FakeAdapters().Add(Eth2, DhcpLease), runner).ApplyAsync(Plc() with { Ip = "nope" }, CancellationToken.None);

        Assert.Equal((ApplyOutcome.Failed, ApplyPipeline.InvalidProfile), (report.Outcome, report.Error));
        Assert.Empty(runner.Calls);
        Assert.False(report.CanUndo);
    }

    [Fact]
    public async Task Missing_adapter_fails_without_running_netsh()
    {
        var runner = new FakeRunner();
        var report = await Pipeline(new FakeAdapters().Add(Usb, DhcpLease), runner).ApplyAsync(Plc(), CancellationToken.None);

        Assert.Equal((ApplyOutcome.Failed, ApplyPipeline.AdapterNotFound), (report.Outcome, report.Error));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Ip_on_another_local_adapter_fails_without_running_netsh()
    {
        var runner = new FakeRunner();
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease).Add(Usb, PlcBench);

        var report = await Pipeline(adapters, runner).ApplyAsync(Plc(), CancellationToken.None);

        Assert.Equal((ApplyOutcome.Failed, "IP already used by USB Ethernet"), (report.Outcome, report.Error));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Same_ip_already_on_the_target_adapter_is_fine()
    {
        var adapters = new FakeAdapters().Add(Eth2, PlcBench);
        var report = await Pipeline(adapters).ApplyAsync(Plc(), CancellationToken.None);
        Assert.Equal(ApplyOutcome.Applied, report.Outcome);
    }

    [Fact]
    public async Task Netsh_failure_stops_and_reports_its_output()
    {
        var runner = new FakeRunner().Then(new CommandResult(1, "\r\nThe filename, directory name, or volume label syntax is incorrect.\r\n\r\n", "", false));
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease);

        var report = await Pipeline(adapters, runner).ApplyAsync(Plc() with { Dns1 = "1.1.1.1" }, CancellationToken.None);

        Assert.Equal(ApplyOutcome.Failed, report.Outcome);
        Assert.Equal("netsh: The filename, directory name, or volume label syntax is incorrect.", report.Error);
        Assert.Single(runner.Calls);
        Assert.True(report.CanUndo);
        Assert.NotNull(report.Verify);
    }

    [Fact]
    public async Task Netsh_timeout_is_reported()
    {
        var runner = new FakeRunner().Then(new CommandResult(ProcessCommandRunner.TimedOutExitCode, "", "", true));
        var report = await Pipeline(new FakeAdapters().Add(Eth2, DhcpLease), runner).ApplyAsync(Plc(), CancellationToken.None);

        Assert.Equal((ApplyOutcome.Failed, ApplyPipeline.NetshTimedOut), (report.Outcome, report.Error));
    }

    [Fact]
    public async Task Netsh_failure_without_output_shows_the_exit_code()
    {
        var runner = new FakeRunner().Then(new CommandResult(5, "", "  ", false));
        var report = await Pipeline(new FakeAdapters().Add(Eth2, DhcpLease), runner).ApplyAsync(Plc(), CancellationToken.None);

        Assert.Equal("netsh exit code 5", report.Error);
    }

    [Fact]
    public async Task Adapter_removed_during_verify_fails()
    {
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease);
        var runner = new RemovingRunner(adapters, Eth2.Id);

        var report = await new ApplyPipeline(adapters, runner, new FakePinger(), null, TimeProvider.System, Fast).ApplyAsync(Plc(), CancellationToken.None);

        Assert.Equal((ApplyOutcome.Failed, ApplyPipeline.AdapterRemoved), (report.Outcome, report.Error));
    }

    [Fact]
    public async Task Reset_to_dhcp_sets_address_and_dns_and_verifies()
    {
        var adapters = new FakeAdapters().Add(Eth2, PlcBench, DhcpLease);
        var runner = new FakeRunner();

        var report = await Pipeline(adapters, runner).ResetToDhcpAsync(Eth2.Id, CancellationToken.None);

        Assert.Equal(ApplyOutcome.Applied, report.Outcome);
        Assert.Equal(ApplyPipeline.ResetName, report.ProfileName);
        Assert.Equal(["source=dhcp", "source=dhcp"], runner.Calls.Select(c => c[^1]));
        Assert.Equal(PlcBench, report.Before);
    }

    [Fact]
    public async Task Undo_restores_the_snapshot_and_clears_dns_it_did_not_have()
    {
        var before = Live(addresses: [new("192.168.0.50", 24, DadState.Preferred)], gateways: ["192.168.0.1"]);
        var afterApply = Live(addresses: [new("10.10.1.20", 24, DadState.Preferred)], dns: ["1.1.1.1"]);
        var adapters = new FakeAdapters().Add(Eth2, before, afterApply, afterApply, before);
        var runner = new FakeRunner();
        var pipeline = Pipeline(adapters, runner);

        var applied = await pipeline.ApplyAsync(Plc() with { Dns1 = "1.1.1.1" }, CancellationToken.None);
        runner.Calls.Clear();
        var undone = await pipeline.UndoAsync(applied, CancellationToken.None);

        Assert.Equal(ApplyOutcome.Applied, applied.Outcome);
        Assert.Equal(ApplyOutcome.Applied, undone.Outcome);
        Assert.Equal(ApplyPipeline.UndoName, undone.ProfileName);
        Assert.Contains(runner.Calls, c => c.Contains("address=192.168.0.50") && c.Contains("gateway=192.168.0.1"));
        Assert.Contains(runner.Calls, c => c.Contains("address=none"));
        Assert.Equal(afterApply, undone.Before);
    }

    [Fact]
    public async Task Undo_to_dhcp()
    {
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease, PlcBench, PlcBench, DhcpLease);
        var pipeline = Pipeline(adapters);

        var undone = await pipeline.UndoAsync(await pipeline.ApplyAsync(Plc(), CancellationToken.None), CancellationToken.None);

        Assert.Equal(ApplyOutcome.Applied, undone.Outcome);
    }

    [Fact]
    public async Task Undo_of_unrestorable_snapshot_fails()
    {
        var staticWithNothing = Live();
        var adapters = new FakeAdapters().Add(Eth2, staticWithNothing, PlcBench);
        var pipeline = Pipeline(adapters);

        var undone = await pipeline.UndoAsync(await pipeline.ApplyAsync(Plc(), CancellationToken.None), CancellationToken.None);

        Assert.Equal((ApplyOutcome.Failed, ApplyPipeline.CannotRestore), (undone.Outcome, undone.Error));
    }

    [Fact]
    public async Task Undo_without_a_snapshot_throws()
    {
        var pipeline = Pipeline(new FakeAdapters().Add(Eth2, DhcpLease));
        var failed = await pipeline.ApplyAsync(Plc() with { Ip = "x" }, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.UndoAsync(failed, CancellationToken.None));
    }

    [Fact]
    public async Task Writes_commands_checks_and_outcome_to_the_log()
    {
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease, PlcBench);

        await Pipeline(adapters, log: new ApplyLog(_logPath)).ApplyAsync(Plc(ping: "10.10.1.10"), CancellationToken.None);

        var text = File.ReadAllText(_logPath);
        Assert.Contains("Apply PLC bench on Ethernet 2", text, StringComparison.Ordinal);
        Assert.Contains("netsh interface ipv4 set address \"name=Ethernet 2\" source=static", text, StringComparison.Ordinal);
        Assert.Contains("exit 0", text, StringComparison.Ordinal);
        Assert.Contains("Ip Pass expected 10.10.1.20/24 actual 10.10.1.20/24", text, StringComparison.Ordinal);
        Assert.Contains("Ping 10.10.1.10 3/3 2 ms", text, StringComparison.Ordinal);
        Assert.Contains("Result Applied", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var adapters = new FakeAdapters().Add(Eth2, DhcpLease);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pipeline(adapters).ApplyAsync(Plc(), cts.Token));
    }

    private sealed class RemovingRunner(FakeAdapters adapters, Guid id) : ICommandRunner
    {
        public string Executable => "netsh";

        public Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            adapters.Remove(id);
            return Task.FromResult(new CommandResult(0, "", "", false));
        }
    }
}
