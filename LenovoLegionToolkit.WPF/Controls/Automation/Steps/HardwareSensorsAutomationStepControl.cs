using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class HardwareSensorsAutomationStepControl : AbstractComboBoxAutomationStepCardControl<HardwareSensorsState>
{
    public HardwareSensorsAutomationStepControl(IAutomationStep<HardwareSensorsState> step) : base(step)
    {
        Icon = SymbolRegular.DataUsage24;
        Title = Resource.HardwareSensorsAutomationStepControl_Title;
        Subtitle = Resource.HardwareSensorsAutomationStepControl_Message;
    }
}
