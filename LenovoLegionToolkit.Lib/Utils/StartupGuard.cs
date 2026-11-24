using System;
using System.Globalization;
using System.IO;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Notices when Indra's previous start never finished (it crashed, froze and was ended, or the laptop went down while
/// it was starting). The next start is then a safe start: it applies no hardware settings, so a setting that brought
/// the start down can't do it again and again. A note file is written when a start begins and removed once it has
/// finished; a note still there at the next start means that start didn't finish.
/// <para>
/// A note that can't be read or written never blocks a start: it then counts as "finished".
/// </para>
/// </summary>
public sealed class StartupGuard(string folder)
{
    public const string FileName = "startup_in_progress";

    private readonly string _path = Path.Combine(folder, FileName);

    /// <summary>When the unfinished start began (local time), when <see cref="Begin"/> found one.</summary>
    public DateTime? UnfinishedStartedAt { get; private set; }

    /// <summary>
    /// Call when a start begins. True when the previous start never finished; either way the note for this start is
    /// written.
    /// </summary>
    public bool Begin(string version)
    {
        var unfinished = false;
        try
        {
            if (File.Exists(_path))
            {
                unfinished = true;
                var firstLine = File.ReadAllLines(_path) is [var line, ..] ? line : string.Empty;
                if (DateTime.TryParse(firstLine, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startedAt))
                    UnfinishedStartedAt = startedAt.ToLocalTime();
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the start-up note; treating the last start as finished.", ex);
            unfinished = false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllLines(_path, [DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), version]);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't write the start-up note.", ex);
        }

        return unfinished;
    }

    /// <summary>Call once the start has finished (and on a clean exit). Never throws.</summary>
    public void Complete()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't remove the start-up note.", ex);
        }
    }
}
