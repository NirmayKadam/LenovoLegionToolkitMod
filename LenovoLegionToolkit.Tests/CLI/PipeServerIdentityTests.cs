using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.CLI.Lib;
using Xunit;

namespace LenovoLegionToolkit.Tests.CLI;

// Uses a real named pipe served by this test process.
public class PipeServerIdentityTests
{
    [Fact]
    public void PipeName_IsPerUser()
    {
        var user = WindowsIdentity.GetCurrent().User!;
        Assert.Equal($"LenovoLegionToolkit-IPC-0-{user.Value}", Constants.PipeName);
        Assert.NotEqual(Constants.PipeNameFor(user), Constants.PipeNameFor(new SecurityIdentifier("S-1-5-21-1-2-3-1001")));
    }

    [Fact]
    public async Task ServerProcessPath_IsTheProcessThatCreatedThePipe_AndIsNotAcceptedAsLlt()
    {
        var name = $"llt-test-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var wait = server.WaitForConnectionAsync(cts.Token);
        await client.ConnectAsync(cts.Token);
        await wait;

        var serverPath = PipeServerIdentity.GetServerProcessPath(client);

        // The test host created the pipe, so that's who the client sees...
        Assert.True(PipeServerIdentity.IsExpectedServer(serverPath, Environment.ProcessPath!), $"Got {serverPath}, expected {Environment.ProcessPath}");

        // ...and the CLI would refuse it, because it isn't Lenovo Legion Toolkit.exe next to llt.exe.
        var expectedLlt = Path.Combine(AppContext.BaseDirectory, "Lenovo Legion Toolkit.exe");
        Assert.False(PipeServerIdentity.IsExpectedServer(serverPath, expectedLlt));
    }

    [Theory]
    [InlineData(@"C:\Program Files\LenovoLegionToolkit\Lenovo Legion Toolkit.exe", @"C:\Program Files\LenovoLegionToolkit\Lenovo Legion Toolkit.exe", true)]
    [InlineData(@"c:\program files\lenovolegiontoolkit\LENOVO LEGION TOOLKIT.EXE", @"C:\Program Files\LenovoLegionToolkit\Lenovo Legion Toolkit.exe", true)]
    [InlineData(@"C:\Users\Public\Lenovo Legion Toolkit.exe", @"C:\Program Files\LenovoLegionToolkit\Lenovo Legion Toolkit.exe", false)]
    [InlineData(null, @"C:\Program Files\LenovoLegionToolkit\Lenovo Legion Toolkit.exe", false)]
    [InlineData("", @"C:\Program Files\LenovoLegionToolkit\Lenovo Legion Toolkit.exe", false)]
    public void ExpectedServer_ComparesFullPaths(string? serverPath, string expected, bool result)
    {
        Assert.Equal(result, PipeServerIdentity.IsExpectedServer(serverPath, expected));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(50, 30)]
    public void CreationBackoff_GrowsAndIsCapped(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), PipeServerIdentity.CreationBackoff(failures));
    }

    [Fact]
    public async Task FirstPipeInstance_FailsWhenNameIsAlreadyOwned()
    {
        // What the server relies on: with FirstPipeInstance it can't silently join a pipe someone else created.
        var name = $"llt-test-{Guid.NewGuid():N}";
        await using var squatter = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous);

        Assert.ThrowsAny<Exception>(() =>
            new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance).Dispose());
    }
}
