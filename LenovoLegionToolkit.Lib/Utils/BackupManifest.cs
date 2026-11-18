using System;
using System.IO;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// A note saved next to a settings backup: which LLT and which laptop model made it, and when. No serial number.
/// It is never copied into the settings folder on restore; it's only read to warn when the backup comes from a
/// different model, whose Custom mode limits and fan curves may not fit this one.
/// </summary>
public sealed record BackupManifest(string LltVersion, string MachineType, string Model, DateTime CreatedUtc)
{
    public const string FileName = "backup_info.json";

    public static void Write(string folder, BackupManifest manifest) =>
        File.WriteAllText(Path.Combine(folder, FileName), JsonConvert.SerializeObject(manifest, Formatting.Indented));

    /// <summary>The backup's note, or null for a backup made before notes existed, or one that can't be read.</summary>
    public static BackupManifest? Read(string folder)
    {
        var path = Path.Combine(folder, FileName);
        try
        {
            if (!File.Exists(path))
                return null;

            return JsonConvert.DeserializeObject<BackupManifest>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the backup note {path}.", ex);
            return null;
        }
    }

    /// <summary>
    /// True when the backup was made on another laptop model. The machine type (e.g. 82XV) decides; the model
    /// name is only used when a machine type is missing. Unknown on either side means "can't tell", so false.
    /// </summary>
    public static bool IsFromAnotherModel(BackupManifest manifest, string? machineType, string? model)
    {
        static bool Known(string? s) => !string.IsNullOrWhiteSpace(s);
        static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        if (Known(manifest.MachineType) && Known(machineType))
            return !Same(manifest.MachineType, machineType!);

        if (Known(manifest.Model) && Known(model))
            return !Same(manifest.Model, model!);

        return false;
    }
}
