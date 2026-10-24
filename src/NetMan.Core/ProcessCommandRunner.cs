using System.ComponentModel;
using System.Diagnostics;

namespace NetMan.Core;

public sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

public interface ICommandRunner
{
    string Executable { get; }

    Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// <summary>Runs an executable with an argument list, no shell. Kills the process tree when it exceeds the timeout.</summary>
public sealed class ProcessCommandRunner(string executable, TimeSpan timeout) : ICommandRunner
{
    public const int TimedOutExitCode = -1;
    public const int StartFailedExitCode = -2;

    public string Executable { get; } = executable;

    public async Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new CommandResult(StartFailedExitCode, "", $"Cannot start {Executable}: {ex.Message}", TimedOut: false);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new CommandResult(TimedOutExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), TimedOut: true);
        }

        return new CommandResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), TimedOut: false);
    }

    /// <summary>The command as one line for the log. Quotes arguments that contain spaces.</summary>
    public static string Describe(string executable, IReadOnlyList<string> arguments) =>
        string.Join(' ', new[] { executable }.Concat(arguments).Select(a => a.Length == 0 || a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a));

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timeout and the kill.
        }
    }
}
