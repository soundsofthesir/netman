using System.Globalization;

namespace NetMan.Core;

public enum ApplyOutcome
{
    /// <summary>Every value matches on a connected adapter, and the ping target replied if there is one.</summary>
    Applied,

    /// <summary>Every value matches the stored settings of a disconnected adapter.</summary>
    Saved,

    /// <summary>Every value matches, but the ping target did not reply.</summary>
    NoReply,

    Failed,
}

public sealed record CommandRecord(string CommandLine, CommandResult Result);

public sealed record ApplyReport(
    string ProfileName,
    Guid AdapterId,
    string AdapterName,
    ApplyOutcome Outcome,
    string Error,
    IReadOnlyList<CommandRecord> Commands,
    VerifyResult? Verify,
    string PingTarget,
    PingResult? Ping,
    LiveConfig? Before,
    DateTimeOffset Finished)
{
    /// <summary>True once netsh has run, so the adapter may have changed and the snapshot can be restored.</summary>
    public bool CanUndo => Before is not null && Commands.Count > 0;
}

public sealed record ApplyTimings(
    TimeSpan PollInterval,
    TimeSpan VerifyTimeout,
    TimeSpan DhcpTimeout,
    int PingCount,
    TimeSpan PingTimeout)
{
    public static readonly ApplyTimings Default = new(
        PollInterval: TimeSpan.FromMilliseconds(250),
        VerifyTimeout: TimeSpan.FromSeconds(10),
        DhcpTimeout: TimeSpan.FromSeconds(15),
        PingCount: 3,
        PingTimeout: TimeSpan.FromSeconds(1));
}

/// <summary>
/// Applies a profile and proves the result: pre-check, snapshot, netsh, poll the live settings until they match,
/// ping, report. Every step is written to the log.
/// </summary>
public sealed class ApplyPipeline(
    IAdapterReader adapters,
    ICommandRunner netsh,
    IPinger pinger,
    ApplyLog? log,
    TimeProvider clock,
    ApplyTimings timings)
{
    public const string InvalidProfile = "Profile is not valid";
    public const string AdapterNotFound = "Adapter not found";
    public const string AdapterRemoved = "Adapter removed";
    public const string NetshTimedOut = "netsh did not respond";
    public const string CannotRestore = "Previous settings cannot be restored";
    public const string ResetName = "Reset to DHCP";
    public const string UndoName = "Undo";

    public Task<ApplyReport> ApplyAsync(Profile profile, CancellationToken cancellationToken) =>
        RunAsync(profile, clearBlankDns: false, cancellationToken);

    public Task<ApplyReport> ResetToDhcpAsync(Guid adapterId, CancellationToken cancellationToken) =>
        RunAsync(new Profile { Name = ResetName, AdapterId = adapterId, Mode = AddressMode.Dhcp }, clearBlankDns: false, cancellationToken);

    /// <summary>Restores the settings captured before <paramref name="last"/> ran, clearing DNS if there was none.</summary>
    public Task<ApplyReport> UndoAsync(ApplyReport last, CancellationToken cancellationToken)
    {
        if (!last.CanUndo)
        {
            throw new InvalidOperationException("Nothing to undo.");
        }

        var restore = Capture.ToProfile(last.Before!, last.AdapterId, last.AdapterName, UndoName);
        if (!ProfileValidator.IsValid(restore))
        {
            return Task.FromResult(Finish(restore, last.AdapterName, ApplyOutcome.Failed, CannotRestore, [], null, null, null));
        }

        return RunAsync(restore, clearBlankDns: true, cancellationToken);
    }

    private async Task<ApplyReport> RunAsync(Profile profile, bool clearBlankDns, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var adapter = adapters.List().FirstOrDefault(a => a.Id == profile.AdapterId);
        var adapterName = adapter?.Name ?? profile.AdapterName;
        Log($"Apply {profile.Name} on {adapterName}");

        if (!ProfileValidator.IsValid(profile))
        {
            return Finish(profile, adapterName, ApplyOutcome.Failed, InvalidProfile, [], null, null, null);
        }

        if (adapter is null)
        {
            return Finish(profile, adapterName, ApplyOutcome.Failed, AdapterNotFound, [], null, null, null);
        }

        if (IpUsedElsewhere(profile) is { } other)
        {
            return Finish(profile, adapterName, ApplyOutcome.Failed, $"IP already used by {other}", [], null, null, null);
        }

        var before = adapters.Read(adapter.Id);
        if (before is null)
        {
            return Finish(profile, adapterName, ApplyOutcome.Failed, AdapterRemoved, [], null, null, null);
        }

        var (records, commandError) = await RunCommandsAsync(NetshBuilder.ForProfile(profile, adapter.Name, clearBlankDns), ct).ConfigureAwait(false);

        var timeout = profile.Mode == AddressMode.Dhcp ? timings.DhcpTimeout : timings.VerifyTimeout;
        var verify = await VerifyAsync(profile, clearBlankDns, commandError is null ? timeout : TimeSpan.Zero, ct).ConfigureAwait(false);
        if (verify is null)
        {
            return Finish(profile, adapterName, ApplyOutcome.Failed, AdapterRemoved, records, null, null, before);
        }

        if (commandError is not null || verify.Status != CheckStatus.Pass)
        {
            return Finish(profile, adapterName, ApplyOutcome.Failed, commandError ?? "", records, verify, null, before);
        }

        if (!verify.AdapterConnected)
        {
            return Finish(profile, adapterName, ApplyOutcome.Saved, "", records, verify, null, before);
        }

        PingResult? ping = null;
        if (!string.IsNullOrWhiteSpace(profile.PingTarget))
        {
            ping = await pinger.PingAsync(profile.PingTarget.Trim(), timings.PingCount, timings.PingTimeout, ct).ConfigureAwait(false);
        }

        var outcome = ping is { Reachable: false } ? ApplyOutcome.NoReply : ApplyOutcome.Applied;
        return Finish(profile, adapterName, outcome, "", records, verify, ping, before);
    }

    private string? IpUsedElsewhere(Profile profile)
    {
        if (profile.Mode != AddressMode.Static || !Ipv4.TryParse(profile.Ip, out var ip))
        {
            return null;
        }

        foreach (var other in adapters.List().Where(a => a.Id != profile.AdapterId))
        {
            var live = adapters.Read(other.Id);
            if (live is not null && live.Addresses.Any(a => Ipv4.TryParse(a.Ip, out var v) && v == ip))
            {
                return other.Name;
            }
        }

        return null;
    }

    private async Task<(IReadOnlyList<CommandRecord> Records, string? Error)> RunCommandsAsync(IReadOnlyList<NetshCommand> commands, CancellationToken ct)
    {
        var records = new List<CommandRecord>();
        foreach (var command in commands)
        {
            var line = ProcessCommandRunner.Describe(netsh.Executable, command.Arguments);
            Log(line);
            var result = await netsh.RunAsync(command.Arguments, ct).ConfigureAwait(false);
            Log(result.TimedOut ? "timed out" : $"exit {result.ExitCode.ToString(CultureInfo.InvariantCulture)}\n{result.StdOut}\n{result.StdErr}");
            records.Add(new CommandRecord(line, result));

            if (!result.Succeeded)
            {
                return (records.AsReadOnly(), CommandError(result));
            }
        }

        return (records.AsReadOnly(), null);
    }

    /// <summary>Polls until every check passes, a duplicate address is found, or the timeout passes. Null when the adapter disappears.</summary>
    private async Task<VerifyResult?> VerifyAsync(Profile profile, bool clearBlankDns, TimeSpan timeout, CancellationToken ct)
    {
        var started = clock.GetTimestamp();
        while (true)
        {
            var live = adapters.Read(profile.AdapterId);
            if (live is null)
            {
                return null;
            }

            var result = Verifier.Compare(profile, live, clearBlankDns);
            var conflict = result.Checks.Any(c => c.Kind == CheckKind.Conflict && c.Status == CheckStatus.Fail);
            if (result.Status == CheckStatus.Pass || conflict || clock.GetElapsedTime(started) >= timeout)
            {
                return FinalizeChecks(result);
            }

            await Task.Delay(timings.PollInterval, clock, ct).ConfigureAwait(false);
        }
    }

    // A check still pending when polling stops did not settle in time, so it counts as failed.
    private static VerifyResult FinalizeChecks(VerifyResult result) =>
        result with
        {
            Checks = result.Checks
                .Select(c => c.Status == CheckStatus.Pending ? c with { Status = CheckStatus.Fail } : c)
                .ToList()
                .AsReadOnly(),
        };

    private static string CommandError(CommandResult result)
    {
        if (result.TimedOut)
        {
            return NetshTimedOut;
        }

        var message = new[] { result.StdOut, result.StdErr }
            .SelectMany(s => s.Split('\n'))
            .Select(s => s.Trim())
            .FirstOrDefault(s => s.Length > 0);

        return message is null
            ? $"netsh exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}"
            : $"netsh: {message}";
    }

    private ApplyReport Finish(
        Profile profile,
        string adapterName,
        ApplyOutcome outcome,
        string error,
        IReadOnlyList<CommandRecord> commands,
        VerifyResult? verify,
        PingResult? ping,
        LiveConfig? before)
    {
        foreach (var check in verify?.Checks ?? [])
        {
            Log($"{check.Kind} {check.Status} expected {check.Expected} actual {check.Actual}");
        }

        if (ping is not null)
        {
            var timing = ping.AverageMs is { } ms ? $"{ms.ToString(CultureInfo.InvariantCulture)} ms" : "no reply";
            Log($"Ping {profile.PingTarget.Trim()} {ping.Received.ToString(CultureInfo.InvariantCulture)}/{ping.Sent.ToString(CultureInfo.InvariantCulture)} {timing}");
        }

        Log(error.Length == 0 ? $"Result {outcome}" : $"Result {outcome}: {error}");

        return new ApplyReport(
            profile.Name,
            profile.AdapterId,
            adapterName,
            outcome,
            error,
            commands,
            verify,
            profile.PingTarget.Trim(),
            ping,
            before,
            clock.GetLocalNow());
    }

    private void Log(string text) => log?.Write(text);
}
