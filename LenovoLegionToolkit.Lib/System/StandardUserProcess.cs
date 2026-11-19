using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32.SafeHandles;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>What happened when starting a program as the signed-in user without administrator rights.</summary>
public sealed record StandardUserStart(bool Started, int? ExitCode, string? Refusal)
{
    public static StandardUserStart Refused(string reason) => new(false, null, reason);
}

/// <summary>
/// Starts a program as the signed-in user <b>without</b> administrator rights, although LLT itself runs elevated: it
/// uses the token of the user's desktop shell (Explorer), the same rights the program would have if the user started
/// it. When that isn't possible (no shell, or the shell itself runs as administrator), the program isn't started and
/// the reason is returned; it never falls back to administrator rights.
/// </summary>
public static class StandardUserProcess
{
    public static async Task<StandardUserStart> RunAsync(string program, string? arguments, bool hidden, bool waitForExit,
        IReadOnlyDictionary<string, string?>? environment, CancellationToken token)
    {
        if (!TryGetShellToken(out var shellToken, out var refusal))
            return StandardUserStart.Refused(refusal);

        using (shellToken)
        {
            var envBlock = IntPtr.Zero;
            try
            {
                envBlock = BuildEnvironmentBlock(shellToken, environment);

                var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
                if (hidden)
                {
                    startup.dwFlags = STARTF_USESHOWWINDOW;
                    startup.wShowWindow = SW_HIDE;
                }

                var flags = CREATE_UNICODE_ENVIRONMENT | (hidden ? CREATE_NO_WINDOW : 0);
                var commandLine = new StringBuilder(CommandLine(program, arguments));
                var folder = Path.GetDirectoryName(program.Trim().Trim('"'));
                var workingDirectory = folder is not null && Directory.Exists(folder) ? folder : null;

                if (!CreateProcessWithTokenW(shellToken, 0, null, commandLine, flags, envBlock, workingDirectory, ref startup, out var info))
                    return StandardUserStart.Refused($"Windows refused to start it without administrator rights (error {Marshal.GetLastWin32Error()}).");

                CloseHandle(info.hThread);
                try
                {
                    if (!waitForExit)
                        return new StandardUserStart(true, null, null);

                    // The handle from CreateProcessWithTokenW, so a program that ends at once is still waited for.
                    using var exited = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(info.hProcess, ownsHandle: false) };
                    await WaitAsync(exited, token).ConfigureAwait(false);
                    return new StandardUserStart(true, GetExitCodeProcess(info.hProcess, out var exitCode) ? (int)exitCode : null, null);
                }
                finally
                {
                    CloseHandle(info.hProcess);
                }
            }
            finally
            {
                if (envBlock != IntPtr.Zero)
                    Marshal.FreeHGlobal(envBlock);
            }
        }
    }

    private static Task WaitAsync(WaitHandle handle, CancellationToken token)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => done.TrySetResult(), null, Timeout.Infinite, true);
        var cancellation = token.Register(() => done.TrySetCanceled(token));
        return done.Task.ContinueWith(t =>
        {
            registration.Unregister(null);
            cancellation.Dispose();
            return t;
        }, TaskScheduler.Default).Unwrap();
    }

    /// <summary>The program, quoted unless it already is, then its arguments.</summary>
    internal static string CommandLine(string program, string? arguments)
    {
        var path = program.Trim();
        var quoted = path.StartsWith('"') ? path : $"\"{path}\"";
        return string.IsNullOrWhiteSpace(arguments) ? quoted : $"{quoted} {arguments}";
    }

    /// <summary>Reads a Windows environment block (NAME=value strings, each ended by a null, then a final null).</summary>
    internal static Dictionary<string, string> ParseEnvironmentBlock(string block)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in block.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // Entries like "=C:=C:\" (per-drive folders) start with '='; the name is everything before the next '='.
            var separator = entry.IndexOf('=', 1);
            if (separator > 0)
                result[entry[..separator]] = entry[(separator + 1)..];
        }

        return result;
    }

    /// <summary>The block's text: entries sorted by name (as Windows expects), each ended by a null, then a final null.</summary>
    internal static string FormatEnvironmentBlock(IReadOnlyDictionary<string, string> variables) =>
        string.Concat(variables.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Select(v => $"{v.Key}={v.Value}\0")) + "\0";

    private static bool TryGetShellToken(out SafeAccessTokenHandle token, out string refusal)
    {
        token = new SafeAccessTokenHandle(IntPtr.Zero);
        refusal = string.Empty;

        var shell = GetShellWindow();
        if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out var shellPid) == 0)
        {
            refusal = "No desktop shell (Explorer) is running to take the user's rights from.";
            return false;
        }

        using var shellProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, shellPid);
        if (shellProcess.IsInvalid || !OpenProcessToken(shellProcess, TOKEN_DUPLICATE | TOKEN_QUERY, out var shellToken))
        {
            refusal = $"The desktop shell's rights couldn't be read (error {Marshal.GetLastWin32Error()}).";
            return false;
        }

        using (shellToken)
        {
            using (var identity = new WindowsIdentity(shellToken.DangerousGetHandle()))
            {
                if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    refusal = "The desktop shell itself runs as administrator (User Account Control is off), so there are no standard rights to use.";
                    return false;
                }
            }

            if (!DuplicateTokenEx(shellToken, TOKEN_ALL_ACCESS, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out token))
            {
                refusal = $"The user's rights couldn't be copied (error {Marshal.GetLastWin32Error()}).";
                return false;
            }
        }

        return true;
    }

    private static IntPtr BuildEnvironmentBlock(SafeAccessTokenHandle token, IReadOnlyDictionary<string, string?>? extra)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (CreateEnvironmentBlock(out var userBlock, token, false))
        {
            try
            {
                variables = ParseEnvironmentBlock(ReadBlock(userBlock));
            }
            finally
            {
                DestroyEnvironmentBlock(userBlock);
            }
        }

        foreach (var (name, value) in extra ?? new Dictionary<string, string?>())
        {
            if (value is null)
                variables.Remove(name);
            else
                variables[name] = value;
        }

        return Marshal.StringToHGlobalUni(FormatEnvironmentBlock(variables));
    }

    private static string ReadBlock(IntPtr block)
    {
        var text = new StringBuilder();
        for (var offset = 0; ; offset += 2)
        {
            var c = (char)Marshal.ReadInt16(block, offset);
            if (c == '\0' && text.Length > 0 && text[^1] == '\0')
                return text.ToString();
            text.Append(c);
        }
    }

    // ---- Windows ----

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ALL_ACCESS = 0xF01FF;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const int STARTF_USESHOWWINDOW = 0x00000001;
    private const short SW_HIDE = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle existing, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out SafeAccessTokenHandle newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeAccessTokenHandle token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, uint logonFlags, string? applicationName, StringBuilder commandLine,
        uint creationFlags, IntPtr environment, string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);
}
