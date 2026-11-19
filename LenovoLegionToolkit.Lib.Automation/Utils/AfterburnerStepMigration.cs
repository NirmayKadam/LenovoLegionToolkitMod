using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Automation.Utils;

/// <summary>
/// Turns Run steps that only switch an MSI Afterburner profile ("MSIAfterburner.exe -Profile1" … "-Profile5") into
/// <see cref="AfterburnerProfileAutomationStep"/>, which starts only the genuine, MSI-signed program. Other Run steps
/// are left alone. Running it again finds nothing more to convert.
/// </summary>
internal static partial class AfterburnerStepMigration
{
    /// <summary>The automations as they were before the first conversion, next to the settings file.</summary>
    internal const string BackupFileName = "automation_before_round14.json";

    /// <summary>Converts in place; returns how many steps were converted.</summary>
    internal static int Convert(IEnumerable<AutomationPipeline> pipelines)
    {
        var converted = 0;
        foreach (var pipeline in pipelines)
        {
            for (var i = 0; i < pipeline.Steps.Count; i++)
            {
                if (pipeline.Steps[i] is not RunAutomationStep run || !TryGetProfile(run, out var profile))
                    continue;

                pipeline.Steps[i] = new AfterburnerProfileAutomationStep(profile, run.ScriptPath!.Trim().Trim('"'));
                converted++;
            }
        }

        return converted;
    }

    internal static bool TryGetProfile(RunAutomationStep run, out int profile)
    {
        profile = 0;

        var path = run.ScriptPath?.Trim().Trim('"');
        if (string.IsNullOrEmpty(path) || !Path.GetFileName(path).Equals(AfterburnerProgram.FileName, StringComparison.OrdinalIgnoreCase))
            return false;

        var match = ProfileArgument().Match(run.ScriptArguments ?? string.Empty);
        if (!match.Success)
            return false;

        profile = match.Groups[1].Value[0] - '0';
        return true;
    }

    /// <summary>Copies the settings file before it is changed, unless a copy from an earlier conversion exists. Returns the copy's path.</summary>
    internal static string? BackUpOnce(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
            return null;

        var backup = Path.Combine(Path.GetDirectoryName(settingsFilePath)!, BackupFileName);
        if (!File.Exists(backup))
            File.Copy(settingsFilePath, backup);

        return backup;
    }

    [GeneratedRegex(@"^\s*-profile([1-5])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProfileArgument();
}
