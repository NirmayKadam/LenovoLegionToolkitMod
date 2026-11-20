using System;
using System.Collections.Generic;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Settings;

/// <summary>When the Health check last ran, and which items were notified and are still unresolved.</summary>
public class HealthSettings() : AbstractSettings<HealthSettings.HealthSettingsStore>("health.json")
{
    public class HealthSettingsStore
    {
        public DateTime? LastCheckUtc { get; set; }
        public List<HealthCheck> Notified { get; set; } = [];
    }

    protected override HealthSettingsStore Default => new();
}
