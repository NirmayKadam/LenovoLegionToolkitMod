using System;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Controls.Help;

/// <summary>
/// The hover explanation for one setting or tool: what it does, when to use it, and its risks.
/// The text lives in <c>Resources/Resource.resx</c> under <c>&lt;key&gt;_What</c>, <c>&lt;key&gt;_When</c> and
/// <c>&lt;key&gt;_Risk</c>. Only "what" is required.
/// </summary>
public sealed record HelpContent(string What, string? When, string? Risk)
{
    public const string WhatSuffix = "_What";
    public const string WhenSuffix = "_When";
    public const string RiskSuffix = "_Risk";

    /// <summary>
    /// Looks up the three sections for <paramref name="key"/>. Returns null when there's no key or no "what" text,
    /// so nothing is shown rather than an empty box.
    /// </summary>
    public static HelpContent? Resolve(string? key, Func<string, string?> lookup)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var what = Clean(lookup(key + WhatSuffix));
        if (what is null)
            return null;

        return new HelpContent(what, Clean(lookup(key + WhenSuffix)), Clean(lookup(key + RiskSuffix)));
    }

    /// <summary>Resolves from the app's resources in the current UI language (falling back to English).</summary>
    public static HelpContent? Resolve(string? key) =>
        Resolve(key, name => Resource.ResourceManager.GetString(name, Resource.Culture));

    /// <summary>The whole text as one string, for screen readers.</summary>
    public string ToPlainText()
    {
        var text = What;
        if (When is not null)
            text += $" {Resource.Help_WhenLabel}: {When}";
        if (Risk is not null)
            text += $" {Resource.Help_RiskLabel}: {Risk}";
        return text;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
