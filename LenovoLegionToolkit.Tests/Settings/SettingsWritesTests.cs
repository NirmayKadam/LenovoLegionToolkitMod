using System;
using System.IO;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.Settings;

/// <summary>
/// After a settings restore or reset, nothing in the running Indra may write the settings files before the restart
/// (the OSD used to save its old settings over restored ones as it closed). Real settings files in temporary
/// folders; freezing one folder never affects another.
/// </summary>
public sealed class SettingsWritesTests : IDisposable
{
    private readonly string _frozen = Path.Combine(Path.GetTempPath(), "llt-settings-frozen-" + Guid.NewGuid().ToString("N"));
    private readonly string _other = Path.Combine(Path.GetTempPath(), "llt-settings-other-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SettingsWrites.Thaw(_frozen);
        foreach (var dir in new[] { _frozen, _other })
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void AFrozenFolder_KeepsItsFilesExactlyAsTheyAre()
    {
        var settings = new GameProfileSettings(_frozen);
        settings.SynchronizeStore();
        var before = File.ReadAllBytes(settings.FilePath);

        SettingsWrites.Freeze(_frozen);
        settings.Store.Profiles.Add(new GameProfile { Name = "Written after the restore", ExeName = "game" });
        settings.SynchronizeStore();
        settings.Save();

        Assert.Equal(before, File.ReadAllBytes(settings.FilePath));
    }

    [Fact]
    public void AFileDeletedByAReset_IsNotWrittenBack()
    {
        var settings = new GameProfileSettings(_frozen);
        settings.SynchronizeStore();

        SettingsWrites.Freeze(_frozen);
        File.Delete(settings.FilePath);
        settings.SynchronizeStore();

        Assert.False(File.Exists(settings.FilePath));
    }

    [Fact]
    public void OtherFolders_AreStillWritten()
    {
        SettingsWrites.Freeze(_frozen);

        var other = new GameProfileSettings(_other);
        other.SynchronizeStore();

        Assert.True(File.Exists(other.FilePath));
    }

    [Fact]
    public void AfterARestoreThatDidntGoAhead_WritesWorkAgain()
    {
        var settings = new GameProfileSettings(_frozen);
        settings.SynchronizeStore();
        var before = File.ReadAllBytes(settings.FilePath);

        SettingsWrites.Freeze(_frozen);
        SettingsWrites.Thaw(_frozen);
        settings.Store.Profiles.Add(new GameProfile { Name = "Saved normally", ExeName = "game" });
        settings.SynchronizeStore();

        Assert.NotEqual(before, File.ReadAllBytes(settings.FilePath));
    }

    [Fact]
    public void TheGameProfileSessionNote_IsStillWrittenWhileFrozen()
    {
        // The shutdown after a restore ends the game profile's session; the note must then say nothing is left to
        // put back, or the next start puts it back a second time.
        var session = new GameProfileSessionSettings(_frozen);
        session.Store.ProfileId = Guid.NewGuid();
        session.SynchronizeStore();

        SettingsWrites.Freeze(_frozen);
        session.Store.ProfileId = null;
        session.SynchronizeStore();

        Assert.Null(new GameProfileSessionSettings(_frozen).Store.ProfileId);
    }

    [Fact]
    public void TheBackgroundServicesNote_IsStillWrittenWhileFrozen()
    {
        var session = new BackgroundServicesSessionSettings(_frozen);
        session.Store.StoppedServices.Add("SysMain");
        session.SynchronizeStore();

        SettingsWrites.Freeze(_frozen);
        session.Store.StoppedServices.Clear();
        session.SynchronizeStore();

        Assert.Empty(new BackgroundServicesSessionSettings(_frozen).Store.StoppedServices);
    }

    [Fact]
    public void BesideTheSessionNotes_SettingsInTheSameFolderStayFrozen()
    {
        var settings = new GameProfileSettings(_frozen);
        settings.SynchronizeStore();
        var before = File.ReadAllBytes(settings.FilePath);

        SettingsWrites.Freeze(_frozen);
        new GameProfileSessionSettings(_frozen).SynchronizeStore();
        settings.Store.Profiles.Add(new GameProfile { Name = "Written after the restore", ExeName = "game" });
        settings.SynchronizeStore();

        Assert.Equal(before, File.ReadAllBytes(settings.FilePath));
    }
}
