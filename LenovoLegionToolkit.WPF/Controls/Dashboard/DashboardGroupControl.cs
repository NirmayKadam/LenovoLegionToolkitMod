using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Utils.Skins;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class DashboardGroupControl : UserControl
{
    private readonly TaskCompletionSource _initializedTaskCompletionSource = new();

    private readonly DashboardGroup _dashboardGroup;

    public Task InitializedTask => _initializedTaskCompletionSource.Task;

    public DashboardGroupControl(DashboardGroup dashboardGroup)
    {
        _dashboardGroup = dashboardGroup;

        Initialized += DashboardGroupControl_Initialized;
    }

    private async void DashboardGroupControl_Initialized(object? sender, System.EventArgs e)
    {
        // The dashboard waits for every group before it stops showing "loading", so this group always reports that it
        // has finished, or the whole dashboard would stay on the loader forever.
        try
        {
            var stackPanel = new StackPanel { Margin = new(0, 0, 16, 0) };

            var textBlock = new TextBlock
            {
                Text = _dashboardGroup.GetName(),
                Focusable = true,
                FontSize = 24,
                FontWeight = FontWeights.Medium,
                Margin = new(0, 16, 0, 24)
            };
            AutomationProperties.SetName(textBlock, textBlock.Text);
            textBlock.SetResourceReference(EffectProperty, SkinKeys.TextGlowEffect);
            stackPanel.Children.Add(textBlock);

            // One card failing to build is left out; it used to empty the whole group.
            var controlsTasks = _dashboardGroup.Items.Select(ControlsOrNoneAsync);
            var controls = await Task.WhenAll(controlsTasks);

            foreach (var control in controls.SelectMany(c => c))
            {
                stackPanel.Children.Add(control);
            }

            Content = stackPanel;
        }
        finally
        {
            _initializedTaskCompletionSource.TrySetResult();
        }
    }

    private static async Task<IEnumerable<AbstractRefreshingControl>> ControlsOrNoneAsync(DashboardItem item)
    {
        try
        {
            return await item.GetControlAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Dashboard card couldn't be built and is left out. [item={item}]", ex);
            return [];
        }
    }
}
