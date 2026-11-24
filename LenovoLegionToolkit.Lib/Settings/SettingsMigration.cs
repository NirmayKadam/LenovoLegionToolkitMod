using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json.Linq;

namespace LenovoLegionToolkit.Lib.Settings;

/// <summary>
/// Settings written by an older version, made readable by this one. Settings files store choices by name, so a
/// renamed value (Lenovo Legion Toolkit 2.38 renamed the chipset "PCH" readings to motherboard and system fans)
/// would make the whole file unreadable, and the settings would fall back to defaults. Old names are changed to the
/// new ones as the file is read; nothing else in it changes.
/// </summary>
public static class SettingsMigration
{
    /// <summary>OSD items renamed in 2.38.</summary>
    public static readonly IReadOnlyDictionary<string, string> OsdItemRenames = new Dictionary<string, string>
    {
        ["PchTemperature"] = nameof(OsdItem.MotherboardTemperature),
        ["PchFan"] = nameof(OsdItem.SystemFans),
    };

    /// <summary>Dashboard sensor items renamed in 2.38.</summary>
    public static readonly IReadOnlyDictionary<string, string> SensorItemRenames = new Dictionary<string, string>
    {
        ["PchTemperature"] = nameof(SensorItem.MotherboardTemperature),
        ["PchFanSpeed"] = nameof(SensorItem.SystemFans),
    };

    /// <summary>Fan table types renamed in 2.38 (stored with Custom mode's fan curves).</summary>
    public static readonly IReadOnlyDictionary<string, string> FanTableTypeRenames = new Dictionary<string, string>
    {
        ["PCH"] = nameof(FanTableType.SystemFan),
    };

    /// <summary>
    /// Replaces every string value equal to an old name (only in properties called <paramref name="onlyProperty"/>,
    /// when given) with its new name. Lists keep their order; a value that would then appear twice in the same list
    /// is kept once. Text that isn't JSON is returned unchanged, for the normal reading to deal with.
    /// </summary>
    public static string RenameEnumValues(string json, IReadOnlyDictionary<string, string> renames, string? onlyProperty = null)
    {
        if (string.IsNullOrWhiteSpace(json) || !renames.Keys.Any(json.Contains))
            return json;

        JToken root;
        try
        {
            root = JToken.Parse(json);
        }
        catch (Exception)
        {
            return json;
        }

        var changed = 0;
        var tokens = root is JContainer container ? container.DescendantsAndSelf() : [root];
        foreach (var value in tokens.OfType<JValue>().Where(v => v.Type == JTokenType.String).ToArray())
        {
            if (value.Value is not string text || !renames.TryGetValue(text, out var newName))
                continue;

            if (onlyProperty is not null && (value.Parent as JProperty)?.Name != onlyProperty)
                continue;

            if (value.Parent is JArray array && array.Any(t => t.Type == JTokenType.String && (string?)t == newName))
                value.Remove();
            else
                value.Value = newName;

            changed++;
        }

        if (changed == 0)
            return json;

        Log.Instance.Trace($"Settings written by an older version: {changed} renamed value(s) updated.");
        return root.ToString();
    }
}
