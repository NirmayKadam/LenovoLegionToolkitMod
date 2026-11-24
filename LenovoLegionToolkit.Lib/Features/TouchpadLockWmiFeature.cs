using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features;

public class TouchpadLockWmiFeature()
    : AbstractWmiFeature<TouchpadLockState>(WMI.LenovoGameZoneData.GetTPStatusStatusAsync, WMI.LenovoGameZoneData.SetTPStatusAsync, WMI.LenovoGameZoneData.IsSupportDisableTPAsync)
{
    private readonly ThreadSafeCounter _expectedEchoCounter = new();

    public override async Task SetStateAsync(TouchpadLockState state)
    {
        _expectedEchoCounter.Increment();

        await base.SetStateAsync(state).ConfigureAwait(false);
    }

    internal bool ConsumeExpectedEcho() => !_expectedEchoCounter.Decrement();
}
