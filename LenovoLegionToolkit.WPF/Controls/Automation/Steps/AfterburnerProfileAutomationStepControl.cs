using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Automation.Utils;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class AfterburnerProfileAutomationStepControl : AbstractComboBoxAutomationStepCardControl<int>
{
    private readonly AfterburnerProfileAutomationStep _step;

    /// <summary>Which program runs, whether it's the genuine one, and whether files can be planted next to it.</summary>
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new(8, 8, 0, 0),
        MaxWidth = 420,
    };

    public AfterburnerProfileAutomationStepControl(AfterburnerProfileAutomationStep step) : base(step)
    {
        _step = step;
        Icon = SymbolRegular.Gauge24;
        Title = Resource.AfterburnerProfileAutomationStepControl_Title;
        Subtitle = Resource.AfterburnerProfileAutomationStepControl_Message;
    }

    public override IAutomationStep CreateAutomationStep() => new AfterburnerProfileAutomationStep(SelectedState, _step.ProgramPath);

    protected override string ComboBoxItemDisplayName(int value) => string.Format(Resource.AfterburnerProfileAutomationStepControl_Profile, value);

    protected override UIElement GetCustomControl()
    {
        var panel = new StackPanel();
        panel.Children.Add(base.GetCustomControl());
        panel.Children.Add(_status);
        return panel;
    }

    protected override async Task RefreshAsync()
    {
        await base.RefreshAsync();

        var path = _step.EffectivePath;
        var check = await Task.Run(() => AfterburnerProgram.Check(path));
        var folderWarning = check == AfterburnerCheck.Genuine ? RunTargetCheck.Describe(RunTargetCheck.Check(path)) : null;

        var lines = new[] { path, AfterburnerProfileAutomationStep.Describe(check), folderWarning };
        _status.Text = string.Join("\n", lines.Where(l => !string.IsNullOrEmpty(l)));
        _status.Foreground = check == AfterburnerCheck.Genuine && folderWarning is null
            ? TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray
            : Brushes.Orange;
    }
}
