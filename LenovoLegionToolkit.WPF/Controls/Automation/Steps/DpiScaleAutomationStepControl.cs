using System;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class DpiScaleAutomationStepControl : AbstractComboBoxAutomationStepCardControl<DpiScale>
{
    private readonly DisplayConfigurationListener _listener = IoCContainer.Resolve<DisplayConfigurationListener>();

    public DpiScaleAutomationStepControl(IAutomationStep<DpiScale> step) : base(step)
    {
        Icon = SymbolRegular.TextFontSize24;
        Title = Resource.DpiScaleAutomationStepControl_Title;
        Subtitle = Resource.DpiScaleAutomationStepControl_Message;

        this.WhileLoaded(() => _listener.Changed += Listener_Changed, () => _listener.Changed -= Listener_Changed);
    }

    protected override string ComboBoxItemDisplayName(DpiScale value)
    {
        var str = base.ComboBoxItemDisplayName(value);
        return LocalizationHelper.ForceLeftToRight(str);
    }

    private void Listener_Changed(object? sender, EventArgs e) => Dispatcher.Post(async () =>
    {
        if (IsLoaded)
            await RefreshAsync();
    });
}
