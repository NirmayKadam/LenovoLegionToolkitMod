using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

public static class CMD
{
    /// <param name="endOnCancel">
    /// End the program when <paramref name="token"/> cancels the wait. Off by default: a Run step's program keeps
    /// running when its rule is cancelled, as it always has.
    /// </param>
    public static async Task<(int, string)> RunAsync(string file, string arguments, bool createNoWindow = true, bool waitForExit = true, Dictionary<string, string?>? environment = null, CancellationToken token = default, bool endOnCancel = false)
    {
        Log.Instance.Trace($"Running... [file={file}, argument={arguments}, createNoWindow={createNoWindow}, waitForExit={waitForExit}, environment=[{(environment is null ? string.Empty : string.Join(",", environment))}]");

        // Output is only captured when we wait for the process. Redirecting without reading
        // would block the child once the pipe buffer (~4 KB) fills up.
        var redirect = createNoWindow && waitForExit;

        using var cmd = new Process();
        cmd.StartInfo.UseShellExecute = false;
        cmd.StartInfo.CreateNoWindow = createNoWindow;
        cmd.StartInfo.RedirectStandardOutput = redirect;
        cmd.StartInfo.RedirectStandardError = redirect;
        cmd.StartInfo.WindowStyle = createNoWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal;
        cmd.StartInfo.FileName = file;
        if (!string.IsNullOrWhiteSpace(arguments))
            cmd.StartInfo.Arguments = arguments;

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                cmd.StartInfo.Environment[key] = value;
        }

        cmd.Start();

        if (!waitForExit)
        {
            Log.Instance.Trace($"Ran [file={file}, argument={arguments}, createNoWindow={createNoWindow}, waitForExit={waitForExit}, environment=[{(environment is null ? string.Empty : string.Join(",", environment))}]");

            return (-1, string.Empty);
        }

        var output = string.Empty;
        var error = string.Empty;

        try
        {
            if (redirect)
            {
                // Read both streams while the process runs; reading only after exit deadlocks
                // as soon as the child writes more than the pipe buffer can hold.
                var outputTask = cmd.StandardOutput.ReadToEndAsync(token);
                var errorTask = cmd.StandardError.ReadToEndAsync(token);

                await Task.WhenAll(outputTask, errorTask, cmd.WaitForExitAsync(token)).ConfigureAwait(false);

                output = await outputTask.ConfigureAwait(false);
                error = await errorTask.ConfigureAwait(false);
            }
            else
            {
                await cmd.WaitForExitAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && endOnCancel)
        {
            // The caller stopped waiting and asked for the program not to be left running behind its back.
            try
            {
                if (!cmd.HasExited)
                    cmd.Kill(true);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't end {file} after the wait was cancelled.", ex);
            }

            Log.Instance.Trace($"Stopped waiting for {file}. [argument={arguments}]");
            throw;
        }

        var exitCode = cmd.ExitCode;

        Log.Instance.Trace($"Ran [file={file}, argument={arguments}, createNoWindow={createNoWindow}, waitForExit={waitForExit}, exitCode={exitCode} output={output} error={error}]");

        return (exitCode, output);
    }
}
