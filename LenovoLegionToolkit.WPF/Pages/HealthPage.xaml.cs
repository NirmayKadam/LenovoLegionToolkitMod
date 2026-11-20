using System;
using System.Diagnostics;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows;
using Wpf.Ui.Common;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Pages;

/// <summary>The read-only Health check: one card per item, each opening the Windows page where it can be changed.</summary>
public partial class HealthPage
{
    private readonly HealthMonitor _monitor = IoCContainer.Resolve<HealthMonitor>();

    public HealthPage()
    {
        InitializeComponent();

        // Only while shown, so a closed page doesn't keep this page alive.
        Loaded += async (_, _) =>
        {
            _monitor.Updated -= Monitor_Updated;
            _monitor.Updated += Monitor_Updated;

            ShowItems();
            if (_monitor.LastCheckUtc is null || _monitor.Items.Count == 0)
                await CheckNowAsync();
        };
        Unloaded += (_, _) => _monitor.Updated -= Monitor_Updated;
    }

    // Raised on a background thread after every check.
    private void Monitor_Updated(object? sender, EventArgs e) => Dispatcher.Post(ShowItems);

    private async void CheckNowButton_Click(object sender, RoutedEventArgs e) => await CheckNowAsync();

    private async System.Threading.Tasks.Task CheckNowAsync()
    {
        _checkNowButton.IsEnabled = false;
        _lastCheckText.Text = Resource.Health_Checking;
        try
        {
            await _monitor.CheckAsync(notify: false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Health check from the page failed.", ex);
        }
        finally
        {
            _checkNowButton.IsEnabled = true;
            ShowItems();
        }
    }

    private void ShowItems()
    {
        _lastCheckText.Text = _monitor.LastCheckUtc is { } last
            ? string.Format(Resource.Health_LastCheck, last.ToLocalTime().ToString("g"))
            : Resource.Health_NotYetChecked;

        _itemsPanel.Children.Clear();
        foreach (var item in _monitor.Items)
            _itemsPanel.Children.Add(CreateCard(item));
    }

    private UIElement CreateCard(HealthItem item)
    {
        var header = new CardHeaderControl { Title = item.Title, Subtitle = item.Detail, HelpKey = $"Help_Health_{item.Check}" };
        switch (item.State)
        {
            case HealthState.Attention:
                header.Warning = Resource.Health_State_Attention;
                break;
            case HealthState.Ok:
                header.Success = Resource.Health_State_Ok;
                break;
            case HealthState.Info:
                header.Info = Resource.Health_State_Info;
                break;
            default:
                header.Info = Resource.Health_State_Unknown;
                break;
        }

        var card = new CardControl { Margin = new(0, 0, 0, 8), Icon = Icon(item.Check), Header = header };

        if (item.Action != HealthAction.None)
        {
            var button = new Button { Content = Resource.Health_Open, MinWidth = 100, Appearance = ControlAppearance.Secondary };
            System.Windows.Automation.AutomationProperties.SetName(button, $"{Resource.Health_Open}: {item.Title}");
            button.Click += (_, _) => Open(item.Action);
            card.Content = button;
        }

        return card;
    }

    private static SymbolRegular Icon(HealthCheck check) => check switch
    {
        HealthCheck.BatteryAction => SymbolRegular.BatteryCharge24,
        HealthCheck.WindowsUpdate => SymbolRegular.ArrowSync24,
        HealthCheck.VirusProtection => SymbolRegular.Shield24,
        HealthCheck.SecurityNotifications => SymbolRegular.Alert24,
        HealthCheck.MemoryIntegrity => SymbolRegular.ShieldCheckmark24,
        HealthCheck.DisabledServices => SymbolRegular.Settings24,
        HealthCheck.GpuDriverErrors => SymbolRegular.DeveloperBoard24,
        HealthCheck.DiskSpace => SymbolRegular.Storage24,
        HealthCheck.BatteryWear => SymbolRegular.BatteryCheckmark24,
        _ => SymbolRegular.HeartPulse24
    };

    /// <summary>
    /// Opens where the item can be changed. Settings pages go through explorer, so they open as you, not elevated;
    /// nothing is changed by Indra.
    /// </summary>
    private void Open(HealthAction action)
    {
        try
        {
            switch (action)
            {
                case HealthAction.PowerOptions:
                    Process.Start(new ProcessStartInfo("control.exe", "powercfg.cpl,,3") { UseShellExecute = true });
                    break;
                case HealthAction.Services:
                    Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });
                    break;
                case HealthAction.BatteryPage:
                    (Window.GetWindow(this) as MainWindow)?.NavigateTo("battery");
                    break;
                case HealthAction.None:
                    break;
                default:
                    new Uri(action switch
                    {
                        HealthAction.WindowsUpdate => "ms-settings:windowsupdate",
                        HealthAction.WindowsSecurity => "windowsdefender://threat",
                        HealthAction.Notifications => "ms-settings:notifications",
                        HealthAction.CoreIsolation => "windowsdefender://coreisolation",
                        _ => "ms-settings:storagesense"
                    }).Open();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Opening {action} failed.", ex);
        }
    }
}
