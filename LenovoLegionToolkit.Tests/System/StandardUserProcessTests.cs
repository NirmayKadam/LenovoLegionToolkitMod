using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

// Real Windows: a real cmd.exe started through the desktop shell's rights. Whether that's possible depends on how
// the test runs (a normal, non-elevated test run can't borrow another process's token), so the test accepts both
// outcomes, but never a program running with administrator rights.
public sealed class StandardUserProcessTests : IDisposable
{
    private readonly string _output = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}.txt");

    public void Dispose()
    {
        if (File.Exists(_output))
            File.Delete(_output);
    }

    private const string HighIntegrity = "S-1-16-12288";
    private const string MediumIntegrity = "S-1-16-8192";

    [Fact]
    public async Task AProgramStarts_WithoutAdministratorRights_OrDoesntStartAtAll()
    {
        var cmd = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        var result = await StandardUserProcess.RunAsync(cmd, $"/c whoami /groups > \"{_output}\"", hidden: true, waitForExit: true,
            new Dictionary<string, string?>(), CancellationToken.None);

        if (result.Started)
        {
            var groups = await File.ReadAllTextAsync(_output);
            Assert.Contains(MediumIntegrity, groups);
            Assert.DoesNotContain(HighIntegrity, groups);
            Assert.Equal(0, result.ExitCode);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Refusal));
            Assert.False(File.Exists(_output), "Refused, yet the program ran.");
        }
    }

    [Fact]
    public async Task ARunStep_ThatCantStartAsStandardUser_FailsInsteadOfRunningAsAdministrator()
    {
        var cmd = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var possibleHere = (await StandardUserProcess.RunAsync(cmd, "/c exit 0", hidden: true, waitForExit: true,
            new Dictionary<string, string?>(), CancellationToken.None)).Started;

        var step = new RunAutomationStep(cmd, $"/c whoami /groups > \"{_output}\"", true, true, false, runAsStandardUser: true);
        var run = step.RunAsync(new AutomationContext(), new AutomationEnvironment(), CancellationToken.None);

        if (possibleHere)
        {
            await run;
            var groups = await File.ReadAllTextAsync(_output);
            Assert.Contains(MediumIntegrity, groups);
            Assert.DoesNotContain(HighIntegrity, groups);
        }
        else
        {
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => run);
            Assert.False(string.IsNullOrWhiteSpace(refusal.Message));
            Assert.False(File.Exists(_output), "Refused, yet the program ran the usual way (as administrator in LLT).");
        }
    }

    [Theory]
    [InlineData(@"C:\Tools\app.exe", null, "\"C:\\Tools\\app.exe\"")]
    [InlineData(@"C:\My Tools\app.exe", "-a \"b c\"", "\"C:\\My Tools\\app.exe\" -a \"b c\"")]
    [InlineData("\"C:\\My Tools\\app.exe\"", "-x", "\"C:\\My Tools\\app.exe\" -x")]
    public void TheCommandLine_QuotesTheProgramOnce(string program, string? arguments, string expected) =>
        Assert.Equal(expected, StandardUserProcess.CommandLine(program, arguments));

    [Fact]
    public void TheEnvironmentBlock_RoundTrips_IncludingPerDriveEntries()
    {
        var block = "=C:=C:\\Users\0Path=C:\\Windows;C:\\Tools\0LLT_TRIGGER=power\0\0";

        var parsed = StandardUserProcess.ParseEnvironmentBlock(block);

        Assert.Equal("C:\\Users", parsed["=C:"]);
        Assert.Equal("C:\\Windows;C:\\Tools", parsed["PATH"]);
        Assert.Equal(parsed, StandardUserProcess.ParseEnvironmentBlock(StandardUserProcess.FormatEnvironmentBlock(parsed)));
        Assert.EndsWith("\0\0", StandardUserProcess.FormatEnvironmentBlock(parsed));
    }

    [Fact]
    public void RunStepsSavedBeforeTheOption_KeepRunningAsAdministrator_NewOnesDont()
    {
        Assert.False(new RunAutomationStep("app.exe", null, null, null, null).RunAsStandardUser);

        var copy = (RunAutomationStep)((LenovoLegionToolkit.Lib.Automation.Steps.IAutomationStep)new RunAutomationStep("app.exe", null, null, null, null, runAsStandardUser: true)).DeepCopy();
        Assert.True(copy.RunAsStandardUser);
    }
}
