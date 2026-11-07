using System;
using System.IO;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Automation.Utils;
using LenovoLegionToolkit.Lib.Settings;
using Newtonsoft.Json;
using Xunit;

namespace LenovoLegionToolkit.Tests.Security;

// Writes real backup files into a temp folder, using the app's own JSON format.
public class BackupInspectorTests : IDisposable
{
    private static readonly string[] AllFiles = [BackupInspector.AUTOMATION_FILE, BackupInspector.MACRO_FILE, BackupInspector.ARGS_FILE];

    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private void WriteAutomation(params IAutomationStep[] steps)
    {
        var store = new AutomationSettings.AutomationSettingsStore
        {
            Pipelines = [new AutomationPipeline(new OnStartupAutomationPipelineTrigger()) { Steps = [.. steps] }],
        };
        File.WriteAllText(Path.Combine(_dir, BackupInspector.AUTOMATION_FILE), JsonConvert.SerializeObject(store, AbstractSettings<object>.CreateJsonSerializerSettings()));
    }

    [Fact]
    public void EmptyFolder_HasNoRisks()
    {
        Assert.False(BackupInspector.Inspect(_dir, AllFiles).HasRisks);
    }

    [Fact]
    public void AutomationWithoutRunSteps_HasNoRisks()
    {
        WriteAutomation(new DelayAutomationStep(new LenovoLegionToolkit.Lib.Automation.Delay(1)), new NotificationAutomationStep("hi"));

        Assert.False(BackupInspector.Inspect(_dir, AllFiles).HasRisks);
    }

    [Fact]
    public void RunSteps_AreListed_WithArguments()
    {
        WriteAutomation(
            new RunAutomationStep(@"C:\Users\Public\evil.exe", "--silent", true, false, false),
            new NotificationAutomationStep("hi"),
            new RunAutomationStep("powershell.exe", null, true, false, false));

        var result = BackupInspector.Inspect(_dir, AllFiles);

        Assert.True(result.HasRisks);
        Assert.Equal([new BackupInspector.ProgramToRun(@"C:\Users\Public\evil.exe", "--silent"), new BackupInspector.ProgramToRun("powershell.exe", null)], result.Programs);
    }

    [Fact]
    public void UnselectedFiles_AreNotInspected()
    {
        WriteAutomation(new RunAutomationStep("evil.exe", null, true, false, false));

        Assert.False(BackupInspector.Inspect(_dir, [BackupInspector.MACRO_FILE]).HasRisks);
    }

    [Fact]
    public void AutomationWithForbiddenType_IsReportedAsUnreadable()
    {
        File.WriteAllText(Path.Combine(_dir, BackupInspector.AUTOMATION_FILE),
            """{ "Pipelines": [ { "Trigger": { "$type": "System.IO.FileInfo, System.Private.CoreLib" } } ] }""");

        var result = BackupInspector.Inspect(_dir, AllFiles);

        Assert.True(result.HasRisks);
        Assert.NotNull(result.AutomationError);
    }

    [Fact]
    public void Macros_AreCounted()
    {
        File.WriteAllText(Path.Combine(_dir, BackupInspector.MACRO_FILE),
            """{ "IsEnabled": true, "Sequences": { "One": { }, "Two": { } } }""");

        var result = BackupInspector.Inspect(_dir, AllFiles);

        Assert.Equal(2, result.MacroCount);
        Assert.True(result.HasRisks);
    }

    [Fact]
    public void RiskyFlags_AreListedOnce_WithoutValues()
    {
        File.WriteAllLines(Path.Combine(_dir, BackupInspector.ARGS_FILE),
            ["--minimized", "--proxy-url=http://10.0.0.1:8080", "--proxy-password=secret", "--debug", "--debug"]);

        var result = BackupInspector.Inspect(_dir, AllFiles);

        Assert.Equal(["--proxy-url", "--proxy-password", "--debug"], result.Flags);
        Assert.DoesNotContain(result.Flags, f => f.Contains("secret"));
    }

    [Fact]
    public void HarmlessFlags_HaveNoRisks()
    {
        File.WriteAllLines(Path.Combine(_dir, BackupInspector.ARGS_FILE), ["--minimized", "--trace"]);

        Assert.False(BackupInspector.Inspect(_dir, AllFiles).HasRisks);
    }
}
