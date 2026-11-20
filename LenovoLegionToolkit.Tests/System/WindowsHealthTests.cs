using System;
using System.Globalization;
using System.Management;
using System.ServiceProcess;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

/// <summary>
/// The Health page's Windows reads, against this PC's real Windows. They check that each read works and returns a
/// plausible value, never this laptop's particular values. Where a part of Windows is missing on some editions (CI
/// runs on Windows Server, which has no Security Center), the test accepts that one honest failure and nothing else.
/// </summary>
public class WindowsHealthTests
{
    [Fact]
    public void TheLowBatteryAction_IsReadFromTheActivePowerPlan()
    {
        var info = WindowsHealth.GetBatteryAction();

        Assert.InRange(info.CriticalActionOnBattery, WindowsHealth.ActionNothing, WindowsHealth.ActionShutDown);
        Assert.InRange(info.CriticalLevelPercent, 0, 100);
    }

    [Fact]
    public async Task Updates_AreRead_WithAPlausibleLastDate()
    {
        var info = await WindowsHealth.GetUpdatesAsync();

        if (info.LastInstalledDate is { } date)
            Assert.InRange(date, new DateTime(2000, 1, 1), DateTime.Now.Date.AddDays(1));
    }

    [Fact]
    public async Task VirusProtection_IsRead_OrSecurityCenterIsMissing()
    {
        try
        {
            var info = await WindowsHealth.GetVirusProtectionAsync();
            Assert.DoesNotContain(info.OtherAntivirusOn, name => name.Contains("Defender", StringComparison.OrdinalIgnoreCase));
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidNamespace)
        {
            // Windows Server: no Security Center or no Defender WMI namespace.
        }
    }

    [Fact]
    public async Task MemoryIntegrity_IsRead_OrNeedsAdministratorRights()
    {
        try
        {
            _ = await WindowsHealth.IsMemoryIntegrityRunningAsync();
        }
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.AccessDenied or ManagementStatus.InvalidNamespace)
        {
            // Device Guard's class can need administrator rights (Indra has them) or be missing on some editions.
        }
    }

    [Fact]
    public void SecurityNotifications_AreRead() => _ = WindowsHealth.AreSecurityNotificationsOff();

    [Fact]
    public void ServiceStartModes_AreRead_AndAMissingServiceIsNull()
    {
        Assert.Equal(ServiceStartMode.Automatic, WindowsHealth.GetStartMode("Winmgmt"));   // WMI always starts automatically
        Assert.Null(WindowsHealth.GetStartMode($"llt_no_such_service_{Guid.NewGuid():N}"));
    }

    [Fact]
    public void TheSystemDrive_IsRead()
    {
        var drive = WindowsHealth.GetSystemDrive();

        Assert.True(drive.TotalBytes > 0);
        Assert.InRange(drive.FreeBytes, 0, drive.TotalBytes);
        Assert.StartsWith(drive.Name.TrimEnd('\\'), Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("11/11/2026", 2026, 11, 11)]
    [InlineData("3/7/2025", 2025, 3, 7)]
    [InlineData("20261014", 2026, 10, 14)]
    public void UpdateDates_InWindowsFormats_AreRead(string text, int year, int month, int day) =>
        Assert.Equal(new DateTime(year, month, day), WindowsHealth.ParseInstalledOn(text));

    [Fact]
    public void AnOldUpdatesFileTimeDate_IsRead()
    {
        var fileTime = new DateTime(2019, 6, 2, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc().ToString("x16", CultureInfo.InvariantCulture);
        Assert.Equal(new DateTime(2019, 6, 2), WindowsHealth.ParseInstalledOn(fileTime));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("13/45/2026")]
    public void AnythingElse_IsUnknown(string? text) => Assert.Null(WindowsHealth.ParseInstalledOn(text));

    [Theory]
    [InlineData(0x061100u, true)]    // on, definitions up to date
    [InlineData(0x061110u, true)]    // on, definitions out of date
    [InlineData(0x060100u, false)]   // off
    [InlineData(0x060000u, false)]   // off
    public void AnAntivirusIsOn_OnlyWhenWindowsSecuritySaysSo(uint productState, bool on) =>
        Assert.Equal(on, WindowsHealth.IsAntivirusOn(productState));
}
