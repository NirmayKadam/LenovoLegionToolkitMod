using LenovoLegionToolkit.Lib.Controllers.Sensors;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

// The FPS counter's once-a-second decision: which foreground process to measure. The loop itself needs real
// windows and processes, so only the decision is tested here.
public class FpsMonitorDecisionTests
{
    [Theory]
    [InlineData(100, null, null, FpsSensorController.MonitorStep.Switch)]   // first game comes to the front
    [InlineData(200, 100, 100, FpsSensorController.MonitorStep.Switch)]    // another app comes to the front
    [InlineData(100, 100, 100, FpsSensorController.MonitorStep.Keep)]      // same game, still running
    [InlineData(null, 100, 100, FpsSensorController.MonitorStep.Stop)]     // game closed or desktop in front
    [InlineData(null, null, null, FpsSensorController.MonitorStep.Keep)]   // nothing to do
    [InlineData(100, 100, null, FpsSensorController.MonitorStep.Keep)]     // measurement ended on its own; not restarted every second
    public void Decide(int? foreground, int? lastStarted, int? monitored, FpsSensorController.MonitorStep expected) =>
        Assert.Equal(expected, FpsSensorController.Decide(foreground, lastStarted, monitored));
}
