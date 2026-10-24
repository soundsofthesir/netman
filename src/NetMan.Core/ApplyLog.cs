using System.Globalization;

namespace NetMan.Core;

/// <summary>Appends timestamped lines to a text file. Keeps one rotated file once the log passes the size limit.</summary>
public sealed class ApplyLog(string path, TimeProvider? clock = null, long maxBytes = ApplyLog.DefaultMaxBytes)
{
    public const long DefaultMaxBytes = 1024 * 1024;
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    private const string Gap = "  ";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();

    public string FilePath { get; } = path;

    /// <summary>Returns false when the log cannot be written. A failed log write never stops an apply.</summary>
    public bool Write(string text)
    {
        var stamp = _clock.GetLocalNow().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var indent = new string(' ', stamp.Length + Gap.Length);
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select((line, i) => (i == 0 ? stamp + Gap : indent) + line)
            .ToArray();
        if (lines.Length == 0)
        {
            lines = [stamp];
        }

        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(FilePath));
                if (dir is not null)
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= maxBytes)
                {
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                }

                File.AppendAllLines(FilePath, lines);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
