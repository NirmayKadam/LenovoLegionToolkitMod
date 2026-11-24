using System;
using System.Linq;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Macro;
using LenovoLegionToolkit.WPF.Extensions;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class MacroPage
{
    private readonly MacroController _controller = IoCContainer.Resolve<MacroController>();

    public MacroPage()
    {
        Initialized += MacroPage_Initialized;

        InitializeComponent();
    }

    private void Controller_StateChanged(object? sender, EventArgs e) => Dispatcher.Post(() => _enableMacroToggle.IsChecked = _controller.IsEnabled);

    private void MacroPage_Initialized(object? sender, EventArgs e)
    {
        // Only while shown: the controller lives as long as the app, and kept every replaced page (and window) alive.
        this.WhileLoaded(() =>
        {
            _controller.StateChanged += Controller_StateChanged;
            _enableMacroToggle.IsChecked = _controller.IsEnabled;   // may have changed while the page was away
        }, () => _controller.StateChanged -= Controller_StateChanged);

        _enableMacroToggle.IsChecked = _controller.IsEnabled;

        var zeroNumberButton = _numberPad.Children.OfType<Button>().Last();
        Reload(zeroNumberButton);
    }

    private void EnableMacroToggle_Click(object sender, RoutedEventArgs e)
    {
        _controller.SetEnabled(_enableMacroToggle.IsChecked ?? false);
    }

    private void NumberPadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        _numberPad.Children
            .OfType<Button>()
            .ForEach(b => b.Appearance = ControlAppearance.Secondary);

        Reload(button);
    }

    private void Reload(Button button)
    {
        button.Appearance = ControlAppearance.Primary;

        var key = Convert.ToUInt64((string)button.Tag, 16);
        _sequenceControl.Set(new(MacroSource.Keyboard, key));
    }
}
