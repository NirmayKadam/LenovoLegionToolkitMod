using System;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LenovoLegionToolkit.Lib.Settings;

public abstract class AbstractSettings<T> where T : class, new()
{
    protected readonly JsonSerializerSettings JsonSerializerSettings;
    private readonly string _settingsStorePath;
    private readonly string _fileName;
    private T? _store;

    protected virtual T Default => new();

    public T Store
    {
        get => _store ??= LoadStore() ?? Default;
        protected set => _store = value;
    }

    protected AbstractSettings(string filename) : this(filename, Folders.Settings) { }

    /// <summary>Stores the file in <paramref name="folder"/> instead of the app's settings folder (used by tests).</summary>
    protected AbstractSettings(string filename, string folder)
    {
        JsonSerializerSettings = CreateJsonSerializerSettings();

        _fileName = filename;
        _settingsStorePath = Path.Combine(folder, _fileName);
    }

    /// <summary>
    /// The JSON settings every settings file (and LampArray profile import) is read and written with.
    /// </summary>
    public static JsonSerializerSettings CreateJsonSerializerSettings() => new()
    {
        Formatting = Formatting.Indented,
        TypeNameHandling = TypeNameHandling.Auto,
        SerializationBinder = SafeSerializationBinder.Instance,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Converters = { new StringEnumConverter() }
    };

    public void Save()
    {
        try
        {
            WriteStore();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to save {_fileName}", ex);
        }
    }

    public void EnsureFileExists()
    {
        if (!File.Exists(_settingsStorePath))
        {
            Save();
        }
    }

    public void Reset()
    {
        _store = null;
    }

    public virtual T? LoadStore()
    {
        T? store = null;
        try
        {
            if (!File.Exists(_settingsStorePath)) return null;

            var settingsSerialized = File.ReadAllText(_settingsStorePath);
            store = JsonConvert.DeserializeObject<T>(settingsSerialized, JsonSerializerSettings);

            if (store is null)
                TryBackup();
        }
        catch
        {
            TryBackup();
        }

        return store;
    }

    public void SynchronizeStore()
    {
        try
        {
            WriteStore();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to synchronize {_fileName}", ex);
        }
    }

    private void WriteStore()
    {
        var settingsSerialized = JsonConvert.SerializeObject(Store, JsonSerializerSettings);
        Folders.EnsureParentDirectoryExists(_settingsStorePath);
        WriteAllTextAtomic(_settingsStorePath, settingsSerialized);
        RemoveStaleTempFiles();
    }

    /// <summary>
    /// Writes to a temporary file in the same folder, then replaces the target in one rename.
    /// A crash or power loss mid-write leaves either the old file or the new one, never a half-written file.
    /// </summary>
    internal static void WriteAllTextAtomic(string path, string contents)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Ignored, the original exception matters more.
            }

            throw;
        }
    }

    /// <summary>How many backups of one unreadable settings file are kept; older ones are deleted.</summary>
    internal const int MAX_BACKUPS = 5;

    /// <summary>How old a leftover temporary file from an interrupted save must be before it is removed.</summary>
    internal static readonly TimeSpan StaleTempFileAge = TimeSpan.FromDays(1);

    /// <summary>
    /// Keeps a copy of a settings file that couldn't be read, next to it. An unreadable file stays in place until
    /// something saves over it, so this runs at every start until then: a copy identical to the newest backup is
    /// not made again, and only the newest <see cref="MAX_BACKUPS"/> are kept.
    /// </summary>
    private void TryBackup()
    {
        try
        {
            if (!File.Exists(_settingsStorePath))
                return;

            var folder = Path.GetDirectoryName(_settingsStorePath)!;
            var name = Path.GetFileNameWithoutExtension(_fileName);
            var extension = Path.GetExtension(_fileName);

            var content = File.ReadAllBytes(_settingsStorePath);
            var backups = GetBackups(folder, name, extension);
            if (backups.Length > 0 && File.ReadAllBytes(backups[^1]).AsSpan().SequenceEqual(content))
                return;

            var stamp = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            var backupFilePath = Path.Combine(folder, $"{name}_backup_{stamp}{extension}");
            for (var n = 1; File.Exists(backupFilePath); n++)
                backupFilePath = Path.Combine(folder, $"{name}_backup_{stamp}_{n}{extension}");

            File.WriteAllBytes(backupFilePath, content);

            backups = GetBackups(folder, name, extension);
            foreach (var old in backups.Take(Math.Max(0, backups.Length - MAX_BACKUPS)))
                File.Delete(old);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to create backup for {_fileName}", ex);
        }
    }

    /// <summary>This file's backups, oldest first (by the time they were written, then by name).</summary>
    private static string[] GetBackups(string folder, string name, string extension) =>
        Directory.GetFiles(folder, $"{name}_backup_*{extension}")
            .OrderBy(File.GetLastWriteTimeUtc)
            .ThenBy(f => f, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Removes temporary files an interrupted save of this settings file left behind (a crash or power loss
    /// between writing and renaming). Only old ones, so a save in progress elsewhere is never touched.
    /// </summary>
    private void RemoveStaleTempFiles()
    {
        try
        {
            var folder = Path.GetDirectoryName(_settingsStorePath)!;
            if (!Directory.Exists(folder))
                return;

            var cutoff = DateTime.UtcNow - StaleTempFileAge;
            foreach (var temp in Directory.GetFiles(folder, $"{_fileName}.*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(temp) < cutoff)
                    File.Delete(temp);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to remove leftover temporary files for {_fileName}", ex);
        }
    }
}
