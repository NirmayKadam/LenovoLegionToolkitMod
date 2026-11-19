using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

/// <summary>
/// Switches MSI Afterburner to one of its five profiles. Unlike a Run step, it only ever starts the genuine
/// MSIAfterburner.exe (validly signed by Micro-Star; see <see cref="AfterburnerProgram"/>). It applies the profile at
/// once, also while a game runs.
/// </summary>
/// <param name="programPath">The program to start; null means the installed Afterburner, looked up when the step runs.</param>
[method: JsonConstructor]
public class AfterburnerProfileAutomationStep(int state, string? programPath = null) : IAutomationStep<int>
{
    public int State { get; } = state is >= 1 and <= 5 ? state : 1;

    public string? ProgramPath { get; } = string.IsNullOrWhiteSpace(programPath) ? null : programPath;

    /// <summary>The program this step starts: its own path, or the installed Afterburner.</summary>
    [JsonIgnore]
    public string? EffectivePath => ProgramPath ?? AfterburnerProgram.FindInstalled();

    public Task<bool> IsSupportedAsync() => Task.FromResult(EffectivePath is { } path && File.Exists(path));

    public Task<int[]> GetAllStatesAsync() => Task.FromResult(Enumerable.Range(1, 5).ToArray());

    public IAutomationStep DeepCopy() => new AfterburnerProfileAutomationStep(State, ProgramPath);

    public async Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        var path = EffectivePath;
        var result = await AfterburnerProgram.StartProfileAsync(path, State, token).ConfigureAwait(false);
        if (result == AfterburnerCheck.Genuine)
            return;

        Log.Instance.Trace($"MSI Afterburner profile not applied: {result}. [path={path}, profile={State}]");
        MessagingCenter.Publish(new NotificationMessage(NotificationType.AutomationNotification, string.Format(Resource.AfterburnerStep_NotStarted, State, Describe(result))));
    }

    public static string Describe(AfterburnerCheck result) => result switch
    {
        AfterburnerCheck.Genuine => Resource.AfterburnerStep_Genuine,
        AfterburnerCheck.NotFound => Resource.AfterburnerStep_NotFound,
        AfterburnerCheck.NotAfterburner => Resource.AfterburnerStep_NotAfterburner,
        AfterburnerCheck.NotSigned => Resource.AfterburnerStep_NotSigned,
        AfterburnerCheck.WrongSigner => Resource.AfterburnerStep_WrongSigner,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
    };
}
