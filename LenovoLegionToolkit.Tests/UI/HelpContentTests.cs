using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LenovoLegionToolkit.WPF.Controls.Help;
using LenovoLegionToolkit.WPF.Resources;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

public class HelpContentTests
{
    private static global::System.Func<string, string?> Lookup(Dictionary<string, string> strings) =>
        name => strings.TryGetValue(name, out var value) ? value : null;

    [Fact]
    public void Resolve_AllThreeSections()
    {
        var help = HelpContent.Resolve("Help_X", Lookup(new()
        {
            ["Help_X_What"] = "Does a thing.",
            ["Help_X_When"] = "Sometimes.",
            ["Help_X_Risk"] = "Could be hot.",
        }));

        Assert.Equal(new HelpContent("Does a thing.", "Sometimes.", "Could be hot."), help);
    }

    [Fact]
    public void Resolve_OnlyWhatIsRequired()
    {
        var help = HelpContent.Resolve("Help_X", Lookup(new() { ["Help_X_What"] = "Does a thing." }));

        Assert.NotNull(help);
        Assert.Null(help!.When);
        Assert.Null(help.Risk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NoKey_ShowsNothing(string? key) =>
        Assert.Null(HelpContent.Resolve(key, Lookup(new() { ["_What"] = "x" })));

    [Fact]
    public void Resolve_MissingWhat_ShowsNothing_EvenIfOtherSectionsExist() =>
        Assert.Null(HelpContent.Resolve("Help_X", Lookup(new() { ["Help_X_When"] = "Sometimes." })));

    [Fact]
    public void Resolve_BlankSections_AreTreatedAsMissing_AndTextIsTrimmed()
    {
        var help = HelpContent.Resolve("Help_X", Lookup(new()
        {
            ["Help_X_What"] = "  Does a thing.  ",
            ["Help_X_When"] = "   ",
        }));

        Assert.Equal("Does a thing.", help!.What);
        Assert.Null(help.When);
    }

    [Fact]
    public void PlainText_IncludesEverySectionThatExists()
    {
        var text = new HelpContent("What.", "When.", "Risk.").ToPlainText();
        Assert.StartsWith("What.", text);
        Assert.Contains("When.", text);
        Assert.Contains("Risk.", text);
    }

    /// <summary>
    /// Every help key the UI refers to must have its "what" text in the resources; otherwise its ⓘ silently
    /// never appears. Reads the real source files.
    /// </summary>
    [Fact]
    public void EveryHelpKeyUsedInTheUi_HasText()
    {
        var root = FindRepositoryRoot();
        var wpf = Path.Combine(root, "LenovoLegionToolkit.WPF");
        var keyPattern = new Regex(@"HelpKey\s*=\s*""(?<key>[A-Za-z0-9_]+)""");

        var keys = Directory.EnumerateFiles(wpf, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml") || f.EndsWith(".cs"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => keyPattern.Matches(File.ReadAllText(f)).Select(m => m.Groups["key"].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(keys);

        var missing = keys.Where(k => string.IsNullOrWhiteSpace(Resource.ResourceManager.GetString(k + HelpContent.WhatSuffix))).ToList();
        Assert.True(missing.Count == 0, "Help keys without text: " + string.Join(", ", missing));
    }

    internal static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LenovoLegionToolkit.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Couldn't find the repository root from " + global::System.AppContext.BaseDirectory);
    }
}
