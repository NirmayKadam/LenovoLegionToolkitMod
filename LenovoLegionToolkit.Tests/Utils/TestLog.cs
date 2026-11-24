using System;
using System.IO;
using System.Runtime.CompilerServices;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>
/// Points the app's log at a temporary folder for the whole test run, so no test creates or writes to the real log
/// folder in %LOCALAPPDATA%. Detailed logging stays off, as it is by default.
/// </summary>
internal static class TestRunLog
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var folder = Path.Combine(Path.GetTempPath(), "llt-test-run-log-" + Guid.NewGuid().ToString("N"));
        Log.ReplaceInstance(new Log(folder, () => DateTime.UtcNow));
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch
            {
                // A file still open at exit stays in the temp folder.
            }
        };
    }
}

/// <summary>
/// A real log with detailed logging on, in a temporary folder, in place of the app's log for one test. Tests that use
/// it must be in <see cref="UI.AppLoggerCollection"/>, so no other test writes to the log meanwhile.
/// </summary>
internal sealed class TestLog : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "llt-test-log-" + Guid.NewGuid().ToString("N"));
    private readonly Log? _previous;

    public TestLog()
    {
        Log = new Log(_folder, () => DateTime.UtcNow) { IsTraceEnabled = true };
        _previous = Log.ReplaceInstance(Log);
    }

    public Log Log { get; }

    /// <summary>Everything written so far (empty when nothing was).</summary>
    public string Text
    {
        get
        {
            if (!File.Exists(Log.LogPath))
                return string.Empty;

            using var stream = new FileStream(Log.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    public void Dispose()
    {
        Log.ReplaceInstance(_previous);
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }
}
