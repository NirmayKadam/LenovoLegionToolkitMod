using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using TaskService = Microsoft.Win32.TaskScheduler.TaskService;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class SoftwareDisablerException(string message, Exception innerException) : Exception(message, innerException);

public abstract class AbstractSoftwareDisabler
{
    public class AbstractSoftwareDisablerEventArgs : EventArgs
    {
        public SoftwareStatus Status { get; init; }
    }

    protected abstract IEnumerable<string> ScheduledTasksPaths { get; }
    protected abstract IEnumerable<string> ServiceNames { get; }
    protected abstract IEnumerable<string> ProcessNames { get; }

    public event EventHandler<AbstractSoftwareDisablerEventArgs>? OnRefreshed;

    public Task<SoftwareStatus> GetStatusAsync() => Task.Run(() =>
    {
        bool isEnabled;
        bool isInstalled;

        try
        {
            var services = RunningServices().ToArray();
            var processes = RunningProcesses().ToArray();

            Log.Instance.Trace($"Running services count: {services.Length}. [type={GetType().Name}, services={string.Join(",", services)}]");
            Log.Instance.Trace($"Running processes count: {processes.Length}. [type={GetType().Name}, processes={string.Join(",", processes)}]");

            isEnabled = services.Length != 0 || processes.Length != 0;
            isInstalled = IsInstalled();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Exception while getting status. [type={GetType().Name}]", ex);

            isEnabled = false;
            isInstalled = false;
        }

        Log.Instance.Trace($"Status: {isEnabled},{isInstalled} [type={GetType().Name}]");

        SoftwareStatus status;

        if (isEnabled)
            status = SoftwareStatus.Enabled;
        else if (!isInstalled)
            status = SoftwareStatus.NotFound;
        else
            status = SoftwareStatus.Disabled;

        OnRefreshed?.Invoke(this, new() { Status = status });

        return status;
    });

    public virtual Task EnableAsync() => Task.Run(async () =>
    {
        Log.Instance.Trace($"Enabling... [type={GetType().Name}]");

        SetScheduledTasksEnabled(true);
        SetServicesEnabled(true);

        _ = await GetStatusAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Enabled [type={GetType().Name}]");
    });

    public virtual Task DisableAsync() => Task.Run(async () =>
    {
        Log.Instance.Trace($"Disabling... [type={GetType().Name}]");

        SetScheduledTasksEnabled(false);
        SetServicesEnabled(false);
        await KillProcessesAsync().ConfigureAwait(false);

        _ = await GetStatusAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Disabled [type={GetType().Name}]");
    });

    /// <summary>How long a service may take to start or stop before this gives up waiting (it used to wait for good).</summary>
    private static readonly TimeSpan ServiceWaitLimit = TimeSpan.FromSeconds(30);

    /// <summary>How long a killed process may take to exit before this moves on.</summary>
    private static readonly TimeSpan ProcessExitLimit = TimeSpan.FromSeconds(10);

    private bool IsInstalled() => WithServices(services => services.Any(s => ServiceNames.Contains(s.ServiceName)));

    private IEnumerable<string> RunningServices() => WithServices(services => ServiceNames.Where(s => IsServiceEnabled(s, services)).ToArray());

    private static bool IsServiceInstalled(string serviceName) => WithServices(services => services.Any(s => s.ServiceName == serviceName));

    /// <summary>Reads the installed services and releases their handles afterwards (they were left to the garbage collector).</summary>
    private static T WithServices<T>(Func<ServiceController[], T> read)
    {
        var services = ServiceController.GetServices();
        try
        {
            return read(services);
        }
        finally
        {
            foreach (var service in services)
                service.Dispose();
        }
    }

    protected virtual IEnumerable<string> RunningProcesses()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                foreach (var processName in ProcessNames)
                {
                    var name = string.Empty;

                    try
                    {
                        name = process.ProcessName;
                        if (!name.StartsWith(processName, StringComparison.InvariantCultureIgnoreCase))
                            continue;
                    }
                    catch (InvalidOperationException) { /* The process exited meanwhile. */ }

                    if (!string.IsNullOrEmpty(name))
                        yield return name;
                }
            }
        }
    }

    private static bool IsServiceEnabled(string serviceName, IEnumerable<ServiceController> services)
    {
        try
        {
            var service = services.FirstOrDefault(s => s.ServiceName == serviceName);
            if (service is null)
                return false;

            return service.Status is not ServiceControllerStatus.Stopped;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void SetScheduledTasksEnabled(bool enabled)
    {
        var taskService = TaskService.Instance;
        foreach (var path in ScheduledTasksPaths)
            SetTasksInFolderEnabled(taskService, path, enabled);
    }

    private void SetTasksInFolderEnabled(TaskService taskService, string path, bool enabled)
    {
        Log.Instance.Trace($"Setting tasks in folder {path} to {enabled}. [type={GetType().Name}]");

        var folder = taskService.GetFolder(path);
        if (folder is null)
        {
            Log.Instance.Trace($"Folder not found [path={path}, type={GetType().Name}]]");

            return;
        }

        foreach (var task in folder.Tasks.ToArray())
        {
            task.Definition.Settings.Enabled = enabled;
            try
            {
                task.RegisterChanges();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to register changes on task {task.Name} in {task.Path}.", ex);

                throw new SoftwareDisablerException($"Failed to register changes on task {task.Name} in {task.Path} [type={GetType().Name}]", ex);
            }
        }
    }

    private void SetServicesEnabled(bool enabled)
    {
        foreach (var serviceName in ServiceNames)
            SetServiceEnabled(serviceName, enabled);
    }

    private void SetServiceEnabled(string serviceName, bool enabled)
    {
        try
        {
            Log.Instance.Trace($"Setting service {serviceName} to {enabled}. [type={GetType().Name}]");

            if (!IsServiceInstalled(serviceName))
            {
                Log.Instance.Trace($"Service {serviceName} not found. [type={GetType().Name}]");

                return;
            }

            using var service = new ServiceController(serviceName);

            try
            {
                Log.Instance.Trace($"Changing service {serviceName} start mode to {enabled}.  [type={GetType().Name}]");

                service.ChangeStartMode(enabled);

                if (enabled)
                {
                    if (service.Status != ServiceControllerStatus.Running)
                    {
                        Log.Instance.Trace($"Starting service {serviceName}... [type={GetType().Name}]");
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running, ServiceWaitLimit);
                    }
                    else
                    {
                        Log.Instance.Trace($"Will not start service {serviceName}. [status={service.Status}, type={GetType().Name}]]");
                    }
                }
                else
                {
                    if (service.CanStop)
                    {
                        Log.Instance.Trace($"Stopping service {serviceName}... [type={GetType().Name}]");
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, ServiceWaitLimit);
                    }
                    else
                    {
                        Log.Instance.Trace($"Will not stop service {serviceName}. [status={service.Status}, canStop={service.CanStop}, type={GetType().Name}]]");
                    }
                }
            }
            finally
            {
                service.Close();
            }
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 1060 })
        {
            Log.Instance.Trace($"Service {serviceName} could not be set to {enabled}");

            throw new SoftwareDisablerException(serviceName, ex);
        }
        catch (global::System.ServiceProcess.TimeoutException ex)
        {
            Log.Instance.Trace($"Service {serviceName} didn't reach its new state within {ServiceWaitLimit.TotalSeconds:0} s. [enabled={enabled}, type={GetType().Name}]");

            throw new SoftwareDisablerException(serviceName, ex);
        }
    }

    protected virtual async Task KillProcessesAsync()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                foreach (var processName in ProcessNames)
                {
                    try
                    {
                        if (process.ProcessName.StartsWith(processName, StringComparison.InvariantCultureIgnoreCase))
                        {
                            process.Kill(true);
                            using var exitWait = new CancellationTokenSource(ProcessExitLimit);
                            await process.WaitForExitAsync(exitWait.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Couldn't kill process.", ex);
                    }
                }
            }
        }
    }
}
