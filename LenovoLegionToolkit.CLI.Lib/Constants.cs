using System.Security.Principal;

namespace LenovoLegionToolkit.CLI.Lib;

public static class Constants
{
    private const string PIPE_NAME_PREFIX = "LenovoLegionToolkit-IPC-0";

    /// <summary>
    /// One pipe per user (the elevated app and the normal CLI of the same account share the user SID),
    /// so another user's session can neither collide with nor impersonate this user's LLT.
    /// </summary>
    public static string PipeName => PipeNameFor(WindowsIdentity.GetCurrent().User);

    internal static string PipeNameFor(SecurityIdentifier? user) =>
        user is null ? PIPE_NAME_PREFIX : $"{PIPE_NAME_PREFIX}-{user.Value}";
}
