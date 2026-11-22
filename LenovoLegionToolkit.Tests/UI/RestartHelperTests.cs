using System;
using System.Diagnostics;
using System.IO;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The copy started by a settings restore or reset waits for the one it replaces. Real processes: a cmd.exe that
/// ends after about two seconds stands in for the old copy shutting down.
/// </summary>
public class RestartHelperTests
{
    [Fact]
    public void TheReplacedProcess_IsReadFromTheArguments() =>
        Assert.Equal(1234, RestartHelper.ParseAfterRestartPid(["--minimized", "--after-restart=1234"]));

    [Theory]
    [InlineData("--minimized")]
    [InlineData("--after-restart=")]
    [InlineData("--after-restart=abc")]
    [InlineData("--after-restart=-5")]
    public void WithoutAUsableProcessId_ThereIsNothingToWaitFor(string argument) =>
        Assert.Null(RestartHelper.ParseAfterRestartPid([argument]));

    [Fact]
    public void TheNewCopy_IsToldWhichProcessItReplaces()
    {
        var info = RestartHelper.StartInfo(@"C:\Program Files\Indra\Lenovo Legion Toolkit.exe", 4321);

        Assert.Equal(@"C:\Program Files\Indra\Lenovo Legion Toolkit.exe", info.FileName);
        Assert.True(info.UseShellExecute);
        Assert.Equal(4321, RestartHelper.ParseAfterRestartPid([info.Arguments]));
    }

    [Fact]
    public void WhenTheNewCopyCantBeStarted_ItSaysSo_InsteadOfThrowing()
    {
        // A real Windows failure: the program isn't there. Nothing is started and no dialog is shown.
        var missing = Path.Combine(Path.GetTempPath(), "llt-restart-missing-" + Guid.NewGuid().ToString("N"), "Lenovo Legion Toolkit.exe");

        Assert.False(RestartHelper.TryStartNewCopy(missing, Environment.ProcessId));
    }

    private static Process StartShortLived() => Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 > nul")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    })!;

    [Fact]
    public void TheNewCopy_WaitsUntilTheOldOneHasEnded()
    {
        using var old = StartShortLived();
        var watch = Stopwatch.StartNew();

        Assert.True(RestartHelper.WaitForExit(old.Id, "cmd", TimeSpan.FromSeconds(30)));
        Assert.True(old.HasExited);
        Assert.True(watch.Elapsed > TimeSpan.FromSeconds(1), $"returned after {watch.ElapsedMilliseconds} ms, before the process ended");
    }

    [Fact]
    public void AnOldCopyStillRunningAfterTheLimit_IsReported()
    {
        using var old = StartShortLived();
        try
        {
            Assert.False(RestartHelper.WaitForExit(old.Id, "cmd", TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            old.Kill(true);
        }
    }

    [Fact]
    public void AnIdNowUsedByAnotherProgram_IsNotWaitedFor()
    {
        using var other = StartShortLived();
        try
        {
            var watch = Stopwatch.StartNew();
            Assert.True(RestartHelper.WaitForExit(other.Id, "Lenovo Legion Toolkit", TimeSpan.FromSeconds(30)));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
        }
        finally
        {
            other.Kill(true);
        }
    }

    [Fact]
    public void AnOldCopyAlreadyGone_IsNotWaitedFor()
    {
        using var old = StartShortLived();
        old.WaitForExit();
        var id = old.Id;

        Assert.True(RestartHelper.WaitForExit(id, "cmd", TimeSpan.FromSeconds(30)));
    }
}
