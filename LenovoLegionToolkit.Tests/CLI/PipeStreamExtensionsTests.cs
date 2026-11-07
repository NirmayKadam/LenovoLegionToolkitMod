using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.CLI.Lib;
using LenovoLegionToolkit.CLI.Lib.Extensions;
using Xunit;

namespace LenovoLegionToolkit.Tests.CLI;

// Uses a real Message-mode named pipe pair, the same transport the CLI uses.
public class PipeStreamExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static async Task<(NamedPipeServerStream Server, NamedPipeClientStream Client)> ConnectAsync(CancellationToken token)
    {
        var name = $"llt-test-{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);

        var waitTask = server.WaitForConnectionAsync(token);
        await client.ConnectAsync(token);
        await waitTask;
        client.ReadMode = PipeTransmissionMode.Message;

        return (server, client);
    }

    // The pipes have no buffer, so a write completes only while the other end reads.
    // Run them concurrently, as the CLI and LLT processes do.
    private static async Task<T?> WriteAndReadAsync<T>(PipeStream writer, PipeStream reader, T value, CancellationToken token)
    {
        var writeTask = writer.WriteObjectAsync(value, token);
        var result = await reader.ReadObjectAsync<T>(token);
        await writeTask;
        return result;
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1024)]     // exactly one read buffer
    [InlineData(1500)]     // two chunks: the old code appended stale bytes here
    [InlineData(5 * 1024)]
    [InlineData(200 * 1024)]
    public async Task Response_OfAnySize_RoundTrips(int messageLength)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var (server, client) = await ConnectAsync(cts.Token);
        await using var _ = server;
        await using var __ = client;

        var expected = new IpcResponse { Success = true, Message = new string('a', messageLength) };

        var actual = await WriteAndReadAsync(server, client, expected, cts.Token);

        Assert.NotNull(actual);
        Assert.True(actual.Success);
        Assert.Equal(expected.Message, actual.Message);
    }

    [Fact]
    public async Task MultiByteCharacters_SplitAcrossChunks_RoundTrip()
    {
        // 3-byte UTF-8 characters; with 1024-byte chunks some characters straddle a chunk boundary.
        using var cts = new CancellationTokenSource(Timeout);
        var (server, client) = await ConnectAsync(cts.Token);
        await using var _ = server;
        await using var __ = client;

        var names = string.Join('\n', Enumerable.Range(1, 200).Select(i => $"游戏模式 {i} 安静模式"));
        var expected = new IpcResponse { Success = true, Message = names };

        var actual = await WriteAndReadAsync(server, client, expected, cts.Token);

        Assert.NotNull(actual);
        Assert.Equal(names, actual.Message);
    }

    [Fact]
    public async Task Request_FromClient_RoundTrips()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var (server, client) = await ConnectAsync(cts.Token);
        await using var _ = server;
        await using var __ = client;

        var request = new IpcRequest { Operation = IpcRequest.OperationType.SetFeatureValue, Name = "PowerModeFeature", Value = "performance" };

        var actual = await WriteAndReadAsync(client, server, request, cts.Token);

        Assert.NotNull(actual);
        Assert.Equal(request.Operation, actual.Operation);
        Assert.Equal(request.Name, actual.Name);
        Assert.Equal(request.Value, actual.Value);
    }

    [Fact]
    public async Task ClientDisconnects_BeforeSending_ThrowsInsteadOfHanging()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var (server, client) = await ConnectAsync(cts.Token);
        await using var _ = server;

        await client.DisposeAsync();

        await Assert.ThrowsAsync<IOException>(() => server.ReadObjectAsync<IpcRequest>(cts.Token));
        Assert.False(cts.IsCancellationRequested, "Read should fail on its own, not by hitting the test timeout.");
    }

    [Fact]
    public async Task Message_OverLimit_IsRejected()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var (server, client) = await ConnectAsync(cts.Token);
        await using var _ = server;
        await using var __ = client;

        var huge = new IpcResponse { Success = true, Message = new string('a', PipeStreamExtensions.MaxMessageBytes + 10) };

        var writeTask = server.WriteObjectAsync(huge, cts.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.ReadObjectAsync<IpcResponse>(cts.Token));
    }
}
