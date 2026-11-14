using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    /// <summary>
    /// Runs a WMI query and turns each result into a <typeparamref name="T"/>, releasing every WMI object as soon as
    /// it has been read: the searcher, the result collection and each result. Left to the garbage collector, these
    /// COM objects piled up and the finalizer thread spent as much time releasing them as a busy thread (seen in a
    /// trace with the dashboard open). <paramref name="convert"/> must copy what it needs; the object is released
    /// right after it returns. The results come back as a list, never as a lazy sequence over released objects.
    /// </summary>
    internal static Task<List<T>> QueryAsync<T>(string scope, string query, Func<ManagementBaseObject, T> convert) => Task.Run(() =>
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var collection = searcher.Get();

        var results = new List<T>();
        foreach (var item in collection)
        {
            using (item)
                results.Add(convert(item));
        }
        return results;
    });

    /// <summary>
    /// Calls a WMI method on the first object the query finds, releasing the query objects, the method's input
    /// parameters and its result once <paramref name="readResult"/> has copied what it needs.
    /// </summary>
    private static Task<T> InvokeAsync<T>(string scope, string query, string methodName, Dictionary<string, object> methodParams, Func<ManagementBaseObject?, T> readResult) => Task.Run(() =>
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var collection = searcher.Get();

        ManagementObject? target = null;
        try
        {
            foreach (var item in collection)
            {
                if (target is null && item is ManagementObject mo)
                    target = mo;
                else
                    item.Dispose();
            }

            if (target is null)
                throw new InvalidOperationException("No results in query");

            using var inParams = target.GetMethodParameters(methodName);
            foreach (var pair in methodParams)
                inParams[pair.Key] = pair.Value;

            using var outParams = target.InvokeMethod(methodName, inParams, new InvokeMethodOptions());
            return readResult(outParams);
        }
        finally
        {
            target?.Dispose();
        }
    });

    private static async Task<bool> ExistsAsync(string scope, FormattableString query)
    {
        try
        {
            var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);
            var found = await QueryAsync(scope, queryFormatted, _ => true).ConfigureAwait(false);
            return found.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static LambdaDisposable Listen(string scope, FormattableString query, Action<PropertyDataCollection> handler)
    {
        var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);
        var watcher = new ManagementEventWatcher(scope, queryFormatted);
        watcher.EventArrived += (_, e) => handler(e.NewEvent.Properties);
        watcher.Start();

        return new LambdaDisposable(() =>
        {
            watcher.Stop();
            watcher.Dispose();
        });
    }

    private static async Task<IEnumerable<T>> ReadAsync<T>(string scope, FormattableString query, Func<PropertyDataCollection, T> converter)
    {
        try
        {
            var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);
            return await QueryAsync(scope, queryFormatted, mo => converter(mo.Properties)).ConfigureAwait(false);
        }
        catch (ManagementException ex)
        {
            throw new ManagementException($"Read failed: {ex.Message} [scope={scope}, query={query}]", ex);
        }
    }

    private static async Task CallAsync(string scope, FormattableString query, string methodName, Dictionary<string, object> methodParams)
    {
        try
        {
            var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);
            await InvokeAsync(scope, queryFormatted, methodName, methodParams, _ => true).ConfigureAwait(false);
        }
        catch (ManagementException ex)
        {
            var methodParamsStr = string.Join(", ", methodParams.Select(kv => $"{kv.Key}={kv.Value}"));
            throw new ManagementException($"Call failed: {ex.Message}. [scope={scope}, query={query}, methodName={methodName}, methodParams={{{methodParamsStr}}}]", ex);
        }
    }

    private static async Task<T> CallAsync<T>(string scope, FormattableString query, string methodName, Dictionary<string, object> methodParams, Func<PropertyDataCollection, T> converter)
    {
        try
        {
            var queryFormatted = query.ToString(WMIPropertyValueFormatter.Instance);
            return await InvokeAsync(scope, queryFormatted, methodName, methodParams,
                result => converter((result ?? throw new InvalidOperationException($"{methodName} returned nothing")).Properties)).ConfigureAwait(false);
        }
        catch (ManagementException ex)
        {
            var methodParamsStr = string.Join(", ", methodParams.Select(kv => $"{kv.Key}={kv.Value}"));
            throw new ManagementException($"Call failed: {ex.Message}. [scope={scope}, query={query}, methodName={methodName}, methodParams={{{methodParamsStr}}}]", ex);
        }
    }

    private class WMIPropertyValueFormatter : IFormatProvider, ICustomFormatter
    {
        public static readonly WMIPropertyValueFormatter Instance = new();

        private WMIPropertyValueFormatter() { }

        public object GetFormat(Type? formatType)
        {
            if (formatType == typeof(ICustomFormatter))
                return this;

            throw new InvalidOperationException("Invalid type of formatted");
        }

        public string Format(string? format, object? arg, IFormatProvider? formatProvider)
        {
            var stringArg = arg?.ToString()?.Replace("\\", "\\\\");
            return stringArg ?? string.Empty;
        }
    }
}
