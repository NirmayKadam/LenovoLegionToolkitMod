using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.AutoListeners;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Windows.Settings;

namespace LenovoLegionToolkit.WPF.Controls.Games;

/// <summary>How running games are recognised, and which programs are never treated as games.</summary>
public partial class GameDetectionControl
{
    private static readonly (GameDetectionMode Mode, string Text)[] Modes =
    [
        (GameDetectionMode.Auto, Resource.AutomationPage_Game_Detection_Method_Auto),
        (GameDetectionMode.DiscreteGpu, Resource.AutomationPage_Game_Detection_Method_Discrete_GPU),
        (GameDetectionMode.GameList, Resource.AutomationPage_Game_Detection_Method_List),
        (GameDetectionMode.GameMode, Resource.AutomationPage_Game_Detection_Method_Game_Mode),
        (GameDetectionMode.Manual, Resource.AutomationPage_Game_Detection_Method_Manual)
    ];

    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();

    private bool _showingSettings;

    public GameDetectionControl()
    {
        InitializeComponent();

        foreach (var (mode, text) in Modes)
            _modeComboBox.Items.Add(new ComboBoxItem { Content = text, Tag = mode });
    }

    /// <summary>Shows the saved mode; it may have been changed in the other place this card appears.</summary>
    protected override Task OnRefreshAsync()
    {
        var mode = GameDetectionModes.From(_settings.Store.GameDetection);

        _showingSettings = true;
        try
        {
            _modeComboBox.SelectedItem = _modeComboBox.Items.OfType<ComboBoxItem>().First(i => i.Tag is GameDetectionMode m && m == mode);
        }
        finally
        {
            _showingSettings = false;
        }

        return Task.CompletedTask;
    }

    protected override void OnFinishedLoading() { }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showingSettings || !IsLoaded || _modeComboBox.SelectedItem is not ComboBoxItem { Tag: GameDetectionMode mode })
            return;

        if (GameDetectionModes.From(_settings.Store.GameDetection) == mode)
            return;

        GameDetectionModes.Apply(mode, _settings.Store.GameDetection);
        _settings.SynchronizeStore();

        Log.Instance.Trace($"Game detection mode changed to {mode}.");

        // The game listener and the automations' listeners read the mode when they start.
        _ = Task.Run(async () =>
        {
            try
            {
                var gameAutoListener = IoCContainer.Resolve<GameAutoListener>();
                if (gameAutoListener.AreGamesRunning())
                    gameAutoListener.PreserveStateOnRestart();

                await gameAutoListener.RestartAsync().ConfigureAwait(false);
                await IoCContainer.Resolve<AutomationProcessor>().RestartListenersAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to restart listeners after detection mode change.", ex);
            }
        });
    }

    private void ExcludeProcesses_Click(object sender, RoutedEventArgs e) =>
        new ExcludeProcessesWindow { Owner = Window.GetWindow(this) }.ShowDialog();
}
