using System;
using System.Windows;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Controls;

/// <summary>
/// Shows the change that can be undone right now (<see cref="UndoSlot"/>), the seconds left and an Undo button,
/// then what the undo came to. Every open bar shows the same slot; the one in the window in front is the one that
/// can be clicked. Its once-a-second timer runs only while there is something to show.
/// </summary>
public partial class UndoBarControl
{
    private readonly UndoSlot _slot = IoCContainer.Resolve<UndoSlot>();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };

    public UndoBarControl()
    {
        InitializeComponent();

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            _slot.Changed += Slot_Changed;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _slot.Changed -= Slot_Changed;
            _timer.Stop();
        };
    }

    private void Slot_Changed(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        try
        {
            var view = _slot.Current;
            if (view.Status == UndoStatus.None)
            {
                _timer.Stop();
                Visibility = Visibility.Collapsed;
                return;
            }

            _timer.Start();
            Visibility = Visibility.Visible;

            _text.Text = view.Status switch
            {
                UndoStatus.Pending => view.Description,
                UndoStatus.Running => Resource.Undo_Running,
                UndoStatus.Done => Resource.Undo_Done,
                _ => Resource.Undo_NotAllApplied,
            };
            _text.SetResourceReference(ForegroundProperty, view.Status == UndoStatus.NotAllApplied ? "SystemFillColorCautionBrush" : "TextFillColorPrimaryBrush");

            _undoButton.Visibility = view.Status is UndoStatus.Pending or UndoStatus.Running ? Visibility.Visible : Visibility.Collapsed;
            _undoButton.IsEnabled = view.Status == UndoStatus.Pending;
            _undoButton.Content = string.Format(Resource.Undo_Button, Math.Max(1, (int)Math.Ceiling(view.Remaining.TotalSeconds)));
        }
        catch (Exception ex)
        {
            _timer.Stop();
            Log.Instance.Trace($"Undo bar update failed.", ex);
        }
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _slot.TryUndoAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Undo failed.", ex);
        }

        Refresh();
    }
}
