using System;
using System.IO;
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

    protected AbstractSettings(string filename)
    {
        JsonSerializerSettings = CreateJsonSerializerSettings();

        _fileName = filename;
        _settingsStorePath = Path.Combine(Folders.Settings, _fileName);
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

    private void TryBackup()
    {
        try
        {
            if (!File.Exists(_settingsStorePath))
                return;

            var backupFileName = $"{Path.GetFileNameWithoutExtension(_fileName)}_backup_{DateTime.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(_fileName)}";
            var backupFilePath = Path.Combine(Folders.Settings, backupFileName);
            Folders.EnsureParentDirectoryExists(backupFilePath);
            File.Copy(_settingsStorePath, backupFilePath);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to create backup for {_fileName}", ex);
        }
    }
}
