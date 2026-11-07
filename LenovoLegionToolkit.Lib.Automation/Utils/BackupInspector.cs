using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LenovoLegionToolkit.Lib.Automation.Utils;

/// <summary>
/// Looks inside a settings backup before it is restored, so the user can see anything that would make
/// the elevated app run programs, type keystrokes, or change network/debug behavior.
/// </summary>
public static class BackupInspector
{
    public const string AUTOMATION_FILE = "automation.json";
    public const string MACRO_FILE = "macro.json";
    public const string ARGS_FILE = "args.txt";

    private static readonly string[] RiskyFlags =
    [
        "--proxy-url", "--proxy-username", "--proxy-password", "--proxy-allow-all-certs",
        "--debug", "--disable-update-checker", "--skip-compat-check",
    ];

    public sealed record ProgramToRun(string Path, string? Arguments);

    public sealed class Result
    {
        public List<ProgramToRun> Programs { get; } = [];
        public string? AutomationError { get; set; }
        public int MacroCount { get; set; }
        public string? MacroError { get; set; }
        public List<string> Flags { get; } = [];

        public bool HasRisks => Programs.Count > 0 || AutomationError is not null || MacroCount > 0 || MacroError is not null || Flags.Count > 0;
    }

    /// <summary>
    /// Inspects the files from <paramref name="fileNames"/> that exist in <paramref name="folder"/>.
    /// </summary>
    public static Result Inspect(string folder, IEnumerable<string> fileNames)
    {
        var names = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
        var result = new Result();

        var automationPath = Path.Combine(folder, AUTOMATION_FILE);
        if (names.Contains(AUTOMATION_FILE) && File.Exists(automationPath))
            InspectAutomation(automationPath, result);

        var macroPath = Path.Combine(folder, MACRO_FILE);
        if (names.Contains(MACRO_FILE) && File.Exists(macroPath))
            InspectMacros(macroPath, result);

        var argsPath = Path.Combine(folder, ARGS_FILE);
        if (names.Contains(ARGS_FILE) && File.Exists(argsPath))
            InspectArgs(argsPath, result);

        return result;
    }

    private static void InspectAutomation(string path, Result result)
    {
        try
        {
            var json = File.ReadAllText(path);
            var store = JsonConvert.DeserializeObject<AutomationSettings.AutomationSettingsStore>(json, AbstractSettings<object>.CreateJsonSerializerSettings());

            foreach (var step in store?.Pipelines.SelectMany(p => p.Steps) ?? [])
            {
                if (step is RunAutomationStep run && !string.IsNullOrWhiteSpace(run.ScriptPath))
                    result.Programs.Add(new ProgramToRun(run.ScriptPath, run.ScriptArguments));
            }
        }
        catch (Exception ex)
        {
            result.AutomationError = ex.Message;
        }
    }

    private static void InspectMacros(string path, Result result)
    {
        try
        {
            // Plain JSON parsing: no "$type" handling, nothing is instantiated.
            var root = JToken.Parse(File.ReadAllText(path));
            result.MacroCount = root["Sequences"] is JObject sequences ? sequences.Count : 0;
        }
        catch (Exception ex)
        {
            result.MacroError = ex.Message;
        }
    }

    private static void InspectArgs(string path, Result result)
    {
        foreach (var line in File.ReadAllLines(path))
        {
            var flag = line.Trim().Split('=')[0];
            if (RiskyFlags.Contains(flag, StringComparer.Ordinal) && !result.Flags.Contains(flag))
                result.Flags.Add(flag);
        }
    }
}
