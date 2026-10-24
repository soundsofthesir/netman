namespace NetMan.Core.Tests;

// Uses /bin/sh, so these run on macOS and Linux. The Windows runner is the same class pointed at netsh.exe.
public class ProcessCommandRunnerTests
{
    private static readonly ProcessCommandRunner Sh = new("/bin/sh", TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Captures_exit_code_stdout_and_stderr()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await Sh.RunAsync(["-c", "echo out; echo err 1>&2; exit 3"], CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("out", result.StdOut.Trim());
        Assert.Equal("err", result.StdErr.Trim());
        Assert.False(result.TimedOut);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Passes_arguments_with_spaces_as_single_arguments()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await Sh.RunAsync(["-c", "printf '%s|' \"$@\"", "_", "name=Ethernet 2", "x"], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("name=Ethernet 2|x|", result.StdOut);
    }

    [Fact]
    public async Task Kills_the_process_on_timeout()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new ProcessCommandRunner("/bin/sh", TimeSpan.FromMilliseconds(300));
        var started = DateTime.UtcNow;

        var result = await runner.RunAsync(["-c", "sleep 10"], CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Missing_executable_is_a_failed_result()
    {
        var runner = new ProcessCommandRunner("/no/such/netsh", TimeSpan.FromSeconds(1));

        var result = await runner.RunAsync(["x"], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ProcessCommandRunner.StartFailedExitCode, result.ExitCode);
        Assert.NotEmpty(result.StdErr);
    }

    [Fact]
    public void Describe_formats_command_for_the_log()
    {
        Assert.Equal("netsh interface \"name=Ethernet 2\"", ProcessCommandRunner.Describe("netsh", ["interface", "name=Ethernet 2"]));
    }
}
