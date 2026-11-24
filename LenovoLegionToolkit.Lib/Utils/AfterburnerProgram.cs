using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;

namespace LenovoLegionToolkit.Lib.Utils;

public sealed record AfterburnerStart(int Profile, DateTime Utc);

public enum AfterburnerCheck
{
    /// <summary>MSIAfterburner.exe, validly signed by Micro-Star.</summary>
    Genuine,
    NotFound,
    /// <summary>A file, but not one named MSIAfterburner.exe.</summary>
    NotAfterburner,
    /// <summary>No valid signature (unsigned, changed after signing, or an untrusted certificate).</summary>
    NotSigned,
    /// <summary>Validly signed, but not by Micro-Star.</summary>
    WrongSigner
}

/// <summary>
/// Finds MSI Afterburner and starts it with one of its profiles, but only the genuine program: the file must be named
/// MSIAfterburner.exe and be validly signed by Micro-Star. LLT runs as administrator, so this keeps a replaced file
/// from being started with those rights. The file is locked against changes from the check until it has started.
/// </summary>
public static class AfterburnerProgram
{
    public const string FileName = "MSIAfterburner.exe";
    public const string Signer = "MICRO-STAR INTERNATIONAL CO., LTD.";

    private const string RegistryKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\MSI\Afterburner";

    /// <summary>The installed MSIAfterburner.exe, as Afterburner's installer recorded it; null when it isn't installed.</summary>
    public static string? FindInstalled()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(RegistryKey, "InstallPath", null) is string path && File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read MSI Afterburner's install location.", ex);
            return null;
        }
    }

    public static AfterburnerCheck Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return AfterburnerCheck.NotFound;

        if (!Path.GetFileName(path).Equals(FileName, StringComparison.OrdinalIgnoreCase))
            return AfterburnerCheck.NotAfterburner;

        return Classify(AuthenticodeVerifier.Verify(path), AuthenticodeVerifier.GetSignerOrganization(path));
    }

    /// <summary>Starts the genuine program with <c>-Profile{profile}</c>. Returns why it didn't start otherwise.</summary>
    public static Task<AfterburnerCheck> StartProfileAsync(string? path, int profile, CancellationToken token = default) =>
        StartProfileAsync(path, profile, (file, arguments, t) => CMD.RunAsync(file, arguments, createNoWindow: true, waitForExit: false, token: t), token);

    /// <summary>For tests: <paramref name="start"/> takes the place of starting the program, so a test sees whether it would start.</summary>
    internal static async Task<AfterburnerCheck> StartProfileAsync(string? path, int profile, Func<string, string, CancellationToken, Task> start, CancellationToken token = default)
    {
        if (profile is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(profile), profile, "Afterburner has profiles 1 to 5.");

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return AfterburnerCheck.NotFound;

        // Locked first, so the file that is checked is the file that starts.
        await using var locked = LockedFile.Open(path);

        var check = Check(path);
        if (check != AfterburnerCheck.Genuine)
            return check;

        await start(path, $"-Profile{profile}", token).ConfigureAwait(false);
        LastStarted = new AfterburnerStart(profile, DateTime.UtcNow);
        return AfterburnerCheck.Genuine;
    }

    /// <summary>The profile LLT last applied this run, and when (shown with GPU driver errors).</summary>
    public static AfterburnerStart? LastStarted { get; private set; }

    internal static AfterburnerCheck Classify(int trustResult, string? signerOrganization)
    {
        if (trustResult != 0)
            return AfterburnerCheck.NotSigned;

        return string.Equals(signerOrganization?.Trim(), Signer, StringComparison.OrdinalIgnoreCase)
            ? AfterburnerCheck.Genuine
            : AfterburnerCheck.WrongSigner;
    }
}
