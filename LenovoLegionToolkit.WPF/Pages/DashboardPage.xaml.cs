using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls.Dashboard;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Dashboard;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class DashboardPage
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();
    private readonly SensorsControlSettings _sensorsControlSettings = IoCContainer.Resolve<SensorsControlSettings>();
    private readonly HardwareSensorSettings _hardwareSensorSettings = IoCContainer.Resolve<HardwareSensorSettings>();

    private readonly List<DashboardGroupControl> _dashboardGroupControls = [];
    private FrameworkElement sensorControl = null!;

    // Usage rings and the temperature graph, right above the sensor cards; shown and hidden with them.
    private readonly UsageOverviewControl _usageOverview = new() { Margin = new Thickness(0, 16, 16, 0) };

    public DashboardPage()
    {
        InitializeComponent();

        _panel.Children.Insert(_panel.Children.IndexOf(_content), _usageOverview);
        RefreshSensorControl();
        EnsureConfigFilesExist();

        // Only while the page is shown: the message bus keeps its handler, and through it the page and its window, alive.
        // A language change replaced the window, and each old page stayed in memory and reacted again (one PawnIO notice
        // per old page). The swap is made in Settings, while this page isn't shown, so the card is checked on return.
        this.WhileLoaded(() =>
        {
            MessagingCenter.Subscribe<SensorDashboardSwappedMessage>(this, _ =>
            {
                Dispatcher.Post(() =>
                {
                    EnsureConfigFilesExist();
                    RefreshSensorControl();
                });
            });

            if (!SensorControlMatchesSettings())
            {
                EnsureConfigFilesExist();
                RefreshSensorControl();
            }
        }, () => MessagingCenter.Unsubscribe<SensorDashboardSwappedMessage>(this));
    }

    /// <summary>Whether the sensor card shown is the one the settings ask for (the new one needs PawnIO).</summary>
    private bool SensorControlMatchesSettings()
    {
        var wantsNew = _settings.Store.EnableHardwareSensors && _settings.Store.UseNewSensorDashboard
                       && PawnIOHelper.GetPawnIOState() == PawnIOState.Installed;
        return wantsNew ? sensorControl is SensorsControlV2 : sensorControl is SensorsControl;
    }

    private void RefreshSensorControl()
    {
        if (sensorControl != null)
        {
            _panel.Children.Remove(sensorControl);
        }

        if (!_settings.Store.EnableHardwareSensors || !_settings.Store.UseNewSensorDashboard)
        {
            sensorControl = new SensorsControl();
        }
        else if (PawnIOHelper.GetPawnIOState() == PawnIOState.Installed)
        {
            sensorControl = new SensorsControlV2();
        }
        else
        {
            PawnIOHelper.ShowPawnIONotify();
            sensorControl = new SensorsControl();
        }

        int contentIndex = _panel.Children.IndexOf(_content);
        if (contentIndex == -1) contentIndex = 0;

        sensorControl.Margin = new Thickness(0, 16, 16, 0);
        _panel.Children.Insert(contentIndex, sensorControl);

        if (_dashboardSettings != null && _loader != null && !_loader.IsLoading)
        {
            sensorControl.Visibility = _dashboardSettings.Store.ShowSensors
                ? Visibility.Visible
                : Visibility.Collapsed;
            _usageOverview.Visibility = _dashboardSettings.Store.ShowSensors ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void EnsureConfigFilesExist()
    {
        if (_settings.Store.EnableHardwareSensors && _settings.Store.UseNewSensorDashboard)
        {
            _sensorsControlSettings.EnsureFileExists();
            _hardwareSensorSettings.EnsureFileExists();
        }
    }

    private async void DashboardPage_Initialized(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _loader.IsLoading = true;

        try
        {
            ScrollHost?.ScrollToTop();

            _statusArea.Visibility = _settings.Store.ShowStatusStrip ? Visibility.Visible : Visibility.Collapsed;

            if (sensorControl != null)
            {
                sensorControl.Visibility = _dashboardSettings.Store.ShowSensors
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                _usageOverview.Visibility = _dashboardSettings.Store.ShowSensors ? Visibility.Visible : Visibility.Collapsed;
            }

            _dashboardGroupControls.Clear();
            _content.ColumnDefinitions.Clear();
            _content.RowDefinitions.Clear();
            _content.Children.Clear();

            var groups = _dashboardSettings.Store.Groups ?? DashboardGroup.DefaultGroups;

            Log.Instance.Trace($"Groups:");
            foreach (var group in groups)
            {
                Log.Instance.Trace($" - {group}");
            }

            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });

            var initializationTasks = new List<Task>();
            var controls = new List<DashboardGroupControl>();

            foreach (var group in groups)
            {
                _content.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Auto) });

                var control = new DashboardGroupControl(group);
                _content.Children.Add(control);
                controls.Add(control);
                _dashboardGroupControls.Add(control);
            }

            foreach (var control in controls)
            {
                initializationTasks.Add(control.InitializedTask);
            }

            _content.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Auto) });

            var editDashboardHyperlink = new Hyperlink
            {
                Icon = SymbolRegular.Edit24,
                Content = Resource.DashboardPage_Customize,
                Margin = new(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            editDashboardHyperlink.Click += (_, _) =>
            {
                var window = new EditDashboardWindow { Owner = Window.GetWindow(this) };
                window.Apply += async (_, _) => await RefreshAsync();
                window.ShowDialog();
            };

            Grid.SetRow(editDashboardHyperlink, groups.Length);
            Grid.SetColumn(editDashboardHyperlink, 0);
            Grid.SetColumnSpan(editDashboardHyperlink, 2);

            _content.Children.Add(editDashboardHyperlink);

            LayoutGroups(ActualWidth);

            await Task.WhenAll(initializationTasks);

            App.MainWindowInstance!.SetVisual();

            PlayEntrance();
        }
        catch (Exception ex)
        {
            // Called from event handlers (page start, Customize → Apply), where an error used to reach the app's
            // "Unexpected exception" message.
            Log.Instance.Trace($"Building the dashboard failed.", ex);
        }
        finally
        {
            _loader.IsLoading = false;
        }
    }

    /// <summary>
    /// Status strip, then sensors, then each group, 40 ms apart, as the loaded dashboard appears.
    /// </summary>
    private void PlayEntrance()
    {
        var elements = new List<UIElement>();
        if (_statusArea.Visibility == Visibility.Visible)
            elements.Add(_statusArea);
        if (_usageOverview.Visibility == Visibility.Visible)
            elements.Add(_usageOverview);
        if (sensorControl is { Visibility: Visibility.Visible })
            elements.Add(sensorControl);
        elements.AddRange(_dashboardGroupControls);

        for (var i = 0; i < elements.Count; i++)
            Motion.FadeSlideIn(elements[i], TimeSpan.FromMilliseconds(40 * i));
    }

    private void DashboardPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;

        LayoutGroups(e.NewSize.Width);
    }

    private void LayoutGroups(double width)
    {
        if (width > 1000)
            Expand();
        else
            Collapse();
    }

    private void Expand()
    {
        var lastColumn = _content.ColumnDefinitions.LastOrDefault();
        if (lastColumn is not null)
            lastColumn.Width = new(1, GridUnitType.Star);

        for (var index = 0; index < _dashboardGroupControls.Count; index++)
        {
            var control = _dashboardGroupControls[index];
            Grid.SetRow(control, index - (index % 2));
            Grid.SetColumn(control, index % 2);
        }
    }

    private void Collapse()
    {
        var lastColumn = _content.ColumnDefinitions.LastOrDefault();
        if (lastColumn is not null)
            lastColumn.Width = new(0, GridUnitType.Pixel);

        for (var index = 0; index < _dashboardGroupControls.Count; index++)
        {
            var control = _dashboardGroupControls[index];
            Grid.SetRow(control, index);
            Grid.SetColumn(control, 0);
        }
    }
}
