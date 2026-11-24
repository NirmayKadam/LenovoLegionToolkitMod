using System;
using System.IO;
using System.Linq;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>Real files on this PC that tests use.</summary>
internal static class TestFiles
{
    /// <summary>
    /// The Microsoft-signed dotnet.exe: where <c>DOTNET_ROOT</c> points (a per-user SDK), otherwise the machine-wide
    /// install in Program Files.
    /// </summary>
    public static string SignedDotnetExe { get; } = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
        }
        .Where(folder => !string.IsNullOrWhiteSpace(folder))
        .Select(folder => Path.Combine(folder!, "dotnet.exe"))
        .FirstOrDefault(File.Exists)
        ?? throw new FileNotFoundException("dotnet.exe wasn't found in DOTNET_ROOT or Program Files\\dotnet.");
}
