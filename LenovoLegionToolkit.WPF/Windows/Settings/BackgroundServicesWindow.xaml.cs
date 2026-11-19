using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

/// <summary>
/// Which services LLT keeps stopped while it runs (normal use). Nothing changes until Apply; the services' states
/// are read off the UI thread.
/// </summary>
public partial class BackgroundServicesWindow
{
    private readonly BackgroundServicesSettings _settings = IoCContainer.Resolve<BackgroundServicesSettings>();
    private readonly BackgroundServicesController _controller = IoCContainer.Resolve<BackgroundServicesController>();

    private readonly Dictionary<string, CheckBox> _presetBoxes = [];

    public BackgroundServicesWindow()
    {
        InitializeComponent();
        BuildPresets();
        Loaded += async (_, _) => await RefreshStatusAsync();
    }

    private void BuildPresets()
    {
        _enabledToggle.IsChecked = _settings.Store.Enabled;

        // The first time, the presets marked "on by default" are suggested; afterwards, what was saved.
        var chosen = _settings.Store.Presets.Count > 0 || _settings.Store.Enabled
            ? _settings.Store.Presets.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : ServicePresets.All.Where(p => p.OnByDefault).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var preset in ServicePresets.All)
        {
            var box = new CheckBox
            {
                Content = Resource.ResourceManager.GetString($"ServicePreset_{preset.Id}_Title") ?? preset.Id,
                IsChecked = chosen.Contains(preset.Id),
                FontWeight = FontWeights.SemiBold,
            };
            _presetBoxes[preset.Id] = box;

            var note = new TextBlock
            {
                Text = $"{Resource.ResourceManager.GetString($"ServicePreset_{preset.Id}_Note")} ({string.Join(", ", preset.Services)})",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(28, 0, 0, 10),
            };
            note.SetResourceReference(ForegroundProperty, preset.HasWarning ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush");

            _presetsPanel.Children.Add(box);
            _presetsPanel.Children.Add(note);
        }
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var status = await Task.Run(_controller.GetStatus);
            _statusPanel.Children.Clear();

            if (status.Count == 0)
            {
                _statusPanel.Children.Add(new TextBlock { Text = Resource.BackgroundServices_NoneChosen, TextWrapping = TextWrapping.Wrap });
                return;
            }

            foreach (var service in status)
                _statusPanel.Children.Add(StatusRow(service));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the background services' status.", ex);
            _statusText.Text = Resource.SafetyReport_Unknown;
        }
    }

    private static Grid StatusRow(BackgroundServiceStatus service)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = new TextBlock { Text = $"{service.DisplayName} ({service.Name})", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        name.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        var state = new TextBlock { Text = StateText(service), TextWrapping = TextWrapping.Wrap };
        state.SetResourceReference(ForegroundProperty, service.State is BackgroundServiceState.RunningAgain or BackgroundServiceState.Refused
            ? "SystemFillColorCautionBrush"
            : "TextFillColorPrimaryBrush");
        Grid.SetColumn(state, 1);

        grid.Children.Add(name);
        grid.Children.Add(state);
        return grid;
    }

    public static string StateText(BackgroundServiceStatus service) => service.State switch
    {
        BackgroundServiceState.StoppedByLlt => Resource.BackgroundServices_State_StoppedByLlt,
        BackgroundServiceState.RunningAgain => Resource.BackgroundServices_State_RunningAgain,
        BackgroundServiceState.Running => Resource.BackgroundServices_State_Running,
        BackgroundServiceState.StoppedAlready => Resource.BackgroundServices_State_StoppedAlready,
        BackgroundServiceState.NotInstalled => Resource.BackgroundServices_State_NotInstalled,
        _ => string.Format(Resource.BackgroundServices_State_Refused, service.Refusal is { } r ? GameProfileText.Refusal(r) : string.Empty),
    };

    private async Task RunAsync(Func<Task> action)
    {
        _applyButton.IsEnabled = false;
        _restoreButton.IsEnabled = false;
        _statusText.Text = Resource.BackgroundServices_Working;
        try
        {
            await Task.Run(action);
            _statusText.Text = Resource.BackgroundServices_Done;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Background services action failed.", ex);
            _statusText.Text = Resource.BackgroundServices_Failed;
        }
        finally
        {
            _applyButton.IsEnabled = true;
            _restoreButton.IsEnabled = true;
            await RefreshStatusAsync();
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Store.Enabled = _enabledToggle.IsChecked == true;
        _settings.Store.Presets = [.. _presetBoxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key)];
        _settings.SynchronizeStore();

        await RunAsync(_controller.ApplyAsync);
    }

    /// <summary>Turns the feature off and starts every service LLT stopped.</summary>
    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Store.Enabled = false;
        _settings.SynchronizeStore();
        _enabledToggle.IsChecked = false;

        await RunAsync(_controller.ApplyAsync);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
