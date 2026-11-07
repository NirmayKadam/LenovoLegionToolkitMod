using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class HardwareSensorsAutomationStep(HardwareSensorsState state)
    : AbstractFeatureAutomationStep<HardwareSensorsState>(state)
{
    public override IAutomationStep DeepCopy() => new HardwareSensorsAutomationStep(State);
}
