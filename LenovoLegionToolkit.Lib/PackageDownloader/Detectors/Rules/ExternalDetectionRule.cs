using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.PackageDownloader.Detectors.Rules;

internal readonly struct ExternalDetectionRule : IPackageRule
{
    private const string TEMP_FOLDER_SUB_FOLDER = "external_package_detection";

    private int[] ReturnCodes { get; init; }
    private string Command { get; init; }
    private string Url { get; init; }
    private string FileName { get; init; }
    private string PackageName { get; init; }

    public static bool TryCreate(XmlNode? node, XmlDocument document, string baseLocation, out ExternalDetectionRule value)
    {
        var command = node?.InnerText;
        var returnCodes = node?.Attributes?.OfType<XmlAttribute>()
            .FirstOrDefault(a => a.Name == "rc")?
            .InnerText
            .Split(",")
            .Select(s => int.TryParse(s, out var result) ? result : -1)
            .Where(i => i >= 0)
            .Distinct()
            .ToArray() ?? [];
        var externalFile = document.SelectSingleNode("/Package/Files/External/File/Name")?.InnerText;
        var packageName = document.SelectSingleNode("/Package/@id")?.InnerText;

        if (command is null || returnCodes.IsEmpty() || externalFile is null || packageName is null)
        {
            value = default;
            return false;
        }

        value = new ExternalDetectionRule
        {
            Command = command,
            ReturnCodes = returnCodes,
            Url = $"{baseLocation}/{externalFile}",
            FileName = externalFile,
            PackageName = packageName
        };
        return true;
    }

    public Task<bool> CheckDependenciesSatisfiedAsync(List<DriverInfo> _, HttpClient httpClient, CancellationToken token) => CheckExternalDependency(httpClient, token);

    public Task<bool> DetectInstallNeededAsync(List<DriverInfo> _, HttpClient httpClient, CancellationToken token) => CheckExternalDependency(httpClient, token);

    private const string REQUIRED_SIGNER_ORGANIZATION = "Lenovo";

    // The helper is run with administrator rights, so it must be exactly the Lenovo-signed file we just
    // downloaded. It goes into a fresh folder every time (a file planted in %TEMP% is never reused), and it's
    // locked against changes from the signature check until it has finished running.
    private async Task<bool> CheckExternalDependency(HttpClient httpClient, CancellationToken token)
    {
        if (!IsSafeFileName(PackageName) || !IsSafeFileName(FileName))
        {
            Log.Instance.Trace($"Refusing external detection with unsafe names. [package={PackageName}, file={FileName}]");
            return false;
        }

        var workDir = Path.Combine(Folders.Temp, TEMP_FOLDER_SUB_FOLDER, PackageName, Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(workDir, FileName);

        Directory.CreateDirectory(workDir);
        try
        {
            await using (var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await httpClient.DownloadAsync(Url, fileStream, null, token).ConfigureAwait(false);

            await using var locked = LockedFile.Open(filePath);

            var trust = AuthenticodeVerifier.Verify(filePath);
            if (trust != 0)
            {
                Log.Instance.Trace($"External detection helper signature invalid (0x{trust:X8}). Not running it. [file={filePath}]");
                return false;
            }

            var organization = AuthenticodeVerifier.GetSignerOrganization(filePath);
            if (!string.Equals(organization, REQUIRED_SIGNER_ORGANIZATION, StringComparison.Ordinal))
            {
                Log.Instance.Trace($"External detection helper not signed by {REQUIRED_SIGNER_ORGANIZATION} (signer O={organization}). Not running it.");
                return false;
            }

            var (executable, arguments) = ResolveCommand(Command, workDir);
            if (executable is null || !string.Equals(Path.GetFullPath(executable), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase))
            {
                Log.Instance.Trace($"External detection command doesn't run the downloaded helper. Refusing. [command={Command}]");
                return false;
            }

            var (exitCode, _) = await CMD.RunAsync(filePath, arguments, token: token).ConfigureAwait(false);
            return ReturnCodes.Contains(exitCode);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, true);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to clean up {workDir}.", ex);
            }
        }
    }

    /// <summary>
    /// A plain file name with no folder parts, so it can't point outside the work folder.
    /// </summary>
    internal static bool IsSafeFileName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name == Path.GetFileName(name)
        && name is not "." and not ".."
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>
    /// Splits the catalog command into the executable path (with %PACKAGEPATH% expanded and bare names
    /// placed in <paramref name="workDir"/>) and its arguments.
    /// </summary>
    internal static (string? Executable, string Arguments) ResolveCommand(string command, string workDir)
    {
        var parts = command.Split(' ');
        var executable = parts.FirstOrDefault();
        var arguments = string.Join(' ', parts.Skip(1));

        if (string.IsNullOrWhiteSpace(executable))
            return (null, arguments);

        // ReSharper disable StringLiteralTypo
        executable = executable.Replace("%PACKAGEPATH%", workDir, StringComparison.OrdinalIgnoreCase);
        // ReSharper restore StringLiteralTypo

        if (!executable.Contains('\\'))
            executable = Path.Combine(workDir, executable);

        return (executable, arguments);
    }
}
