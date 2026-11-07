using System;

namespace LenovoLegionToolkit.Lib.Utils;

public static class AutomationTranslator
{
    public static Func<string, string>? GetTitleFunc { get; set; }

    public static string Translate(string typeName)
    {
        return GetTitleFunc?.Invoke(typeName) ?? typeName.Replace("AutomationStep", "");
    }
}