using System;
using System.Collections.Generic;
using System.Diagnostics;
using LenovoLegionToolkit.Lib.AutoListeners;
using Xunit;

namespace LenovoLegionToolkit.Tests.AutoListeners;

// The set the game listener keeps running games in. Real child processes are used: a short "cmd /c exit" that
// has finished, which is what a closed game looks like to LLT.
public class ProcessSetTests
{
    private static Process StartAndFinish()
    {
        var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
        process.WaitForExit(10_000);
        return process;
    }

    /// <summary>
    /// The cause of the crash, shown with a set hashed on <c>Process.Id</c> like the old one: once the object
    /// is disposed, looking it up throws the exception from the Windows log.
    /// </summary>
    [Fact]
    public void Control_ASetHashedOnProcessId_ThrowsForADisposedProcess()
    {
        var process = StartAndFinish();
        var old = new HashSet<Process>(new IdComparer()) { process };
        process.Dispose();

        var ex = Assert.Throws<InvalidOperationException>(() => old.Remove(process));
        Assert.Contains("No process is associated", ex.Message);
    }

    [Fact]
    public void DisposedProcess_CanBeLookedUpAndRemoved_WithoutThrowing()
    {
        var process = StartAndFinish();
        var set = new ProcessSet();
        Assert.True(set.Add(process));
        process.Dispose();

        Assert.True(set.Contains(process));
        Assert.True(set.Remove(process));
        Assert.False(set.Contains(process));
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void DisposedProcessThatIsNotInTheSet_IsSimplyNotFound()
    {
        var process = StartAndFinish();
        process.Dispose();
        var set = new ProcessSet();

        Assert.False(set.Contains(process));
        Assert.False(set.Remove(process));
        Assert.False(set.Add(process));   // its id can't be read any more
    }

    [Fact]
    public void SameProcessId_IsAddedOnce_AndAnotherObjectForItIsFound()
    {
        using var mine = Process.GetCurrentProcess();
        using var again = Process.GetCurrentProcess();   // a second object for the same process
        var set = new ProcessSet();

        Assert.True(set.Add(mine));
        Assert.False(set.Add(again));
        Assert.True(set.Contains(again));
        Assert.True(set.Remove(again));
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void Enumerates_WhatWasAdded()
    {
        using var mine = Process.GetCurrentProcess();
        var set = new ProcessSet();
        set.Add(mine);

        Assert.Same(mine, Assert.Single(set));
        set.Clear();
        Assert.Empty(set);
    }

    private sealed class IdComparer : IEqualityComparer<Process>
    {
        public bool Equals(Process? x, Process? y) => ReferenceEquals(x, y) || (x is not null && y is not null && x.Id == y.Id);
        public int GetHashCode(Process obj) => obj.Id;
    }
}
