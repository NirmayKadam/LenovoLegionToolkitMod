using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.GameSessions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Microsoft.Win32;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

/// <summary>
/// The last game sessions, newest first, with their details and GPU driver errors, and an export to CSV. It only
/// writes the file the user picks.
/// </summary>
public partial class GameSessionsWindow
{
    public sealed record SessionRow(string Summary, IReadOnlyList<string> Details);

    private readonly List<GameSessionReport> _sessions;

    public GameSessionsWindow()
    {
        InitializeComponent();
        Title = _title.Text = Resource.GameSessions_WindowTitle;

        var unit = IoCContainer.Resolve<ApplicationSettings>().Store.TemperatureUnit;
        _sessions = [.. IoCContainer.Resolve<GameSessionSettings>().Store.Sessions];
        var rows = _sessions
            .Select(s => new SessionRow(GameSessionText.Summary(s, unit), GameSessionText.Details(s)))
            .ToList();

        _sessionList.ItemsSource = rows;
        _empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _exportButton.IsEnabled = rows.Count > 0;
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "indra-game-sessions.csv",
            DefaultExt = ".csv",
            Filter = "CSV (*.csv)|*.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            // UTF-8 with BOM, so Excel reads "°C" correctly.
            File.WriteAllText(dialog.FileName, GameSessionCsv.Build(_sessions), new UTF8Encoding(true));
            _exportResult.Text = string.Format(Resource.GameSessions_Exported, dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Log.Instance.Trace($"Exporting game sessions failed.", ex);
            _exportResult.Text = string.Format(Resource.GameSessions_ExportFailed, ex.Message);
        }

        _exportResult.Visibility = Visibility.Visible;
    }
}
