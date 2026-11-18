using System.Collections.Generic;
using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.Lib.Macro.Utils;

public class MacroSettings : AbstractSettings<MacroSettings.MacroSettingsStore>
{
    private const string FILE_NAME = "macro.json";

    public MacroSettings() : base(FILE_NAME) { }

    /// <summary>Keeps the file in <paramref name="folder"/>, so tests never touch the real settings.</summary>
    internal MacroSettings(string folder) : base(FILE_NAME, folder) { }

    public class MacroSettingsStore
    {
        public bool IsEnabled { get; set; }

        public Dictionary<MacroIdentifier, MacroSequence> Sequences { get; set; } = [];
    }

    protected override MacroSettingsStore Default => new();
}
