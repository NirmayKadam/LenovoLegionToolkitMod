using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// Unit strings in every language file. Several translations had turned "W" and "GB" into words or country codes
/// (Ukrainian "Пт" means Friday; German "SF"), so power and memory showed with nonsense units, and a missing power
/// reading showed as "-1 Пт" instead of "-". The real resource files are read.
/// </summary>
public class UnitStringsTests
{
    /// <summary>Real translations of "GB" (gigabyte), not mistakes.</summary>
    private static readonly Dictionary<string, string> GigabyteTranslations = new()
    {
        ["ar"] = "جيجابايت",
        ["uk"] = "ГБ",
    };

    private static IEnumerable<(string Language, XDocument Resx)> LanguageFiles()
    {
        var folder = Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.WPF", "Resources");
        return Directory.GetFiles(folder, "Resource*.resx")
            .Select(f => (Path.GetFileNameWithoutExtension(f).Replace("Resource", string.Empty).TrimStart('.'), XDocument.Load(f)));
    }

    private static string? Value(XDocument resx, string key) =>
        resx.Root!.Elements("data").FirstOrDefault(d => (string?)d.Attribute("name") == key)?.Element("value")?.Value;

    [Fact]
    public void Watts_AreWrittenW_InEveryLanguage()
    {
        var wrong = LanguageFiles()
            .Select(f => (f.Language, Value: Value(f.Resx, "Watt")))
            .Where(f => f.Value is not null && f.Value != "W")
            .Select(f => $"{f.Language}: {f.Value}")
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void Gigabytes_AreWrittenGB_OrTranslatedAsGigabytes()
    {
        var wrong = LanguageFiles()
            .Select(f => (f.Language, Value: Value(f.Resx, "GB")))
            .Where(f => f.Value is not null && f.Value != "GB" && GigabyteTranslations.GetValueOrDefault(f.Language) != f.Value)
            .Select(f => $"{f.Language}: {f.Value}")
            .ToList();

        Assert.Empty(wrong);
    }
}
