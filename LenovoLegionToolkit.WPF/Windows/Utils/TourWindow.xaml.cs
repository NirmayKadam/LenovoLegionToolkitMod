using System;
using System.Collections.Generic;
using System.Windows;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

/// <summary>
/// The first-run tour: a few steps, each with a "Show me" that opens the page in the main window. It doesn't block
/// the main window, so the page can be looked at while the tour stays open. Only one is open at a time.
/// </summary>
public partial class TourWindow
{
    private static TourWindow? _open;

    private readonly IReadOnlyList<TourStep> _steps = TourSteps.All;
    private int _index;

    private TourWindow()
    {
        InitializeComponent();
        ShowStep();
    }

    /// <summary>Opens the tour over <paramref name="owner"/>, or brings the open one to the front.</summary>
    public static void Open(Window owner)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }

        _open = new TourWindow { Owner = owner };
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    private TourStep Step => _steps[_index];

    private bool IsLast => _index == _steps.Count - 1;

    private void ShowStep()
    {
        _icon.Symbol = Step.Icon;
        _counter.Text = string.Format(Resource.Tour_StepOf, _index + 1, _steps.Count);
        _title.Text = Step.Title;
        _text.Text = Step.Text;
        _showMeButton.Visibility = Visibility.Visible;
        _backButton.IsEnabled = _index > 0;
        _skipButton.Visibility = IsLast ? Visibility.Hidden : Visibility.Visible;
        _nextButton.Content = IsLast ? Resource.Tour_Finish : Resource.Tour_Next;
    }

    private void ShowMeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Owner is MainWindow mainWindow && mainWindow.NavigateTo(Step.PageTag, Step.SettingsTab))
                return;

            _showMeButton.Visibility = Visibility.Collapsed;   // this laptop doesn't have that page
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Tour: couldn't open {Step.PageTag}.", ex);
            _showMeButton.Visibility = Visibility.Collapsed;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_index == 0)
            return;

        _index--;
        ShowStep();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsLast)
        {
            Close();
            return;
        }

        _index++;
        ShowStep();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e) => Close();
}
