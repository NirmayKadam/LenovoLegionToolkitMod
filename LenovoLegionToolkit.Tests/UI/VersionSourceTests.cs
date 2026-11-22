using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The version is set in one place, and no build or installer falls back to 0.0.1: the update check compares the
/// app's own version with the releases, so a 0.0.1 build (or the old 2.34 in the project file) always "saw" an update.
/// </summary>
public class VersionSourceTests
{
    private static readonly string Root = HelpContentTests.FindRepositoryRoot();

    [Fact]
    public void TheProjectFile_CarriesTheCurrentVersion()
    {
        var props = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));

        var version = Version.Parse(Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value);
        var fileVersion = Version.Parse(Regex.Match(props, "<FileVersion>([^<]+)</FileVersion>").Groups[1].Value);

        Assert.Equal(version, fileVersion);
        Assert.True(version >= new Version(2, 36, 18, 0), $"Directory.Build.props says {version}");
    }

    [Fact]
    public void MakeBat_NeedsAVersion_AndHasNoDefault()
    {
        var make = File.ReadAllText(Path.Combine(Root, "make.bat"));

        Assert.DoesNotContain("0.0.1", make);
        Assert.Contains("IF \"%VERSION%\"==\"\" GOTO usage", make);
        Assert.Contains(":usage", make);

        // The check comes before anything is restored or built.
        Assert.True(make.IndexOf("GOTO usage", StringComparison.Ordinal) < make.IndexOf("dotnet restore", StringComparison.Ordinal));
    }

    [Fact]
    public void TheInstaller_RefusesToBuildWithoutAVersion()
    {
        var iss = File.ReadAllText(Path.Combine(Root, "make_installer.iss"));

        Assert.Matches(@"#ifndef MyAppVersion\s+#error", iss);
        Assert.DoesNotContain("#define MyAppVersion \"0.0.1\"", iss);
    }
}
