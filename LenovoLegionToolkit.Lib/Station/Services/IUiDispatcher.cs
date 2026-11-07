using System;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Station.Services;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
    Task<T> InvokeAsync<T>(Func<T> action);
}
