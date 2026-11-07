using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace LenovoLegionToolkit.CLI.Lib;

/// <summary>
/// Lets the CLI check who is on the other end of the pipe before sending commands, so a program that
/// created the pipe name first can't pose as Lenovo Legion Toolkit.
/// </summary>
public static class PipeServerIdentity
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Full path of the executable of the process that created the pipe (the server).
    /// Works for an elevated server too, because only limited query rights are needed.
    /// </summary>
    public static string GetServerProcessPath(PipeStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var buffer = new StringBuilder(32768);
            var size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(process, 0, buffer, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return buffer.ToString(0, (int)size);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>
    /// True when <paramref name="serverPath"/> is the expected executable (compared as full paths, ignoring case).
    /// </summary>
    public static bool IsExpectedServer(string? serverPath, string expectedPath) =>
        !string.IsNullOrWhiteSpace(serverPath)
        && string.Equals(Path.GetFullPath(serverPath), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Delay before retrying to create the server pipe after <paramref name="failures"/> consecutive failures:
    /// 1 s, 2 s, 4 s ... capped at 30 s.
    /// </summary>
    public static TimeSpan CreationBackoff(int failures) =>
        TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Max(0, failures - 1))));
}
