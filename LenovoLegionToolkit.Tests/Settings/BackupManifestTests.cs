using System;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Windows.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.Settings;

/// <summary>The note saved with a settings backup (real files in a temporary folder) and the "another model" check.</summary>
public sealed class BackupManifestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-backup-manifest-" + Guid.NewGuid().ToString("N"));

    public BackupManifestTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static readonly BackupManifest Loq = new("2.36.8.0", "82XV", "LOQ 15IRH8", new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Written_ThenRead_GivesTheSameNote()
    {
        BackupManifest.Write(_dir, Loq);
        Assert.Equal(Loq, BackupManifest.Read(_dir));
    }

    [Fact]
    public void TheNote_HoldsNoSerialNumber()
    {
        BackupManifest.Write(_dir, Loq);
        Assert.DoesNotContain("Serial", File.ReadAllText(Path.Combine(_dir, BackupManifest.FileName)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ABackupWithoutANote_OrAnUnreadableOne_GivesNull()
    {
        Assert.Null(BackupManifest.Read(_dir));   // a backup made before notes existed

        File.WriteAllText(Path.Combine(_dir, BackupManifest.FileName), "{ not json");
        Assert.Null(BackupManifest.Read(_dir));
    }

    [Theory]
    [InlineData("82XV", "LOQ 15IRH8", false)]       // this laptop
    [InlineData("82xv ", "LOQ 15IRH8 2023", false)] // same machine type; the model text may differ by BIOS
    [InlineData("82JW", "Legion 5 15ACH6H", true)]  // another model
    [InlineData("", "LOQ 15IRH8", false)]           // machine type unknown here: the model name decides
    [InlineData("", "Legion 5 15ACH6H", true)]
    [InlineData("", "", false)]                     // nothing known here: can't tell, so no warning
    public void AnotherModel_IsTold_ByTheMachineType(string machineType, string model, bool expected)
    {
        Assert.Equal(expected, BackupManifest.IsFromAnotherModel(Loq, machineType, model));
    }

    [Fact]
    public void TheNote_IsNeverRestoredIntoTheSettingsFolder()
    {
        Assert.DoesNotContain(BackupManifest.FileName, SettingsBackupWindow.KnownSettingsFiles);
    }
}
