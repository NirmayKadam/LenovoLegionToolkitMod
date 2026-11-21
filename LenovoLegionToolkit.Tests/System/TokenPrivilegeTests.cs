using System;
using System.Runtime.InteropServices;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.Tests.Performance;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

/// <summary>
/// Privileges Indra turns on for an operation (UEFI variables, registry ownership) are turned off again afterwards, and
/// not while another operation still needs them. Real Windows: uses the shutdown privilege, which every account has
/// (off by default) and which does nothing by being on. Read back from this process's own token. Runs alone, since
/// the token is shared by the whole test process.
/// </summary>
[Collection(BudgetCollection.Name)]
public class TokenPrivilegeTests
{
    private const string Privilege = "SeShutdownPrivilege";

    [Fact]
    public void APrivilegeTurnedOnForAnOperation_IsTurnedOffAfterwards()
    {
        Assert.False(IsEnabled(), "the shutdown privilege was already on before the test");

        Assert.True(TokenManipulator.AddPrivileges(Privilege));
        Assert.True(IsEnabled());

        Assert.True(TokenManipulator.RemovePrivileges(Privilege));
        Assert.False(IsEnabled());
    }

    [Fact]
    public void APrivilegeStaysOn_WhileAnotherOperationStillHoldsIt()
    {
        TokenManipulator.AddPrivileges(Privilege);
        TokenManipulator.AddPrivileges(Privilege);

        TokenManipulator.RemovePrivileges(Privilege);
        Assert.True(IsEnabled());

        TokenManipulator.RemovePrivileges(Privilege);
        Assert.False(IsEnabled());
    }

    // ---- Reading this process's token ----

    private const uint TokenQuery = 0x0008;
    private const int TokenPrivilegesClass = 3;
    private const uint SePrivilegeEnabled = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private static bool IsEnabled()
    {
        Assert.True(LookupPrivilegeValue(null, Privilege, out var wanted));
        Assert.True(OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token), $"OpenProcessToken failed ({Marshal.GetLastWin32Error()})");
        try
        {
            GetTokenInformation(token, TokenPrivilegesClass, IntPtr.Zero, 0, out var length);
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                Assert.True(GetTokenInformation(token, TokenPrivilegesClass, buffer, length, out _), $"GetTokenInformation failed ({Marshal.GetLastWin32Error()})");

                // TOKEN_PRIVILEGES: a count, then that many (LUID, attributes) pairs of 12 bytes each.
                var count = Marshal.ReadInt32(buffer);
                for (var i = 0; i < count; i++)
                {
                    var entry = buffer + 4 + i * 12;
                    var luid = Marshal.PtrToStructure<Luid>(entry);
                    if (luid.LowPart != wanted.LowPart || luid.HighPart != wanted.HighPart)
                        continue;

                    return (unchecked((uint)Marshal.ReadInt32(entry + 8)) & SePrivilegeEnabled) != 0;
                }

                Assert.Fail($"This account's token has no {Privilege}.");
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
