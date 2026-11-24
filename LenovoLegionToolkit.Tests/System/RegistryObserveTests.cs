using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using IndraRegistry = LenovoLegionToolkit.Lib.System.Registry;
using WinRegistry = Microsoft.Win32.Registry;

namespace LenovoLegionToolkit.Tests.System;

/// <summary>
/// Watching a registry key (game detection watches Windows' list of known games this way), on a real key made for the
/// test under the current user's Software key and removed afterwards.
/// </summary>
public sealed class RegistryObserveTests : IDisposable
{
    private readonly string _subKey = $@"Software\LLT-Tests\observe-{Guid.NewGuid():N}";

    public RegistryObserveTests() => WinRegistry.CurrentUser.CreateSubKey(_subKey)!.Dispose();

    public void Dispose()
    {
        WinRegistry.CurrentUser.DeleteSubKeyTree(_subKey, false);
        using var parent = WinRegistry.CurrentUser.OpenSubKey(@"Software\LLT-Tests", true);
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
            WinRegistry.CurrentUser.DeleteSubKey(@"Software\LLT-Tests", false);
    }

    private void Write(int value)
    {
        using var key = WinRegistry.CurrentUser.OpenSubKey(_subKey, true)!;
        key.SetValue("Value", value);
    }

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
                return false;
            await Task.Delay(20);
        }
        return true;
    }

    [Fact]
    public async Task AListenerThatFails_DoesNotEndTheWatch()
    {
        var calls = 0;
        await using var watch = IndraRegistry.ObserveKey("HKCU", _subKey, false, () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("the first change fails to be handled");
        });
        await Task.Delay(200);   // the watch registers on its own thread

        Write(1);
        Assert.True(await EventuallyAsync(() => Volatile.Read(ref calls) >= 1), "The first change was never seen.");
        await Task.Delay(200);   // re-registered after the failed handler

        Write(2);
        Assert.True(await EventuallyAsync(() => Volatile.Read(ref calls) >= 2), "After one failed handler, later changes were no longer seen.");
    }

    [Fact]
    public void Values_AreRead_AndAMissingKeyGivesTheDefault()
    {
        Write(7);

        Assert.Equal(7, IndraRegistry.GetValue("HKCU", _subKey, "Value", -1));
        Assert.Equal(-1, IndraRegistry.GetValue("HKCU", _subKey + @"\missing", "Value", -1));
        Assert.Empty(IndraRegistry.GetSubKeys("HKCU", _subKey));
    }
}
