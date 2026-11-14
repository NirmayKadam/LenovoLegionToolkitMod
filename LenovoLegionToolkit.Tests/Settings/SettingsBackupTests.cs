using System;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.Settings;

// Real files in a temp folder, through a real settings class (fan_profiles.json). "A start of the app" is a new
// settings object reading the file, which is what a restarted app does.
public sealed class SettingsBackupTests : IDisposable
{
    private const string FileName = "fan_profiles.json";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-backup-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsBackupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private string SettingsPath => Path.Combine(_dir, FileName);

    private string[] Backups() => Directory.GetFiles(_dir, "fan_profiles_backup_*.json");

    /// <summary>The app starts and reads its settings.</summary>
    private void StartApp() => _ = new FanProfileSettings(_dir).Store;

    [Fact]
    public void UnreadableFile_IsBackedUp_NextToIt()
    {
        File.WriteAllText(SettingsPath, "{ this is not json");

        StartApp();

        var backup = Assert.Single(Backups());
        Assert.Equal("{ this is not json", File.ReadAllText(backup));
        Assert.Equal("{ this is not json", File.ReadAllText(SettingsPath));   // the original is left for the user
    }

    [Fact]
    public void SameUnreadableFile_OverSeveralStarts_IsBackedUpOnce()
    {
        File.WriteAllText(SettingsPath, "{ this is not json");

        StartApp();
        StartApp();
        StartApp();

        Assert.Single(Backups());
    }

    [Fact]
    public void ReadableFile_IsNotBackedUp()
    {
        var settings = new FanProfileSettings(_dir);
        settings.Store.Enabled = true;
        settings.SynchronizeStore();

        StartApp();

        Assert.Empty(Backups());
    }

    [Fact]
    public void ManyDifferentUnreadableFiles_KeepOnlyTheNewestBackups()
    {
        for (var i = 1; i <= AbstractSettings<FanProfileSettings.FanProfileSettingsStore>.MAX_BACKUPS + 2; i++)
        {
            File.WriteAllText(SettingsPath, $"{{ broken number {i}");
            StartApp();
        }

        var kept = Backups().Select(File.ReadAllText).OrderBy(c => c, StringComparer.Ordinal).ToArray();

        Assert.Equal(AbstractSettings<FanProfileSettings.FanProfileSettingsStore>.MAX_BACKUPS, kept.Length);
        Assert.Equal(["{ broken number 3", "{ broken number 4", "{ broken number 5", "{ broken number 6", "{ broken number 7"], kept);
    }

    [Fact]
    public void Saving_RemovesOldLeftoverTempFiles_ButNotFreshOnes_NorOtherFiles()
    {
        var stale = Path.Combine(_dir, $"{FileName}.{Guid.NewGuid():N}.tmp");
        var fresh = Path.Combine(_dir, $"{FileName}.{Guid.NewGuid():N}.tmp");
        var otherSettingsFile = Path.Combine(_dir, $"other.json.{Guid.NewGuid():N}.tmp");
        foreach (var path in new[] { stale, fresh, otherSettingsFile })
            File.WriteAllText(path, "left over from an interrupted save");

        var old = DateTime.UtcNow - AbstractSettings<FanProfileSettings.FanProfileSettingsStore>.StaleTempFileAge - TimeSpan.FromHours(1);
        File.SetLastWriteTimeUtc(stale, old);
        File.SetLastWriteTimeUtc(otherSettingsFile, old);

        var settings = new FanProfileSettings(_dir);
        settings.Store.Enabled = true;
        settings.SynchronizeStore();

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));               // could be a save in progress
        Assert.True(File.Exists(otherSettingsFile));   // belongs to another settings file
        Assert.True(File.Exists(SettingsPath));
    }
}
