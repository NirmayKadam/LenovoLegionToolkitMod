using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features.OverDrive;

public class OverDriveCapabilityFeature() : AbstractCapabilityFeature<OverDriveState>(CapabilityID.OverDrive)
{
    protected override Task<bool> ValidateExtraSupportAsync(MachineInformation mi)
    {
        return Task.FromResult(Compatibility.GetIsOverdriverSupported());
    }
}
