using System;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Controls.Help;

/// <summary>
/// App-wide switches for hover help, and the tooltip content shown for a <see cref="HelpContent"/>.
/// </summary>
public static class HelpPresenter
{
    private static bool _isEnabled = true;
    private static int _initialDelayMs = 400;

    /// <summary>Raised when hover help is turned on/off or its delay changes.</summary>
    public static event EventHandler? SettingsChanged;

    public static bool IsEnabled => _isEnabled;

    public static int InitialDelayMs => _initialDelayMs;

    public static void Configure(bool isEnabled, int initialDelayMs)
    {
        initialDelayMs = Math.Clamp(initialDelayMs, 0, 5000);
        if (isEnabled == _isEnabled && initialDelayMs == _initialDelayMs)
            return;

        _isEnabled = isEnabled;
        _initialDelayMs = initialDelayMs;
        SettingsChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Builds the tooltip: the setting's name, then "What it does", "When to use it" and "Risks"
    /// (the last two only when written).
    /// </summary>
    public static ToolTip CreateToolTip(string title, HelpContent content)
    {
        var panel = new StackPanel { MaxWidth = 380 };

        if (!string.IsNullOrWhiteSpace(title))
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) });

        AddSection(panel, Resource.Help_WhatLabel, content.What);
        if (content.When is not null)
            AddSection(panel, Resource.Help_WhenLabel, content.When);
        if (content.Risk is not null)
            AddSection(panel, Resource.Help_RiskLabel, content.Risk);

        return new ToolTip { Content = panel };
    }

    private static void AddSection(Panel panel, string label, string text)
    {
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new(0, panel.Children.Count > 1 ? 6 : 0, 0, 1) });
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
    }
}
