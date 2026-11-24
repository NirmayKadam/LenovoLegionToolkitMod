using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LenovoLegionToolkit.Tests.Utils;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// What a button or switch does when its action fails (<see cref="UiErrors"/>), with a real log in a temporary folder.
/// There is no app window in a test, so showing the warning is skipped there (and that it is skipped is logged too).
/// </summary>
[Collection(nameof(AppLoggerCollection))]
public class UiErrorsTests
{
    [Fact]
    public async Task AFailedAction_IsLogged_AndGoesNoFurther()
    {
        using var log = new TestLog();

        var thrown = await Record.ExceptionAsync(() => UiErrors.RunAsync("Locking the GPU P-state",
            () => Task.FromException(new InvalidOperationException("NVAPI_ERROR"))));

        Assert.Null(thrown);
        Assert.Contains("Locking the GPU P-state failed.", log.Text);
        Assert.Contains("NVAPI_ERROR", log.Text);
    }

    [Fact]
    public async Task AStoppedAction_IsNotAFailure()
    {
        using var log = new TestLog();

        await UiErrors.RunAsync("Listing the devices", () => Task.FromCanceled(new global::System.Threading.CancellationToken(true)));

        Assert.DoesNotContain("Listing the devices failed.", log.Text);
    }

    [Fact]
    public async Task AnActionThatWorks_RunsToTheEnd_WithNothingLogged()
    {
        using var log = new TestLog();
        var ran = false;

        await UiErrors.RunAsync("Saving the automations", async () =>
        {
            await Task.Yield();
            ran = true;
        });

        Assert.True(ran);
        Assert.DoesNotContain("failed", log.Text);
    }

    [Fact]
    public void TheDeviceTriggerTab_ListensToDevices_OnlyWhileOnScreen()
    {
        // It subscribed to the app-wide device listener on every load and never unsubscribed, so each closed trigger
        // window stayed in memory and reloaded its device list at every USB plug and unplug.
        var source = File.ReadAllText(Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.WPF", "Windows", "Automation",
            "TabItemContent", "DeviceAutomationPipelineTriggerTabItemContent.xaml.cs"));

        var subscriptions = Regex.Matches(source, @"_listener\.Changed\s*\+=").Count;
        Assert.Equal(1, subscriptions);
        Assert.Matches(@"WhileLoaded\(\s*\(\)\s*=>\s*_listener\.Changed\s*\+=\s*\w+\s*,\s*\(\)\s*=>\s*_listener\.Changed\s*-=", source);
    }
}
