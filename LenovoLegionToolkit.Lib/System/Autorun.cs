using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32.TaskScheduler;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// Thrown when autostart is requested for an LLT copy in a folder normal programs can write to.
/// The autostart task runs with the highest privileges at logon, so whoever can replace that exe
/// would get administrator rights.
/// </summary>
public class UnprotectedAutorunLocationException(string path)
    : Exception($"Autostart is only allowed from a folder that only administrators can change: {path}")
{
    public string LaunchTarget { get; } = path;
}

public static class Autorun
{
    private const string TASK_NAME = "LenovoLegionToolkit_Autorun_6efcc882-924c-4cbc-8fec-f45c25696f98";

    public static AutorunState State
    {
        get
        {
            var task = TaskService.Instance.GetTask(TASK_NAME);
            if (task is null)
                return AutorunState.Disabled;

            var delayed = task.Definition.Triggers.OfType<LogonTrigger>().FirstOrDefault()?.Delay > TimeSpan.Zero;
            return delayed ? AutorunState.EnabledDelayed : AutorunState.Enabled;
        }
    }

    public static void Validate()
    {
        Log.Instance.Trace($"Validating autorun...");

        var currentTask = TaskService.Instance.GetTask(TASK_NAME);
        if (currentTask is null)
        {
            Log.Instance.Trace($"Autorun is not enabled.");
            return;
        }

        // An existing task that starts an exe from a writable folder is removed: anyone able to replace that
        // exe would be started with the highest privileges at the next logon.
        var taskTarget = GetTargetFromTaskData(currentTask.Definition.Data);
        if (taskTarget is not null && !IsLaunchLocationProtected(taskTarget))
        {
            Log.Instance.Trace($"Autorun task points to an unprotected location, disabling it. [target={taskTarget}]");
            Disable();
            return;
        }

        // A copy running from a writable folder (for example a portable copy) must not re-point the task to itself.
        if (!IsLaunchLocationProtected(GetLaunchTarget()))
        {
            Log.Instance.Trace($"Running from an unprotected location; leaving the autorun task unchanged. [path={GetLaunchTarget()}]");
            return;
        }

        var mainModule = Process.GetCurrentProcess().MainModule;
        if (mainModule is null)
        {
            Log.Instance.Trace($"Main module is null.");
            return;
        }

        var fileVersion = mainModule.FileVersionInfo.FileVersion;
        if (fileVersion is null)
        {
            Log.Instance.Trace($"File version is null.");
            return;
        }

        var launchTarget = GetLaunchTarget();
        var taskData = BuildTaskData(fileVersion, launchTarget);

        if (string.Equals(currentTask.Definition.Data, taskData, StringComparison.OrdinalIgnoreCase))
        {
            Log.Instance.Trace($"Autorun settings seems to be fine.");
            return;
        }

        Log.Instance.Trace($"Autorun settings mismatch. Task Data: '{currentTask.Definition.Data}', Current Data: '{taskData}'. Re-enabling...");

        var delayed = currentTask.Definition.Triggers.OfType<LogonTrigger>().FirstOrDefault()?.Delay > TimeSpan.Zero;

        Enable(delayed);
    }

    public static void Set(AutorunState state)
    {
        if (state == AutorunState.Disabled)
            Disable();
        else
            Enable(state == AutorunState.EnabledDelayed);
    }

    private static void Enable(bool delayed)
    {
        var target = GetLaunchTarget();
        if (!IsLaunchLocationProtected(target))
            throw new UnprotectedAutorunLocationException(target);

        Disable();

        var mainModule = Process.GetCurrentProcess().MainModule ?? throw new InvalidOperationException("Main Module cannot be null");
        var fileVersion = mainModule.FileVersionInfo.FileVersion ?? throw new InvalidOperationException("Current process file version cannot be null");
        var currentUser = WindowsIdentity.GetCurrent().Name;
        var launchTarget = GetLaunchTarget();

        var ts = TaskService.Instance;
        var td = ts.NewTask();
        td.Settings.Compatibility = TaskCompatibility.V2_3;
        td.Data = BuildTaskData(fileVersion, launchTarget);
        td.Principal.UserId = currentUser;
        td.Principal.RunLevel = IsUacDisabledOrBuiltInAdmin() ? TaskRunLevel.LUA : TaskRunLevel.Highest;
        td.Triggers.Add(new LogonTrigger { UserId = currentUser, Delay = new TimeSpan(0, 0, delayed ? 30 : 0) });

        var action = new ExecAction("rundll32.exe", $"shell32.dll,ShellExec_RunDLL \"{launchTarget}\" --minimized", Path.GetDirectoryName(launchTarget));
        td.Actions.Add(action);

        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        ts.RootFolder.RegisterTaskDefinition(TASK_NAME, td);

        Log.Instance.Trace($"Autorun enabled");
    }

    private static void Disable()
    {
        try
        {
            TaskService.Instance.RootFolder.DeleteTask(TASK_NAME);

            Log.Instance.Trace($"Autorun disabled");
        }
        catch
        {
            Log.Instance.Trace($"Autorun was not enabled");
        }
    }

    private static string BuildTaskData(string fileVersion, string launchTarget) => $"{fileVersion}|{launchTarget}";

    /// <summary>
    /// The exe path stored in the task data ("version|path"), or null if the data has no path.
    /// </summary>
    internal static string? GetTargetFromTaskData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return null;

        var separator = data.IndexOf('|');
        if (separator < 0 || separator == data.Length - 1)
            return null;

        return data[(separator + 1)..];
    }

    /// <summary>
    /// True when the folder of <paramref name="launchTarget"/> can only be changed by administrators.
    /// A missing folder or an unreadable ACL counts as unprotected.
    /// </summary>
    internal static bool IsLaunchLocationProtected(string launchTarget)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(launchTarget));
            return !string.IsNullOrEmpty(directory) && !DirectoryProtection.IsWritableByNonAdmins(directory);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Could not check autorun location {launchTarget}.", ex);
            return false;
        }
    }

    private static string GetLaunchTarget()
    {
        var filename = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path cannot be null");
        return filename;
    }

    private static bool IsUacDisabledOrBuiltInAdmin()
    {
        var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.IsWellKnown(WellKnownSidType.AccountAdministratorSid) == true)
        {
            Log.Instance.Trace($"Detected Built-in Administrator account. Downgrading Task Scheduler to LUA RunLevel.");
            return true;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            if (key?.GetValue("EnableLUA") is int enableLua && enableLua == 0)
            {
                Log.Instance.Trace($"Detected globally disabled UAC (EnableLUA=0). Downgrading Task Scheduler to LUA RunLevel.");
                return true;
            }
        }
        catch { /* Ignore */ }

        return false;
    }
}
