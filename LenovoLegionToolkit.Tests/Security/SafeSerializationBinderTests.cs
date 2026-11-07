using System;
using System.Collections.Generic;
using System.IO;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Automation.Utils;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;
using Xunit;

namespace LenovoLegionToolkit.Tests.Security;

// Uses the exact JSON settings the app uses for every settings file.
public class SafeSerializationBinderTests : IDisposable
{
    private readonly string _marker = Path.Combine(Path.GetTempPath(), $"llt_test_pwned_{Guid.NewGuid():N}.txt");
    private readonly JsonSerializerSettings _settings = AbstractSettings<object>.CreateJsonSerializerSettings();

    public void Dispose()
    {
        if (File.Exists(_marker))
            File.Delete(_marker);
    }

    private static AutomationSettings.AutomationSettingsStore CreateAutomationStore() => new()
    {
        IsEnabled = true,
        Pipelines =
        [
            new AutomationPipeline("On AC")
            {
                Trigger = new ACAdapterConnectedAutomationPipelineTrigger(),
                Steps = { new DelayAutomationStep(new Delay(2)), new NotificationAutomationStep("plugged in") },
            },
            new AutomationPipeline("Composite")
            {
                Trigger = new AndAutomationPipelineTrigger(
                [
                    new BatteryPercentageAutomationPipelineTrigger(20, true),
                    new WiFiConnectedAutomationPipelineTrigger(["Home", "Office"]),
                ]),
                Steps = { new RunAutomationStep(@"C:\Tools\script.bat", "--fast", true, true, false), new CloseAppAutomationStep() },
            },
            new AutomationPipeline("Games")
            {
                Trigger = new OrAutomationPipelineTrigger(
                [
                    new ProcessesAreRunningAutomationPipelineTrigger([new ProcessInfo("game", @"C:\Games\game.exe")]),
                    new PeriodicAutomationPipelineTrigger(TimeSpan.FromMinutes(5)),
                    new OnStartupAutomationPipelineTrigger(),
                ]),
                Steps = { new QuickActionAutomationStep(Guid.NewGuid()) },
            },
        ],
    };

    [Fact]
    public void AutomationSettings_RoundTrip_WithPolymorphicStepsAndTriggers()
    {
        var json = JsonConvert.SerializeObject(CreateAutomationStore(), _settings);
        Assert.Contains("\"$type\"", json);

        var restored = JsonConvert.DeserializeObject<AutomationSettings.AutomationSettingsStore>(json, _settings);
        var jsonAgain = JsonConvert.SerializeObject(restored, _settings);

        Assert.Equal(json, jsonAgain);
    }

    [Fact]
    public void LampArraySettings_RoundTrip_WithObjectParameters()
    {
        var store = new LampArraySettings.LampArraySettingsStore
        {
            Brightness = 0.5,
            DefaultEffect = new LampArraySettings.LampEffectConfig { Parameters = { ["Color"] = "255,10,20,30", ["Period"] = 2.5, ["Spatial"] = true } },
        };

        var json = JsonConvert.SerializeObject(store, _settings);
        var restored = JsonConvert.DeserializeObject<LampArraySettings.LampArraySettingsStore>(json, _settings);

        Assert.Equal(json, JsonConvert.SerializeObject(restored, _settings));
    }

    // Json.NET ObjectDataProvider gadget: starts a process while deserializing.
    // (The common variant passes arguments via MethodParameters, but LLT's ObjectCreationHandling.Replace
    // drops that read-only list; putting the command in Process.StartInfo works against LLT's options.)
    private string GadgetPayload => $$"""
        {
          "DefaultEffect": {
            "Parameters": {
              "x": {
                "$type": "System.Windows.Data.ObjectDataProvider, PresentationFramework",
                "MethodName": "Start",
                "ObjectInstance": {
                  "$type": "System.Diagnostics.Process, System",
                  "StartInfo": {
                    "$type": "System.Diagnostics.ProcessStartInfo, System",
                    "FileName": "cmd.exe",
                    "Arguments": "/c echo pwned> \"{{_marker.Replace("\\", "\\\\")}}\"",
                    "CreateNoWindow": true,
                    "UseShellExecute": false
                  }
                }
              }
            }
          }
        }
        """;

    private bool WaitForMarker()
    {
        for (var i = 0; i < 20; i++)
        {
            if (File.Exists(_marker))
                return true;
            global::System.Threading.Thread.Sleep(250);
        }
        return false;
    }

    [Fact]
    public void Control_SamePayloadWithoutAllowList_ReallyRunsAProcess()
    {
        // Proves the payload is live in this environment, so the next test's "nothing ran" means something.
        var unsafeSettings = AbstractSettings<object>.CreateJsonSerializerSettings();
        unsafeSettings.SerializationBinder = new Newtonsoft.Json.Serialization.DefaultSerializationBinder();

        try
        {
            JsonConvert.DeserializeObject<LampArraySettings.LampArraySettingsStore>(GadgetPayload, unsafeSettings);
        }
        catch (JsonException)
        {
            // The store may fail to bind the result; the process starts before that.
        }

        Assert.True(WaitForMarker(), "Without the allow-list the gadget should have started cmd.exe.");
    }

    [Fact]
    public void GadgetPayload_InLampArrayProfile_IsRejected_AndNothingRuns()
    {
        Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<LampArraySettings.LampArraySettingsStore>(GadgetPayload, _settings));

        Assert.False(WaitForMarker(), "The payload ran a process.");
    }

    [Theory]
    [InlineData("System.IO.FileInfo", "System.Private.CoreLib")]
    [InlineData("System.Diagnostics.Process", "System")]
    [InlineData("System.Windows.Data.ObjectDataProvider", "PresentationFramework")]
    [InlineData("System.Collections.ArrayList", "mscorlib")]
    public void NonLltTypes_AreRejected(string typeName, string assemblyName)
    {
        Assert.Throws<JsonSerializationException>(() => SafeSerializationBinder.Instance.BindToType(assemblyName, typeName));
    }

    [Theory]
    [InlineData(typeof(DelayAutomationStep))]
    [InlineData(typeof(PowerModeState))]
    [InlineData(typeof(List<IAutomationStep>))]
    [InlineData(typeof(Dictionary<Guid, string>))]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(int?))]
    [InlineData(typeof(global::System.Drawing.Color))]
    public void AllowedTypes_AreAccepted(Type type)
    {
        Assert.True(SafeSerializationBinder.IsAllowed(type));
    }

    [Theory]
    [InlineData(typeof(List<FileInfo>))]
    [InlineData(typeof(Dictionary<string, object>))]
    [InlineData(typeof(object))]
    [InlineData(typeof(FileInfo[]))]
    public void DisallowedTypes_AreRejected(Type type)
    {
        Assert.False(SafeSerializationBinder.IsAllowed(type));
    }
}
