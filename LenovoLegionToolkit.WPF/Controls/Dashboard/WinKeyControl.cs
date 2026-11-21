using System;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class WinKeyControl : AbstractToggleFeatureCardControl<WinKeyState>
{
    private readonly WinKeyListener _listener = IoCContainer.Resolve<WinKeyListener>();

    protected override WinKeyState OnState => WinKeyState.On;

    protected override WinKeyState OffState => WinKeyState.Off;

    protected override bool DisablesWhileRefreshing => false;

    public WinKeyControl()
    {
        Icon = SymbolRegular.Keyboard24;
        Title = Resource.WinKeyControl_Title;
        Subtitle = Resource.WinKeyControl_Message;

        this.WhileLoaded(() => _listener.Changed += Listener_Changed, () => _listener.Changed -= Listener_Changed);
    }

    private void Listener_Changed(object? sender, EventArgs e) => Dispatcher.Post(async () =>
    {
        if (!IsLoaded || !IsVisible)
            return;

        await RefreshAsync();
    });
}
