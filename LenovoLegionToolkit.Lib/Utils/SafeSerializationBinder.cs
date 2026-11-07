using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Restricts which types a JSON file may name in "$type". Settings use TypeNameHandling.Auto for
/// automation steps and triggers; without a limit, a crafted file can name framework "gadget" types
/// (for example WPF's ObjectDataProvider) and run code inside the elevated app.
/// Only LLT's own types, enums, simple values, colors and standard collections of those are allowed.
/// </summary>
public sealed class SafeSerializationBinder : ISerializationBinder
{
    public static readonly SafeSerializationBinder Instance = new();

    private static readonly HashSet<Type> AllowedSimpleTypes =
    [
        typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset),
        typeof(TimeSpan), typeof(Guid), typeof(Uri),
    ];

    private static readonly HashSet<string> AllowedNamedTypes = new(StringComparer.Ordinal)
    {
        "System.Drawing.Color",
        "System.Windows.Media.Color",
    };

    private static readonly HashSet<Type> AllowedGenericDefinitions =
    [
        typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>), typeof(Nullable<>),
    ];

    private readonly DefaultSerializationBinder _inner = new();

    private SafeSerializationBinder() { }

    public Type BindToType(string? assemblyName, string typeName)
    {
        // Refuse unknown assemblies before resolving, so a file can't make us load arbitrary assemblies.
        if (assemblyName is not null && !IsAllowedAssemblyName(assemblyName))
            throw Reject($"{typeName}, {assemblyName}");

        var type = _inner.BindToType(assemblyName, typeName);

        if (!IsAllowed(type))
            throw Reject(type.AssemblyQualifiedName ?? type.FullName ?? typeName);

        return type;
    }

    public void BindToName(Type serializedType, out string? assemblyName, out string? typeName) =>
        _inner.BindToName(serializedType, out assemblyName, out typeName);

    private static JsonSerializationException Reject(string type)
    {
        Log.Instance.Trace($"Rejected type in JSON: {type}");
        return new JsonSerializationException($"Type '{type}' is not allowed in settings files.");
    }

    private static bool IsAllowedAssemblyName(string assemblyName)
    {
        var name = assemblyName.Split(',')[0].Trim();
        return name.StartsWith("LenovoLegionToolkit", StringComparison.OrdinalIgnoreCase)
               || name is "System.Private.CoreLib" or "mscorlib" or "System.Runtime" or "System.Collections"
                   or "System.Drawing.Primitives" or "System.Drawing" or "PresentationCore";
    }

    internal static bool IsAllowed(Type type)
    {
        if (type.IsArray)
            return IsAllowed(type.GetElementType()!);

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var isLltGeneric = IsLltType(definition);
            if (!isLltGeneric && !AllowedGenericDefinitions.Contains(definition))
                return false;

            foreach (var argument in type.GetGenericArguments())
            {
                if (!IsAllowed(argument))
                    return false;
            }

            return true;
        }

        if (IsLltType(type))
            return true;

        if (type.IsEnum || type.IsPrimitive || AllowedSimpleTypes.Contains(type))
            return true;

        return type.FullName is not null && AllowedNamedTypes.Contains(type.FullName);
    }

    private static bool IsLltType(Type type) =>
        type.Assembly.GetName().Name?.StartsWith("LenovoLegionToolkit", StringComparison.OrdinalIgnoreCase) == true;
}
