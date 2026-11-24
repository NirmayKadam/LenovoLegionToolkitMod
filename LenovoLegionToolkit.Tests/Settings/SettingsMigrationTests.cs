using System;
using System.Collections.Generic;
using System.IO;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LenovoLegionToolkit.Tests.Settings;

/// <summary>
/// Lenovo Legion Toolkit 2.38 renamed the chipset ("PCH") readings. Settings saved by Indra 2.36.26 must still load
/// (with the new names) instead of falling back to defaults.
/// </summary>
public class SettingsMigrationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "indra-tests-migration-" + Guid.NewGuid().ToString("N"));

    public SettingsMigrationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    [Fact]
    public void OsdItems_AreRenamed_InOrder()
    {
        var json = """{"Items":["CpuTemperature","PchTemperature","PchFan","GpuFan"]}""";

        var result = JObject.Parse(SettingsMigration.RenameEnumValues(json, SettingsMigration.OsdItemRenames));

        Assert.Equal(["CpuTemperature", "MotherboardTemperature", "SystemFans", "GpuFan"], result["Items"]!.ToObject<string[]>()!);
    }

    [Fact]
    public void ARenameThatWouldRepeatAnItem_KeepsItOnce()
    {
        var json = """{"VisibleItems":["SystemFans","PchFanSpeed","CpuFanSpeed"]}""";

        var result = JObject.Parse(SettingsMigration.RenameEnumValues(json, SettingsMigration.SensorItemRenames));

        Assert.Equal(["SystemFans", "CpuFanSpeed"], result["VisibleItems"]!.ToObject<string[]>()!);
    }

    [Fact]
    public void OnlyTheNamedProperty_IsChanged()
    {
        var json = """{"Data":[{"Type":"PCH","Name":"PCH"}]}""";

        var result = JObject.Parse(SettingsMigration.RenameEnumValues(json, SettingsMigration.FanTableTypeRenames, onlyProperty: "Type"));

        Assert.Equal("SystemFan", (string?)result["Data"]![0]!["Type"]);
        Assert.Equal("PCH", (string?)result["Data"]![0]!["Name"]);
    }

    [Theory]
    [InlineData("""{"Items":["CpuTemperature"]}""")]
    [InlineData("not json PchFan")]
    [InlineData("")]
    public void NothingToRename_OrNotJson_IsReturnedUnchanged(string json)
    {
        Assert.Same(json, SettingsMigration.RenameEnumValues(json, SettingsMigration.OsdItemRenames));
    }

    private sealed class Store
    {
        public List<OsdItem> Items { get; set; } = [];
    }

    private sealed class OldFileSettings(string folder) : AbstractSettings<Store>("osd_items.json", folder)
    {
        protected override string MigrateJson(string json) => SettingsMigration.RenameEnumValues(json, SettingsMigration.OsdItemRenames);
    }

    private sealed class UnmigratedSettings(string folder) : AbstractSettings<Store>("osd_items.json", folder);

    [Fact]
    public void AFileWithOldNames_Loads()
    {
        File.WriteAllText(Path.Combine(_folder, "osd_items.json"), """{"Items":["CpuTemperature","PchFan"]}""");

        Assert.Equal([OsdItem.CpuTemperature, OsdItem.SystemFans], new OldFileSettings(_folder).Store.Items);
    }

    [Fact]
    public void WithoutTheMigration_TheOldFileWouldNotLoad()
    {
        // The reason for the migration: the same file falls back to an empty store (and is backed up).
        File.WriteAllText(Path.Combine(_folder, "osd_items.json"), """{"Items":["CpuTemperature","PchFan"]}""");

        Assert.Empty(new UnmigratedSettings(_folder).Store.Items);
    }
}
