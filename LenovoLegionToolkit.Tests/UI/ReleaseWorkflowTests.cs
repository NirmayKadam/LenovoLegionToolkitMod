using System.IO;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The release workflow only runs for version tags on main, never keeps the signing key, refuses files signed by
/// another certificate, and only creates a draft. Local and release builds share one installer script.
/// Reads the real workflow and build scripts.
/// </summary>
public class ReleaseWorkflowTests
{
    private static readonly string Root = HelpContentTests.FindRepositoryRoot();
    private static readonly string Workflow = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "release.yml"));

    [Fact]
    public void ItRuns_OnlyForVersionTags_OnMain()
    {
        Assert.Contains("tags:\n      - 'v*'", Workflow.Replace("\r\n", "\n"));
        Assert.DoesNotContain("workflow_dispatch", Workflow);
        Assert.DoesNotContain("pull_request", Workflow);
        Assert.Contains("git merge-base --is-ancestor HEAD origin/main", Workflow);
    }

    [Fact]
    public void TheSigningKey_IsAlwaysRemoved_AndOtherCertificatesFailTheRun()
    {
        var normalized = Workflow.Replace("\r\n", "\n");

        Assert.Contains("- name: Remove signing certificate\n      if: always()", normalized);
        Assert.Contains("LenovoLegionToolkit.cer", Workflow);
        Assert.Contains("$sig.SignerCertificate.Thumbprint -ne $expected", Workflow);
        Assert.Contains("Get-FileHash", Workflow);
    }

    [Fact]
    public void ItOnlyCreatesADraft_FromMain()
    {
        Assert.Contains("draft: true", Workflow);
        Assert.Contains("target_commitish: main", Workflow);
    }

    [Fact]
    public void LocalAndReleaseBuilds_ShareOneInstallerScript()
    {
        Assert.False(File.Exists(Path.Combine(Root, "make_installer_action.iss")));
        Assert.Contains("iscc make_installer.iss", File.ReadAllText(Path.Combine(Root, "make.bat")));
        Assert.Contains("iscc make_installer.iss", File.ReadAllText(Path.Combine(Root, "make_action.bat")));
    }
}
