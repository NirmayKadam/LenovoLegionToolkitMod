using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Steps;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

// Uses real steps: a real 5 s delay, and a real cmd.exe that writes a marker file.
public class PipelineCancellationTests : IDisposable
{
    private readonly string _markerPath = Path.Combine(Path.GetTempPath(), $"llt_test_marker_{Guid.NewGuid():N}.txt");

    public void Dispose()
    {
        if (File.Exists(_markerPath))
            File.Delete(_markerPath);
    }

    private AutomationPipeline CreatePipeline() => new("test")
    {
        Steps =
        {
            new DelayAutomationStep(new Delay(5)),
            new RunAutomationStep("cmd.exe", $"/c echo ran> \"{_markerPath}\"", runSilently: true, waitUntilFinished: true, checkInstance: false),
        }
    };

    [Fact]
    public async Task CancelledRun_StopsPromptly_IsNotAFailure_AndSkipsRemainingSteps()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var sw = Stopwatch.StartNew();

        // Before the fix, the cancelled delay was collected as a step failure and thrown as AggregateException.
        await CreatePipeline().RunAsync([], cts.Token);

        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"Pipeline took {sw.Elapsed} after cancellation.");
        Assert.False(File.Exists(_markerPath), "Step after the interruption should not have run.");
    }

    [Fact]
    public async Task UncancelledRun_RunsAllSteps()
    {
        var pipeline = new AutomationPipeline("test")
        {
            Steps =
            {
                new DelayAutomationStep(new Delay(1)),
                new RunAutomationStep("cmd.exe", $"/c echo ran> \"{_markerPath}\"", runSilently: true, waitUntilFinished: true, checkInstance: false),
            }
        };

        await pipeline.RunAsync([], CancellationToken.None);

        Assert.True(File.Exists(_markerPath));
        Assert.Equal("ran", File.ReadAllText(_markerPath).Trim());
    }
}
