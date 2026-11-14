using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Steps;

namespace LenovoLegionToolkit.Lib.Automation;

/// <summary>
/// Ready-made quick actions (pipelines without a trigger) that users can bind to special keys or run from the tray.
/// </summary>
public static class QuickActionTemplates
{
    /// <summary>A quick action that shows the OSD when hidden and hides it when shown.</summary>
    public static AutomationPipeline CreateOsdToggle(string name) => new()
    {
        Name = name,
        IconName = "Eye24",
        Trigger = null,
        Steps = [new OsdAutomationStep(ToggleState.Toggle)],
    };

    /// <summary>
    /// An existing quick action that does exactly that (no trigger, a single OSD toggle step), so a second copy
    /// isn't created.
    /// </summary>
    public static AutomationPipeline? FindOsdToggle(IEnumerable<AutomationPipeline> pipelines) =>
        pipelines.FirstOrDefault(p => p.Trigger is null
                                      && p.Steps.Count == 1
                                      && p.Steps[0] is OsdAutomationStep { State: ToggleState.Toggle });
}
