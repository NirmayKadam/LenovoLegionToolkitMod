using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Automation.Utils;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class RunAutomationStep(string? scriptPath, string? scriptArguments, bool? runSilently, bool? waitUntilFinished, bool? checkInstance, bool? runAsStandardUser = null)
    : IAutomationStep
{
    public string? ScriptPath { get; } = scriptPath;

    public string? ScriptArguments { get; } = scriptArguments;

    public bool RunSilently { get; } = runSilently ?? true;

    public bool WaitUntilFinished { get; } = waitUntilFinished ?? false;

    public bool CheckInstance { get; } = checkInstance ?? false;

    /// <summary>
    /// Starts the program with the signed-in user's own rights instead of LLT's administrator rights. New steps have it on;
    /// steps saved before it existed keep running as administrator, so nothing that worked stops working.
    /// </summary>
    public bool RunAsStandardUser { get; } = runAsStandardUser ?? false;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public async Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ScriptPath))
            return;

        if (CheckInstance && IsAlreadyRunning(ScriptPath))
            return;

        if (RunAsStandardUser)
        {
            await RunAsStandardUserAsync(context, environment, token).ConfigureAwait(false);
            return;
        }

        // Warn only, as chosen: the program still runs.
        var target = RunTargetCheck.Check(ScriptPath);
        if (target.Problem != RunTargetProblem.None)
            Log.Instance.Trace($"Run step warning: {target.Problem}. LLT runs this as administrator. [path={target.Path}]");

        var (_, output) = await CMD.RunAsync(ScriptPath,
            ScriptArguments ?? string.Empty,
            RunSilently,
            WaitUntilFinished,
            environment.Dictionary,
            token).ConfigureAwait(false);
        context.LastRunOutput = output.TrimEnd();
    }

    /// <summary>Never falls back to administrator rights: a program that can't be started this way isn't started, and the step fails with the reason.</summary>
    private async Task RunAsStandardUserAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        var result = await StandardUserProcess.RunAsync(ScriptPath!, ScriptArguments, RunSilently, WaitUntilFinished, environment.Dictionary, token).ConfigureAwait(false);
        if (result.Started)
        {
            // The program's output isn't captured in this mode.
            context.LastRunOutput = string.Empty;
            return;
        }

        Log.Instance.Trace($"Run step not started as a standard user: {result.Refusal} [path={ScriptPath}]");
        MessagingCenter.Publish(new NotificationMessage(NotificationType.AutomationNotification, string.Format(Resource.RunStep_NotStartedAsStandardUser, ScriptPath, result.Refusal)));
        throw new InvalidOperationException(result.Refusal);
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

    IAutomationStep IAutomationStep.DeepCopy() => new RunAutomationStep(ScriptPath, ScriptArguments, RunSilently, WaitUntilFinished, CheckInstance, RunAsStandardUser);
}
