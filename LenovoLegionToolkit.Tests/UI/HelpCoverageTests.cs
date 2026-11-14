using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Help;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// Every setting card, dashboard card, automation action and trigger must explain itself on hover. These tests read the
/// real XAML/C# sources and the real resources, so a new card without help text fails here instead of shipping silent.
/// </summary>
public class HelpCoverageTests
{
    private static readonly string Wpf = Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.WPF");

    private static bool HasText(string key) => !string.IsNullOrWhiteSpace(Resource.ResourceManager.GetString(key + HelpContent.WhatSuffix));

    private static IEnumerable<string> SourceFiles(string extension) =>
        Directory.EnumerateFiles(Wpf, "*" + extension, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void EveryCardHeaderInXaml_HasAHelpKey()
    {
        // GodModeValueControl's header is a template: each usage passes its own HelpKey (checked below).
        var templates = new[] { "GodModeValueControl.xaml" };
        var element = new Regex(@"<(controls:CardHeaderControl|godMode:GodModeValueControl)\b[^>]*?/?>", RegexOptions.Singleline);

        var missing = SourceFiles(".xaml")
            .Where(f => !templates.Contains(Path.GetFileName(f)))
            .SelectMany(f => element.Matches(File.ReadAllText(f))
                .Where(m => !m.Value.Contains("HelpKey="))
                .Select(m => $"{Path.GetFileName(f)}: {Regex.Replace(m.Value, @"\s+", " ")}"))
            .ToList();

        Assert.True(missing.Count == 0, "Cards without HelpKey:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryCardHeaderBuiltInCode_SetsAHelpKey()
    {
        // Proxy check: a file that builds N headers in code must assign HelpKey at least N times.
        var construction = new Regex(@"new CardHeaderControl\b|CardHeaderControl\s+\w+\s*=\s*new\(\)");
        var assignment = new Regex(@"\bHelpKey\s*=[^=]");

        var missing = SourceFiles(".cs")
            .Where(f => !f.EndsWith("CardHeaderControl.cs"))
            .Select(f => (File: f, Text: File.ReadAllText(f)))
            .Select(x => (x.File, Built: construction.Matches(x.Text).Count, Keys: assignment.Matches(x.Text).Count))
            .Where(x => x.Built > x.Keys)
            .Select(x => $"{Path.GetFileName(x.File)}: builds {x.Built}, sets HelpKey {x.Keys}")
            .ToList();

        Assert.True(missing.Count == 0, "Code-built cards without HelpKey:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryDashboardFeatureCard_HasHelpText()
    {
        // The feature-card base classes default to Help_Feature_<feature state type>.
        var cardBases = new[] { typeof(AbstractToggleFeatureCardControl<>), typeof(AbstractComboBoxFeatureCardControl<>) };
        var cards = typeof(AbstractRefreshingControl).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.BaseType is { IsGenericType: true } b && cardBases.Contains(b.GetGenericTypeDefinition()))
            .ToList();

        Assert.NotEmpty(cards);

        var missing = cards
            .Select(t => (Card: t.Name, Key: "Help_Feature_" + t.BaseType!.GetGenericArguments()[0].Name))
            .Where(x => !HasText(x.Key) && !SetsOwnKey(x.Card))
            .Select(x => $"{x.Card} → {x.Key}")
            .ToList();

        Assert.True(missing.Count == 0, "Dashboard cards without help:\n" + string.Join("\n", missing));
    }

    /// <summary>A card that assigns its own literal key; that key is checked by <see cref="HelpContentTests.EveryHelpKeyUsedInTheUi_HasText"/>.</summary>
    private static bool SetsOwnKey(string className) =>
        SourceFiles(".cs").Where(f => Path.GetFileNameWithoutExtension(f) == className)
            .Any(f => Regex.IsMatch(File.ReadAllText(f), @"HelpKey\s*=\s*""Help_"));

    [Fact]
    public void EveryDashboardItem_HasHelpText()
    {
        var missing = Enum.GetValues<LenovoLegionToolkit.WPF.DashboardItem>()
            .Where(i => !HasText(i.GetHelpKey()))
            .Select(i => $"{i} → {i.GetHelpKey()}")
            .ToList();

        Assert.True(missing.Count == 0, "Dashboard items without help:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryAutomationAction_HasHelpText()
    {
        var steps = typeof(IAutomationStep).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAutomationStep).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(steps);

        var missing = steps.Select(t => "Help_Step_" + t.Name).Where(k => !HasText(k)).ToList();
        Assert.True(missing.Count == 0, "Automation actions without help: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryAutomationTrigger_HasHelpText()
    {
        var triggers = typeof(IAutomationPipelineTrigger).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAutomationPipelineTrigger).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(triggers);

        var missing = triggers.Select(t => "Help_Trigger_" + t.Name).Where(k => !HasText(k)).ToList();
        Assert.True(missing.Count == 0, "Automation triggers without help: " + string.Join(", ", missing));
    }
}
