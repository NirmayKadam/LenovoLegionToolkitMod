using System;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Station.Core;

public interface IExtensionProvider : IAsyncDisposable
{
    void Initialize(IExtensionContext context);
    Task ExecuteAsync(string action, params object[] args);
    object? GetData(string key);
    void SetData(string key, object? value);
}
