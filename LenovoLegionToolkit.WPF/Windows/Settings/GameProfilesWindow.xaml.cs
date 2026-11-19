using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Microsoft.Win32;
using Wpf.Ui.Common;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

/// <summary>
/// Game profiles: per game, what LLT does while it runs (game-safe mode, easing or closing listed apps, pausing
/// listed services). Before a profile first changes other apps or services, it asks. Protected services can't be
/// added; the list shows why. Changes are saved at once and the controller picks them up.
/// </summary>
public partial class GameProfilesWindow
{
    private readonly GameProfileSettings _settings = IoCContainer.Resolve<GameProfileSettings>();
    private readonly GameProfileController _controller = IoCContainer.Resolve<GameProfileController>();

    private bool _showing;

    /// <summary>One entry of the power-mode list: a mode, or null for "don't change".</summary>
    private sealed record PowerModeChoice(PowerModeState? Mode, string Name);

    public GameProfilesWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await LoadPowerModeChoicesAsync();
            Refresh(null);
        };
        _controller.Reported += Controller_Reported;
        Closed += (_, _) => _controller.Reported -= Controller_Reported;
    }

    private GameProfile? Selected => _gamesList.SelectedItem as GameProfile;

    private void Controller_Reported(object? sender, GameProfileReport e) => Dispatcher.InvokeAsync(ShowStatus);

    /// <summary>Game mode (any game) first, then the per-game profiles.</summary>
    private void Refresh(Guid? select)
    {
        var profiles = _settings.Store.Profiles.Prepend(_settings.Store.GameMode).ToList();
        _gamesList.ItemsSource = profiles;
        _gamesList.SelectedItem = profiles.FirstOrDefault(p => p.Id == select) ?? profiles.FirstOrDefault();
        ShowSelected();
        ShowStatus();
    }

    private void ShowStatus()
    {
        var now = _controller.ActiveGameName is { } game
            ? string.Format(Resource.GameProfiles_Status_Active, game)
            : Resource.GameProfiles_Status_Idle;

        _statusText.Text = _controller.LastReport is { } last ? $"{now} {GameProfileText.Summary(last)}" : now;
    }

    private void ShowSelected()
    {
        _showing = true;
        try
        {
            var profile = Selected;
            _removeGameButton.IsEnabled = profile is { IsGameMode: false };
            _noGameText.Visibility = profile is null ? Visibility.Visible : Visibility.Collapsed;
            _profilePanel.Visibility = profile is null ? Visibility.Collapsed : Visibility.Visible;
            if (profile is null)
                return;

            _gamePathText.Text = profile.IsGameMode ? Resource.GameProfiles_GameMode_Explanation : profile.ExePath ?? $"{profile.ExeName}.exe";
            _gamePathText.TextWrapping = profile.IsGameMode ? TextWrapping.Wrap : TextWrapping.NoWrap;
            _enabledToggle.IsChecked = profile.Enabled;
            _forceGameSafeToggle.IsChecked = profile.ForceGameSafe;
            _powerModeComboBox.SelectedItem = _powerModeComboBox.Items.OfType<PowerModeChoice>().FirstOrDefault(c => c.Mode == profile.PowerMode)
                                              ?? _powerModeComboBox.Items.OfType<PowerModeChoice>().FirstOrDefault();

            FillChips(_easeList, profile.EfficiencyApps, AppRules.IsProtectedName);
            FillChips(_closeList, profile.CloseApps, AppRules.IsProtectedName);
            FillChips(_serviceList, profile.PauseServices, name => ServiceRules.Protected.Contains(name));
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>
    /// One removable chip per listed app or service. Entries LLT will never act on (<paramref name="isOffLimits"/>,
    /// e.g. Windows' own apps added before the picker left them out) are shown in the caution colour and say so.
    /// </summary>
    private void FillChips(WrapPanel panel, List<string> items, Func<string, bool> isOffLimits)
    {
        panel.Children.Clear();
        foreach (var item in items.ToList())
        {
            var remove = new Button
            {
                Appearance = ControlAppearance.Transparent,
                Icon = SymbolRegular.Dismiss16,
                Padding = new Thickness(4),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = string.Format(Resource.GameProfiles_RemoveItem, item),
            };
            System.Windows.Automation.AutomationProperties.SetName(remove, string.Format(Resource.GameProfiles_RemoveItem, item));
            remove.Click += (_, _) =>
            {
                items.Remove(item);
                Save();
                ShowSelected();
            };

            var offLimits = isOffLimits(item);
            var label = new TextBlock { Text = item, VerticalAlignment = VerticalAlignment.Center };
            if (offLimits)
                label.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");

            var chip = new StackPanel { Orientation = Orientation.Horizontal };
            chip.Children.Add(label);
            chip.Children.Add(remove);

            var border = new Border
            {
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 2, 2, 2),
                BorderThickness = new Thickness(1),
                Child = chip,
                ToolTip = offLimits ? Resource.GameProfiles_NotTouched : null,
            };
            border.SetResourceReference(Border.BorderBrushProperty, offLimits ? "SystemFillColorCautionBrush" : "CardStrokeColorDefaultBrush");
            border.SetResourceReference(Border.CornerRadiusProperty, "ControlCornerRadius");
            panel.Children.Add(border);
        }
    }

    /// <summary>Saves and lets the controller pick up the change (it puts back a running profile that was turned off).</summary>
    private void Save()
    {
        _settings.SynchronizeStore();
        _ = Task.Run(_controller.RefreshAsync);
    }

    /// <summary>
    /// Before a profile first changes other apps or services, says exactly what will happen and asks.
    /// Returns false when the user said no.
    /// </summary>
    private async Task<bool> ConfirmChangesAsync(GameProfile profile, int ease, int close, int services)
    {
        if (profile.ChangesConfirmed || (ease == 0 && close == 0 && services == 0))
            return true;

        var message = string.Format(Resource.GameProfiles_Confirm_Message, profile.Name, ease, close, services);
        if (!await MessageBoxHelper.ShowAsync(this, Resource.GameProfiles_Confirm_Title, message, Resource.Yes, Resource.No))
            return false;

        profile.ChangesConfirmed = true;
        return true;
    }

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelected();

    /// <summary>
    /// "Add game": the games Windows has recognised on this PC, programs running now, or Browse… The first two give
    /// the game's own program (not its launcher) without browsing.
    /// </summary>
    private async void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        var profiled = _settings.Store.Profiles.Select(p => p.ExeName).ToList();

        IReadOnlyList<(string Name, string Path)> known;
        IReadOnlyList<string> running;
        try
        {
            known = await Task.Run(() => GameProfileCatalog.KnownGames(profiled));
            running = await Task.Run(() => GameProfileCatalog.RunningApps(profiled));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't list games.", ex);
            known = [];
            running = [];
        }

        var menu = new ContextMenu { PlacementTarget = sender as UIElement, MaxHeight = 480 };

        var knownMenu = new MenuItem { Header = Resource.GameProfiles_AddGame_Known };
        foreach (var (name, path) in known)
        {
            var item = new MenuItem { Header = name, ToolTip = path };
            item.Click += (_, _) => AddGame(name, path);
            knownMenu.Items.Add(item);
        }
        if (known.Count == 0)
            knownMenu.Items.Add(new MenuItem { Header = Resource.GameProfiles_AddGame_NoneKnown, IsEnabled = false });
        menu.Items.Add(knownMenu);

        var runningMenu = new MenuItem { Header = Resource.GameProfiles_AddGame_Running };
        foreach (var name in running)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => AddGame(name, null);
            runningMenu.Items.Add(item);
        }
        if (running.Count == 0)
            runningMenu.Items.Add(new MenuItem { Header = Resource.GameProfiles_AddGame_NoneRunning, IsEnabled = false });
        menu.Items.Add(runningMenu);

        var browse = new MenuItem { Header = Resource.GameProfiles_Browse };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = $"{Resource.GameProfiles_ExeFilter}|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true)
                AddGame(Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName);
        };
        menu.Items.Add(browse);

        menu.IsOpen = true;
    }

    private void AddGame(string exeName, string? path)
    {
        if (string.Equals(exeName, AppRules.LltProcessName, StringComparison.OrdinalIgnoreCase) || AppRules.IsProtectedName(exeName))
            return;

        var existing = _settings.Store.Profiles.FirstOrDefault(p => string.Equals(p.ExeName, exeName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new GameProfile { Name = exeName, ExeName = exeName, ExePath = path };
            _settings.Store.Profiles.Add(existing);
            Save();
        }

        Refresh(existing.Id);
    }

    private async void RemoveGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { IsGameMode: false } profile)
            return;

        if (!await MessageBoxHelper.ShowAsync(this, Resource.GameProfiles_RemoveGame, string.Format(Resource.GameProfiles_RemoveGame_Confirm, profile.Name), Resource.Yes, Resource.No))
            return;

        _settings.Store.Profiles.Remove(profile);
        Save();
        Refresh(null);
    }

    private async void EnabledToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_showing || Selected is not { } profile)
            return;

        var enable = _enabledToggle.IsChecked == true;
        if (enable && !await ConfirmChangesAsync(profile, profile.EfficiencyApps.Count, profile.CloseApps.Count, profile.PauseServices.Count))
        {
            _enabledToggle.IsChecked = false;
            return;
        }

        profile.Enabled = enable;
        Save();
    }

    private void ForceGameSafeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_showing || Selected is not { } profile)
            return;

        profile.ForceGameSafe = _forceGameSafeToggle.IsChecked == true;
        Save();
    }

    /// <summary>"Don't change" plus the modes this laptop offers. Only "Don't change" when the modes can't be read.</summary>
    private async Task LoadPowerModeChoicesAsync()
    {
        var choices = new List<PowerModeChoice> { new(null, Resource.GameProfiles_PowerMode_DontChange) };
        try
        {
            var feature = IoCContainer.Resolve<Lib.Features.PowerModeFeature>();
            if (await feature.IsSupportedAsync())
                choices.AddRange((await feature.GetAllStatesAsync()).Select(mode => new PowerModeChoice(mode, mode.GetDisplayName())));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the power modes for game profiles.", ex);
        }

        _powerModeComboBox.ItemsSource = choices;
    }

    private void PowerModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || Selected is not { } profile || _powerModeComboBox.SelectedItem is not PowerModeChoice choice)
            return;

        profile.PowerMode = choice.Mode;
        Save();
    }

    private async Task AddItemAsync(GameProfile profile, List<string> list, string item)
    {
        if (list.Contains(item, StringComparer.OrdinalIgnoreCase))
            return;

        var ease = profile.EfficiencyApps.Count + (list == profile.EfficiencyApps ? 1 : 0);
        var close = profile.CloseApps.Count + (list == profile.CloseApps ? 1 : 0);
        var services = profile.PauseServices.Count + (list == profile.PauseServices ? 1 : 0);
        if (profile.Enabled && !await ConfirmChangesAsync(profile, ease, close, services))
            return;

        list.Add(item);
        Save();
        ShowSelected();
    }

    private async void AddEaseButton_Click(object sender, RoutedEventArgs e) => await ShowAppMenuAsync(_addEaseButton, p => p.EfficiencyApps);

    private async void AddCloseButton_Click(object sender, RoutedEventArgs e) => await ShowAppMenuAsync(_addCloseButton, p => p.CloseApps);

    /// <summary>
    /// Running apps with a window that a profile may touch (never a profiled game, Windows' own apps, or an app
    /// already in either list), plus "Browse…" for an app that isn't running.
    /// </summary>
    private async Task ShowAppMenuAsync(Button anchor, Func<GameProfile, List<string>> listOf)
    {
        if (Selected is not { } profile)
            return;

        var exclude = _settings.Store.Profiles.Select(p => p.ExeName)
            .Concat(profile.EfficiencyApps)
            .Concat(profile.CloseApps)
            .ToList();

        IReadOnlyList<string> apps;
        try
        {
            apps = await Task.Run(() => GameProfileCatalog.RunningApps(exclude));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't list running apps.", ex);
            apps = [];
        }

        var list = listOf(profile);
        var menu = new ContextMenu { PlacementTarget = anchor, MaxHeight = 420 };

        var browse = new MenuItem { Header = Resource.GameProfiles_Browse };
        browse.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = $"{Resource.GameProfiles_ExeFilter}|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true)
                return;

            var app = Path.GetFileNameWithoutExtension(dialog.FileName);
            if (AppRules.IsProtectedName(app) || !AppRules.IsAllowedPath(dialog.FileName) || GameProfileController.IsGame(app, profile.ExeName))
            {
                await MessageBoxHelper.ShowAsync(this, Resource.GameProfiles_Title, string.Format(Resource.GameProfiles_AppNotAllowed, app), Resource.OK);
                return;
            }

            if (profile.EfficiencyApps.Concat(profile.CloseApps).Contains(app, StringComparer.OrdinalIgnoreCase))
            {
                await MessageBoxHelper.ShowAsync(this, Resource.GameProfiles_Title, string.Format(Resource.GameProfiles_AppAlreadyListed, app), Resource.OK);
                return;
            }

            await AddItemAsync(profile, list, app);
        };
        menu.Items.Add(browse);
        menu.Items.Add(new Separator());

        foreach (var app in apps)
        {
            var item = new MenuItem { Header = app };
            item.Click += async (_, _) => await AddItemAsync(profile, list, app);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    /// <summary>Running services; the ones LLT won't pause are shown greyed with the reason.</summary>
    private async void AddServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile)
            return;

        IReadOnlyList<ServiceInfo> services;
        try
        {
            services = await Task.Run(GameProfileCatalog.RunningServices);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't list services.", ex);
            services = [];
        }

        var menu = new ContextMenu { PlacementTarget = _addServiceButton, MaxHeight = 420 };

        var presets = new MenuItem { Header = Resource.GameProfiles_AddPreset };
        foreach (var preset in ServicePresets.All)
        {
            var title = Resource.ResourceManager.GetString($"ServicePreset_{preset.Id}_Title") ?? preset.Id;
            var item = new MenuItem
            {
                Header = title,
                ToolTip = $"{Resource.ResourceManager.GetString($"ServicePreset_{preset.Id}_Note")} ({string.Join(", ", preset.Services)})",
            };
            item.Click += async (_, _) => await AddPresetAsync(profile, preset);
            presets.Items.Add(item);
        }
        menu.Items.Add(presets);
        menu.Items.Add(new Separator());

        foreach (var service in services.Where(s => !profile.PauseServices.Contains(s.Name, StringComparer.OrdinalIgnoreCase)))
        {
            var refusal = ServiceRules.CheckForList(service.Name, service);
            var item = new MenuItem
            {
                Header = refusal is { } reason
                    ? $"{service.DisplayName} ({service.Name}) - {GameProfileText.Refusal(reason)}"
                    : $"{service.DisplayName} ({service.Name})",
                IsEnabled = refusal is null,
            };
            item.Click += async (_, _) => await AddItemAsync(profile, profile.PauseServices, service.Name);
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 2)
            menu.Items.Add(new MenuItem { Header = Resource.GameProfiles_NoServices, IsEnabled = false });

        menu.IsOpen = true;
    }

    /// <summary>
    /// Adds a preset's services in one go: the ones installed here that LLT may stop (also when not running now),
    /// and not already listed. Asks first, like any other change, when the profile is on.
    /// </summary>
    private async Task AddPresetAsync(GameProfile profile, ServicePreset preset)
    {
        IReadOnlyDictionary<string, ServiceInfo?> found;
        try
        {
            found = await Task.Run(() => GameProfileCatalog.ReadServices(preset.Services));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the preset's services.", ex);
            return;
        }

        var toAdd = preset.Services
            .Where(name => !profile.PauseServices.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Where(name => found.GetValueOrDefault(name) is { } info && ServiceRules.CheckForList(name, info) is null)
            .ToList();

        if (toAdd.Count == 0)
        {
            _statusText.Text = Resource.GameProfiles_PresetNothingToAdd;
            return;
        }

        if (profile.Enabled && !await ConfirmChangesAsync(profile, profile.EfficiencyApps.Count, profile.CloseApps.Count, profile.PauseServices.Count + toAdd.Count))
            return;

        profile.PauseServices.AddRange(toAdd);
        Save();
        ShowSelected();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
