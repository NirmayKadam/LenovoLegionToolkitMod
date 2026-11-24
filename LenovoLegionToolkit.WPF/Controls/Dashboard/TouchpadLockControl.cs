using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class TouchpadLockControl : AbstractToggleFeatureCardControl<TouchpadLockState>
{
    private readonly DriverKeyListener _listener = IoCContainer.Resolve<DriverKeyListener>();

    protected override TouchpadLockState OnState => TouchpadLockState.On;

    protected override TouchpadLockState OffState => TouchpadLockState.Off;

    public TouchpadLockControl()
    {
        Icon = SymbolRegular.Tablet24;
        Title = Resource.TouchpadLockControl_Title;
        Subtitle = Resource.TouchpadLockControl_Message;

        this.WhileLoaded(() => _listener.Changed += Listener_Changed, () => _listener.Changed -= Listener_Changed);
    }

    private void Listener_Changed(object? sender, DriverKeyListener.ChangedEventArgs e) => Dispatcher.Post(async () =>
    {
        if (!IsLoaded || !IsVisible)
            return;

        if (e.DriverKey.HasFlag(DriverKey.FnF10))
            await RefreshAsync();
    });
}
