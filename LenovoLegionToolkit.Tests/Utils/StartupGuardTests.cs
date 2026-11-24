using System;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>
/// The start-up note behind safe start. Real files in a temporary folder: a start that never finished (a crash, a
/// frozen start ended in Task Manager) leaves the note behind, and the next start sees it.
/// </summary>
public sealed class StartupGuardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-startup-guard-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Fact]
    public void TheFirstStart_IsNormal()
    {
        Assert.False(new StartupGuard(_dir).Begin("2.36.18.0"));
    }

    [Fact]
    public void AStartThatNeverFinished_IsNoticedByTheNext()
    {
        var before = DateTime.Now;
        new StartupGuard(_dir).Begin("2.36.18.0");   // crashed: Complete never ran

        var next = new StartupGuard(_dir);
        Assert.True(next.Begin("2.36.18.0"));
        Assert.NotNull(next.UnfinishedStartedAt);
        Assert.True(next.UnfinishedStartedAt >= before.AddSeconds(-1));
    }

    [Fact]
    public void AFinishedStart_MakesTheNextOneNormal()
    {
        var first = new StartupGuard(_dir);
        first.Begin("2.36.18.0");
        first.Complete();

        Assert.False(new StartupGuard(_dir).Begin("2.36.18.0"));
    }

    [Fact]
    public void AfterASafeStartThatFinished_TheNextStartIsNormalAgain()
    {
        new StartupGuard(_dir).Begin("2.36.18.0");
        var safe = new StartupGuard(_dir);
        Assert.True(safe.Begin("2.36.18.0"));
        safe.Complete();

        Assert.False(new StartupGuard(_dir).Begin("2.36.18.0"));
    }

    [Fact]
    public void ANoteThatCantBeRead_NeverBlocksAStart()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, StartupGuard.FileName);
        File.WriteAllText(path, "not a date");

        // Held open without sharing, so it can be neither read nor rewritten.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var guard = new StartupGuard(_dir);
            Assert.False(guard.Begin("2.36.18.0"));
            guard.Complete();   // can't delete it either; must not throw
        }
    }

    [Fact]
    public void ANoteWithoutATime_StillCountsAsUnfinished()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, StartupGuard.FileName), "garbled");

        var guard = new StartupGuard(_dir);
        Assert.True(guard.Begin("2.36.18.0"));
        Assert.Null(guard.UnfinishedStartedAt);
    }
}
