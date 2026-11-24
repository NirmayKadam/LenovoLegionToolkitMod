using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Automation.Utils;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

// Real files and real Windows signature checks: an unsigned file, and a copy of the Microsoft-signed dotnet.exe
// (a valid signature from the wrong signer). The Micro-Star case is checked through the signer rule itself, because
// the test machine may not have Afterburner installed.
public sealed class AfterburnerStepTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static string DotnetExe => LenovoLegionToolkit.Tests.Utils.TestFiles.SignedDotnetExe;

    // ---- Which program may start ----

    [Theory]
    [InlineData(0, "MICRO-STAR INTERNATIONAL CO., LTD.", AfterburnerCheck.Genuine)]
    [InlineData(0, " Micro-Star International Co., Ltd. ", AfterburnerCheck.Genuine)]
    [InlineData(0, "Microsoft Corporation", AfterburnerCheck.WrongSigner)]
    [InlineData(0, null, AfterburnerCheck.WrongSigner)]
    [InlineData(unchecked((int)0x80096010), "MICRO-STAR INTERNATIONAL CO., LTD.", AfterburnerCheck.NotSigned)]   // TRUST_E_BAD_DIGEST: changed after signing
    [InlineData(unchecked((int)0x800B0100), null, AfterburnerCheck.NotSigned)]                                   // TRUST_E_NOSIGNATURE
    public void OnlyAValidMicroStarSignature_IsGenuine(int trustResult, string? signer, AfterburnerCheck expected) =>
        Assert.Equal(expected, AfterburnerProgram.Classify(trustResult, signer));

    [Fact]
    public void AMissingProgram_IsNotFound()
    {
        Assert.Equal(AfterburnerCheck.NotFound, AfterburnerProgram.Check(null));
        Assert.Equal(AfterburnerCheck.NotFound, AfterburnerProgram.Check(Path.Combine(_dir, AfterburnerProgram.FileName)));
    }

    [Fact]
    public void AnotherProgram_IsNotAfterburner()
    {
        var other = Path.Combine(_dir, "tool.exe");
        File.Copy(DotnetExe, other);
        Assert.Equal(AfterburnerCheck.NotAfterburner, AfterburnerProgram.Check(other));
    }

    [Fact]
    public void AnUnsignedFileNamedLikeAfterburner_IsNotSigned()
    {
        var fake = Path.Combine(_dir, AfterburnerProgram.FileName);
        File.WriteAllBytes(fake, [0x4D, 0x5A, 0, 0]);
        Assert.Equal(AfterburnerCheck.NotSigned, AfterburnerProgram.Check(fake));
    }

    [Fact]
    public void AValidlySignedProgramFromAnotherCompany_IsTheWrongSigner()
    {
        var renamed = Path.Combine(_dir, AfterburnerProgram.FileName);
        File.Copy(DotnetExe, renamed);
        Assert.Equal(AfterburnerCheck.WrongSigner, AfterburnerProgram.Check(renamed));
    }

    [Fact]
    public async Task AProgramThatIsNotGenuine_IsNeverStarted()
    {
        var renamed = Path.Combine(_dir, AfterburnerProgram.FileName);
        File.Copy(DotnetExe, renamed);
        var starts = new List<string>();   // records what would be started, instead of starting it

        var result = await AfterburnerProgram.StartProfileAsync(renamed, 2, (file, arguments, _) =>
        {
            starts.Add($"{file} {arguments}");
            return Task.CompletedTask;
        });

        Assert.Equal(AfterburnerCheck.WrongSigner, result);
        Assert.Empty(starts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task ProfilesOutsideOneToFive_AreRefused(int profile) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AfterburnerProgram.StartProfileAsync(DotnetExe, profile));

    // ---- Converting the old Run steps ----

    private static RunAutomationStep Run(string path, string? arguments) => new(path, arguments, true, false, false);

    [Theory]
    [InlineData(@"C:\MSI Afterburner\MSIAfterburner.exe", "-Profile4", 4)]
    [InlineData(@"""C:\MSI Afterburner\MSIAfterburner.exe""", " -profile2 ", 2)]
    [InlineData(@"D:\Tools\msiafterburner.EXE", "-PROFILE5", 5)]
    public void RunStepsThatOnlySwitchAProfile_AreConverted(string path, string arguments, int profile)
    {
        var pipeline = new AutomationPipeline("p") { Steps = { Run(path, arguments) } };

        Assert.Equal(1, AfterburnerStepMigration.Convert([pipeline]));

        var step = Assert.IsType<AfterburnerProfileAutomationStep>(Assert.Single(pipeline.Steps));
        Assert.Equal(profile, step.State);
        Assert.Equal(path.Trim('"'), step.ProgramPath);
    }

    [Theory]
    [InlineData(@"C:\MSI Afterburner\MSIAfterburner.exe", "-Profile6")]
    [InlineData(@"C:\MSI Afterburner\MSIAfterburner.exe", "-Profile4 -m")]
    [InlineData(@"C:\MSI Afterburner\MSIAfterburner.exe", "")]
    [InlineData(@"C:\MSI Afterburner\MSIAfterburner.exe", null)]
    [InlineData(@"C:\Windows\System32\cmd.exe", "-Profile1")]
    [InlineData(@"C:\MSI Afterburner\RTSS.exe", "-Profile1")]
    public void OtherRunSteps_AreLeftAlone(string path, string? arguments)
    {
        var run = Run(path, arguments);
        var pipeline = new AutomationPipeline("p") { Steps = { run } };

        Assert.Equal(0, AfterburnerStepMigration.Convert([pipeline]));
        Assert.Same(run, Assert.Single(pipeline.Steps));
    }

    [Fact]
    public void ConvertingTwice_FindsNothingTheSecondTime_AndKeepsTheOtherSteps()
    {
        var delay = new DelayAutomationStep(new Delay(1));
        var pipelines = new List<AutomationPipeline>
        {
            new("a") { Steps = { Run(@"C:\MSI Afterburner\MSIAfterburner.exe", "-Profile1"), delay } },
            new("b") { Steps = { Run(@"C:\MSI Afterburner\MSIAfterburner.exe", "-Profile3") } },
        };

        Assert.Equal(2, AfterburnerStepMigration.Convert(pipelines));
        Assert.Equal(0, AfterburnerStepMigration.Convert(pipelines));
        Assert.Same(delay, pipelines[0].Steps[1]);
    }

    [Fact]
    public void TheBackup_IsMadeOnce_AndNeverOverwritten()
    {
        var settings = Path.Combine(_dir, "automation.json");
        File.WriteAllText(settings, "first");

        var backup = AfterburnerStepMigration.BackUpOnce(settings);
        File.WriteAllText(settings, "second");
        AfterburnerStepMigration.BackUpOnce(settings);

        Assert.Equal(Path.Combine(_dir, AfterburnerStepMigration.BackupFileName), backup);
        Assert.Equal("first", File.ReadAllText(backup!));
        Assert.Null(AfterburnerStepMigration.BackUpOnce(Path.Combine(_dir, "missing.json")));
    }

    [Fact]
    public void LaterProfileSteps_OverrideEarlierOnes() =>
        Assert.Single(AutomationRules.Check(null,
            [new StepInfo(typeof(AfterburnerProfileAutomationStep), 1), new StepInfo(typeof(AfterburnerProfileAutomationStep), 4)]),
            i => i.StepIndex == 0 && i.Kind == StepIssueKind.Overridden);

    [Fact]
    public void AProfileOutsideOneToFive_InASavedFile_BecomesProfileOne() =>
        Assert.Equal(1, new AfterburnerProfileAutomationStep(9, null).State);

    [Fact]
    public async Task TheStepOffersProfilesOneToFive() =>
        Assert.Equal(Enumerable.Range(1, 5), await new AfterburnerProfileAutomationStep(1).GetAllStatesAsync());
}
