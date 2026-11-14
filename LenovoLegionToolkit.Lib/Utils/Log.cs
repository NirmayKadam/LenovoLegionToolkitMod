using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace LenovoLegionToolkit.Lib.Utils;

public class Log
{
    private static Log? _instance;
    public static Log Instance
    {
        get
        {
            _instance ??= new Log();
            return _instance;
        }
    }

    private readonly object _lock = new();
    private readonly string _folderPath;
    private readonly string _logPath;
    private readonly Func<DateTime> _utcNow;

    /// <summary>How many error reports are kept in the log folder; older ones are deleted.</summary>
    internal const int MaxErrorReports = 20;

    public bool IsTraceEnabled { get; set; }

    public string LogPath => _logPath;

    private Log() : this(Path.Combine(Folders.AppData, "log"), () => DateTime.UtcNow) { }

    /// <summary>For tests: a separate folder and clock, so the real log folder is never touched.</summary>
    internal Log(string folderPath, Func<DateTime> utcNow)
    {
        _folderPath = folderPath;
        _utcNow = utcNow;
        Directory.CreateDirectory(_folderPath);
        _logPath = Path.Combine(_folderPath, $"log_{_utcNow():yyyy_MM_dd_HH_mm_ss}.txt");
    }

    /// <summary>When each recent error was last reported, so a repeating error doesn't fill the folder.</summary>
    private readonly Dictionary<string, DateTime> _recentReports = [];

    private static readonly TimeSpan RepeatWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Writes an error report to the local log folder, whether or not trace logging is on. Nothing is sent
    /// anywhere. The same error (type, message and where it was thrown) is written once a minute at most, and
    /// only the newest <see cref="MaxErrorReports"/> reports are kept.
    /// </summary>
    public void ErrorReport(string header, Exception ex)
    {
        try
        {
            var now = _utcNow();
            var key = $"{ex.GetType().FullName}|{ex.Message}|{ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";

            lock (_lock)
            {
                if (_recentReports.TryGetValue(key, out var last) && now - last < RepeatWindow)
                    return;

                _recentReports[key] = now;
                foreach (var old in _recentReports.Where(r => now - r.Value >= RepeatWindow).Select(r => r.Key).ToList())
                    _recentReports.Remove(old);
            }

            var errorReportPath = Path.Combine(_folderPath, $"error_{now:yyyy_MM_dd_HH_mm_ss}.txt");
            Folders.EnsureParentDirectoryExists(errorReportPath);
            File.AppendAllLines(errorReportPath, [header, Serialize(ex)]);

            RemoveOldErrorReports();
        }
        catch
        {
            // Ignored
        }
    }

    /// <summary>Keeps the newest reports (their names sort by time) and deletes the rest.</summary>
    private void RemoveOldErrorReports()
    {
        var reports = Directory.GetFiles(_folderPath, "error_*.txt").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        foreach (var old in reports.Take(Math.Max(0, reports.Length - MaxErrorReports)))
        {
            try
            {
                File.Delete(old);
            }
            catch
            {
                // Ignored: a report that can't be deleted now goes next time.
            }
        }
    }

    public void Trace(FormattableString message,
        Exception? ex = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int lineNumber = -1,
        [CallerMemberName] string? caller = null)
    {
        if (!IsTraceEnabled)
        {
            return;
        }

        LogInternal(_logPath, message, ex, file, lineNumber, caller);
    }

    private void LogInternal(string path,
        FormattableString message,
        Exception? ex,
        string? file,
        int lineNumber,
        string? caller)
    {
        lock (_lock)
        {
            var lines = new List<string>
            {
                $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss.fff}] [Thread Id: {Environment.CurrentManagedThreadId}] [{Path.GetFileName(file)}#{lineNumber}:{caller}] {message}"
            };
            if (ex is not null)
            {
                lines.Add(Serialize(ex));
            }

#if DEBUG
            foreach (var line in lines)
            {
                Debug.WriteLine(line);
            }
#endif

            try
            {
                File.AppendAllLines(path, lines);
            }
            catch (DirectoryNotFoundException)
            {
                try
                {
                    Directory.CreateDirectory(_folderPath);
                    File.AppendAllLines(path, lines);
                }
                catch
                {
                    // Ignored
                }
            }
            catch
            {
                // Ignored
            }
        }
    }

    private static string Serialize(Exception ex) => new StringBuilder()
        .AppendLine("=== Exception ===")
        .AppendLine(ex.ToString())
        .AppendLine()
        .AppendLine("=== Exception demystified ===")
        .AppendLine(ex.ToStringDemystified())
        .ToString();
}
