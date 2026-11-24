using System;
using System.IO;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>
/// When the update check really asks GitHub. The tray window asks on every sensor tick and the main window each time it
/// opens; a call before the check is due used to move the time of the last check forward anyway, so the real check never
/// came, and it rewrote the settings file each time. Only calls that are not due are made here (no network).
/// </summary>
public sealed class UpdateCheckTimingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-update-timing-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task ACallBeforeTheCheckIsDue_LeavesTheTimeOfTheLastCheck_AndTheFileAlone()
    {
        var lastCheck = DateTime.UtcNow.AddMinutes(-30);   // hourly checks: the next one is due in 30 minutes
        var settings = new UpdateSettings(_dir);
        settings.Store.UpdateCheckFrequency = UpdateCheckFrequency.PerHour;
        settings.Store.LastUpdateCheckDateTime = lastCheck;
        settings.SynchronizeStore();
        var file = Path.Combine(_dir, "update_settings.json");
        var writtenAt = File.GetLastWriteTimeUtc(file);
        var checker = new UpdateChecker(new HttpClientFactory(), settings);

        for (var i = 0; i < 5; i++)   // five tray ticks
            Assert.Null(await checker.CheckAsync(false));

        Assert.Equal(lastCheck, new UpdateSettings(_dir).Store.LastUpdateCheckDateTime);
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(file));
    }
}
