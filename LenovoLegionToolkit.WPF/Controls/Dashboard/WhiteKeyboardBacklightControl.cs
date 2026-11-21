using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class WhiteKeyboardBacklightControl : AbstractComboBoxFeatureCardControl<WhiteKeyboardBacklightState>
{
    private readonly DriverKeyListener _listener = IoCContainer.Resolve<DriverKeyListener>();

    public WhiteKeyboardBacklightControl()
    {
        Icon = SymbolRegular.Keyboard24;
        Title = Resource.WhiteKeyboardBacklightControl_Title;
        Subtitle = Resource.WhiteKeyboardBacklightControl_Message;

        this.WhileLoaded(() => _listener.Changed += ListenerChanged, () => _listener.Changed -= ListenerChanged);
    }

    private void ListenerChanged(object? sender, DriverKeyListener.ChangedEventArgs e) => Dispatcher.Post(async () =>
    {
        if (!IsLoaded || !IsVisible)
            return;

        if (e.DriverKey.HasFlag(DriverKey.FnSpace))
            await RefreshAsync();
    });
}
