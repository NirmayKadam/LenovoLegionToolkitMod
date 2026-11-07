using System;

namespace LenovoLegionToolkit.Lib.Station.Logging;

public interface IExtensionLogger
{
    void Trace(string message);
    void Error(string message, Exception exception);
}
