using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Wpf.Ui.Common;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// Icon names written in XAML are only looked up when the window opens: a name WPF-UI doesn't have builds fine and
/// then shows "Unexpected exception: Requested value '…' was not found" (the Game profiles window did this with
/// "Leaf24"). This reads the real XAML files and checks every literal icon name against WPF-UI's own list.
/// </summary>
public class XamlIconTests
{
    private static readonly string Wpf = Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.WPF");

    [Fact]
    public void EveryIconNameInXaml_ExistsInWpfUi()
    {
        var attribute = new Regex(@"\b(?:Icon|Symbol)=""(?<name>[A-Za-z][A-Za-z0-9]*)""");
        var files = Directory.EnumerateFiles(Wpf, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        var checkedNames = 0;
        var unknown = files
            .SelectMany(f => File.ReadAllLines(f).Select((line, index) => (File: f, Line: index + 1, Text: line)))
            .SelectMany(l => attribute.Matches(l.Text).Select(m => (l.File, l.Line, Name: m.Groups["name"].Value)))
            .Where(x =>
            {
                checkedNames++;
                return !Enum.IsDefined(typeof(SymbolRegular), x.Name);
            })
            .Select(x => $"{Path.GetFileName(x.File)}:{x.Line}: {x.Name}")
            .ToList();

        Assert.True(checkedNames > 100, $"only {checkedNames} icon names found; the XAML wasn't read");
        Assert.True(unknown.Count == 0, "Icons WPF-UI doesn't have:\n" + string.Join("\n", unknown));
    }
}
