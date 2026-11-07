using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib.Station.Services;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Station.Services;

public sealed class UiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action) => Application.Current.Dispatcher.InvokeAsync(action).Task;

    public async Task<T> InvokeAsync<T>(Func<T> action) => await Application.Current.Dispatcher.InvokeAsync(action);
}
