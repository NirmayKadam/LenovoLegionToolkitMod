using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LenovoLegionToolkit.WPF.Resources;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The app is Indra, maintained by Nirmay Kadam, while the GPL-3.0 notices for the original project stay reachable,
/// and the installer still upgrades the same installation. Reads the real resources, XAML, build props and installer scripts.
/// </summary>
public class AppIdentityTests
{
    private static readonly string Root = HelpContentTests.FindRepositoryRoot();
    private static readonly string Wpf = Path.Combine(Root, "LenovoLegionToolkit.WPF");
    private const string OriginalRepository = "github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit";

    [Fact]
    public void TheShownName_IsIndra()
    {
        Assert.Equal("Indra – Legion Toolkit Mod", Resource.AppName);
        Assert.Equal(Resource.AppName, Resource.AboutPage_AppName);
    }

    [Fact]
    public void About_ShowsTheMaintainer_AndKeepsTheGplNotice_BehindOneExpander()
    {
        Assert.Contains("Nirmay Kadam", Resource.AboutPage_DevelopedBy);
        Assert.Contains("Nirmay Kadam", Resource.AboutPage_Copyright);

        // GPL-3.0: the original copyright, the licence, no warranty, and a way to read it.
        Assert.Contains("Bartosz Cichecki, Kaguya, and Dr. Skinner", Resource.AboutPage_OriginalNotice);
        Assert.Contains("GPL-3.0", Resource.AboutPage_LicenceNotice);
        Assert.Contains("no warranty", Resource.AboutPage_LicenceNotice);

        var about = File.ReadAllText(Path.Combine(Wpf, "Pages", "AboutPage.xaml"));
        Assert.Contains("<Expander", about);
        Assert.Contains("Resource.AboutPage_OriginalNotice", about);
        Assert.Contains("Resource.AboutPage_LicenceNotice", about);
        Assert.Contains("Click=\"ViewLicence_Click\"", about);
    }

    [Fact]
    public void OnlyTheOriginalProjectLink_PointsAtTheOriginalRepository()
    {
        var links = Directory.EnumerateFiles(Wpf, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), Regex.Escape(OriginalRepository) + @"[^""\s]*").Select(m => (File: Path.GetFileName(f), Link: m.Value)))
            .ToList();

        var link = Assert.Single(links);
        Assert.Equal(("AboutPage.xaml", OriginalRepository), link);
    }

    [Fact]
    public void TheDonatePage_IsGone()
    {
        Assert.False(File.Exists(Path.Combine(Wpf, "Pages", "DonatePage.xaml")));
        Assert.DoesNotContain("DonatePage", File.ReadAllText(Path.Combine(Wpf, "Windows", "MainWindow.xaml")));
    }

    [Fact]
    public void TheBuildCopyright_KeepsBothNotices()
    {
        var props = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));
        var copyright = Regex.Match(props, "<Copyright>([^<]*)</Copyright>").Groups[1].Value;

        Assert.Contains("Nirmay Kadam", copyright);
        Assert.Contains("Bartosz Cichecki, Kaguya, and Dr. Skinner", copyright);
    }

    [Fact]
    public void TheInstaller_ShowsTheNewIdentity_ButUpgradesTheSameInstallation()
    {
        const string script = "make_installer.iss";
        var bytes = File.ReadAllBytes(Path.Combine(Root, script));
        Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..], "UTF-8 with a byte-order mark, so Inno Setup reads the name's dash and the © correctly");
        var iss = File.ReadAllText(Path.Combine(Root, script));

        // Unchanged: same installation, same folder, same program file.
        Assert.Contains("AppId={{0C37B9AC-9C3D-4302-8ABB-125C7C7D83D5}", iss);
        Assert.Contains("#define MyAppNameCompact \"LenovoLegionToolkit\"", iss);
        Assert.Contains(@"DefaultDirName={commonpf}\{#MyAppNameCompact}", iss);
        Assert.Contains("#define MyAppExeName \"Lenovo Legion Toolkit.exe\"", iss);

        // New: name, publisher, links; the original authors' Discord servers are gone.
        Assert.Contains("#define MyAppName \"Indra – Legion Toolkit Mod\"", iss);
        Assert.Contains("#define MyAppPublisher \"Nirmay Kadam\"", iss);
        Assert.Contains("#define MyAppURL \"https://github.com/NirmayKadam/LenovoLegionToolkitMod\"", iss);
        Assert.DoesNotContain("Discord", iss);
        Assert.DoesNotContain(OriginalRepository, iss);

        // The copyright keeps the original notice; old shortcuts are removed on upgrade.
        Assert.Contains("Bartosz Cichecki, Kaguya, and Dr. Skinner", iss);
        Assert.Contains(@"{autoprograms}\Lenovo Legion Toolkit.lnk", iss);
    }
}
