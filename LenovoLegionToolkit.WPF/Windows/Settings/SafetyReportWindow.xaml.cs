using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

/// <summary>
/// The read-only safety report (<see cref="SafetyReport"/>). It is read when the window opens and on Refresh, off
/// the UI thread (Task Scheduler and folder permissions can take a moment); nothing here changes the laptop.
/// </summary>
public partial class SafetyReportWindow
{
    private IReadOnlyList<SafetyRow> _rows = [];
    private DateTime _generated;

    public SafetyReportWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _refreshButton.IsEnabled = false;
        _copyButton.IsEnabled = false;
        _statusText.Text = string.Empty;
        try
        {
            var inputs = await Task.Run(SafetyReportCollector.Collect);
            _rows = SafetyReport.Build(inputs);
            _generated = DateTime.Now;
            ShowRows();
            _copyButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Safety report failed.", ex);
            _statusText.Text = Resource.SafetyReport_Unknown;
        }
        finally
        {
            _refreshButton.IsEnabled = true;
        }
    }

    private void ShowRows()
    {
        _rowsPanel.Children.Clear();

        foreach (var section in _rows.GroupBy(r => r.Section))
        {
            _rowsPanel.Children.Add(new TextBlock
            {
                Text = SafetyReport.SectionTitle(section.Key),
                Margin = new Thickness(0, _rowsPanel.Children.Count == 0 ? 4 : 16, 0, 6),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
            });

            foreach (var row in section)
                _rowsPanel.Children.Add(RowElement(row));
        }
    }

    private static Grid RowElement(SafetyRow row)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var item = new TextBlock { Text = row.Item, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        item.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        var state = new TextBlock { Text = row.State, TextWrapping = TextWrapping.Wrap };
        state.SetResourceReference(ForegroundProperty, row.Attention ? "SystemFillColorCautionBrush" : "TextFillColorPrimaryBrush");
        if (row.Attention)
            state.FontWeight = FontWeights.SemiBold;
        Grid.SetColumn(state, 1);

        grid.Children.Add(item);
        grid.Children.Add(state);
        return grid;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SafetyReport.ToText(_rows, _generated));
            _statusText.Text = Resource.SafetyReport_Copied;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Copying the safety report failed.", ex);
            _statusText.Text = Resource.SafetyReport_CopyFailed;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
