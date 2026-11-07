using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class RunAutomationStep(string? scriptPath, string? scriptArguments, bool? runSilently, bool? waitUntilFinished, bool? checkInstance)
    : IAutomationStep
{
    public string? ScriptPath { get; } = scriptPath;

    public string? ScriptArguments { get; } = scriptArguments;

    public bool RunSilently { get; } = runSilently ?? true;

    public bool WaitUntilFinished { get; } = waitUntilFinished ?? false;

    public bool CheckInstance { get; } = checkInstance ?? false;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public async Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ScriptPath))
            return;

        if (CheckInstance && IsAlreadyRunning(ScriptPath))
            return;

        var (_, output) = await CMD.RunAsync(ScriptPath,
            ScriptArguments ?? string.Empty,
            RunSilently,
            WaitUntilFinished,
            environment.Dictionary,
            token).ConfigureAwait(false);
        context.LastRunOutput = output.TrimEnd();
    }

    private static bool IsAlreadyRunning(string scriptPath)
    {
        var processes = Process.GetProcesses();
        try
        {
            return IsRunning(scriptPath, processes.Select(p => p.ProcessName));
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    /// <summary>
    /// True when one of <paramref name="processNames"/> is the executable named by <paramref name="scriptPath"/>.
    /// Compares the file name without extension, ignoring case (process names never include ".exe").
    /// </summary>
    internal static bool IsRunning(string scriptPath, IEnumerable<string> processNames)
    {
        var executableName = Path.GetFileNameWithoutExtension(scriptPath.Trim().Trim('"'));
        if (string.IsNullOrEmpty(executableName))
            return false;

        return processNames.Any(name => string.Equals(name, executableName, StringComparison.OrdinalIgnoreCase));
    }

    IAutomationStep IAutomationStep.DeepCopy() => new RunAutomationStep(ScriptPath, ScriptArguments, RunSilently, WaitUntilFinished, CheckInstance);
}
