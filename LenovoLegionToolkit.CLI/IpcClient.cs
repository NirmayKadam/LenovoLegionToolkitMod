using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.CLI.Lib;
using LenovoLegionToolkit.CLI.Lib.Extensions;

namespace LenovoLegionToolkit.CLI;

public static class IpcClient
{
    public static async Task<string> ListQuickActionsAsync()
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.ListQuickActions
        };

        return await SendRequestAsync(req).ConfigureAwait(false)
               ?? throw new IpcException("Missing return message");
    }

    public static Task RunQuickActionAsync(string name)
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.QuickAction,
            Name = name
        };

        return SendRequestAsync(req);
    }

    public static async Task<string> ListFeaturesAsync()
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.ListFeatures,
        };

        return await SendRequestAsync(req).ConfigureAwait(false)
               ?? throw new IpcException("Missing return message");
    }

    public static async Task<string> ListFeatureValuesAsync(string name)
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.ListFeatureValues,
            Name = name,
        };

        return await SendRequestAsync(req).ConfigureAwait(false)
               ?? throw new IpcException("Missing return message");
    }

    public static Task SetFeatureValueAsync(string name, string value)
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.SetFeatureValue,
            Name = name,
            Value = value
        };

        return SendRequestAsync(req);
    }

    public static async Task<string> GetFeatureValueAsync(string name)
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.GetFeatureValue,
            Name = name
        };

        return await SendRequestAsync(req).ConfigureAwait(false)
               ?? throw new IpcException("Missing return message");
    }

    public static async Task<string> GetSpectrumProfileAsync()
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.GetSpectrumProfile
        };

        return await SendRequestAsync(req).ConfigureAwait(false)
               ?? throw new IpcException("Missing return message");
    }

    public static Task SetSpectrumProfileAsync(string value)
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.SetSpectrumProfile,
            Value = value
        };

        return SendRequestAsync(req);
    }

    public static async Task<string> GetSpectrumBrightnessAsync()
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.GetSpectrumBrightness
        };

        return await SendRequestAsync(req).ConfigureAwait(false)
               ?? throw new IpcException("Missing return message");
    }

    public static Task SetSpectrumBrightnessAsync(string value)
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.SetSpectrumBrightness,
            Value = value
        };

        return SendRequestAsync(req);
    }

    public static async Task<string> GetRGBPresetAsync()
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.GetRGBPreset
        };

        return await SendRequestAsync(req).ConfigureAwait(false)
               ?? throw new IpcException("Missing return message");
    }

    public static Task SetRGBPresetAsync(string value)
    {
        var req = new IpcRequest
        {
            Operation = IpcRequest.OperationType.SetRGBPreset,
            Value = value
        };

        return SendRequestAsync(req);
    }

    // Lenovo Legion Toolkit.exe is installed next to llt.exe.
    private static readonly string ExpectedServerPath = Path.Combine(AppContext.BaseDirectory, "Lenovo Legion Toolkit.exe");

    private static async Task<string?> SendRequestAsync(IpcRequest req)
    {
        await using var pipe = new NamedPipeClientStream(Constants.PipeName);

        await ConnectAsync(pipe).ConfigureAwait(false);

        // Refuse to send anything unless the pipe really belongs to Lenovo Legion Toolkit, so a program that
        // grabbed the pipe name first can't receive commands or send back fake results.
        var serverPath = PipeServerIdentity.GetServerProcessPath(pipe);
        if (!PipeServerIdentity.IsExpectedServer(serverPath, ExpectedServerPath))
            throw new IpcException($"The CLI pipe is owned by an unexpected program ({serverPath}); refusing to connect.");

        await pipe.WriteObjectAsync(req).ConfigureAwait(false);
        var res = await pipe.ReadObjectAsync<IpcResponse>().ConfigureAwait(false);

        if (res is null || !res.Success)
            throw new IpcException(res?.Message ?? "Unknown failure");

        return res.Message;
    }

    private static async Task ConnectAsync(NamedPipeClientStream pipe)
    {
        var retries = 3;

        while (retries >= 0)
        {
            try
            {
                await pipe.ConnectAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None).ConfigureAwait(false);
                pipe.ReadMode = PipeTransmissionMode.Message;
                return;
            }
            catch (TimeoutException) { }

            retries--;
        }

        throw new IpcConnectException();
    }
}
