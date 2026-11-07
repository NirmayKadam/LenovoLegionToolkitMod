using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

public class ControllerHelpersTests
{
    // Locks in the Smart Fan <= 7 mapping exactly as it behaved before the dead patterns were removed.
    [Theory]
    [InlineData(1, 4, FanTableType.CPU)]
    [InlineData(1, 1, FanTableType.CPUSensor)]
    [InlineData(2, 5, FanTableType.GPU)]
    [InlineData(3, 5, FanTableType.GPU2)]
    [InlineData(4, 4, FanTableType.Unknown)] // only listed for version 8 in the old unreachable patterns
    [InlineData(9, 9, FanTableType.Unknown)]
    public void FanTableType_UpToSmartFanV7(byte fanId, byte sensorId, FanTableType expected)
    {
        Assert.Equal(expected, GodModeController.ResolveFanTableTypeUpToV7(fanId, sensorId));
    }

    [Fact]
    public void CpuBaseClock_IsReadOnThisMachine()
    {
        // Real Windows call on the test machine; 0 means CallNtPowerInformation failed.
        Assert.True(AbstractSensorsController.GetCpuBaseClock() > 0);
    }
}
