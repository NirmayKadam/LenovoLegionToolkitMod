using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The Health page's rules, from plain inputs (each test says what Windows reported), and which items notify.
/// The real Windows reads are tested in <see cref="System.WindowsHealthTests"/>.
/// </summary>
public class HealthReportTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0);
    private const long Gb = 1024L * 1024 * 1024;

    /// <summary>Everything as a healthy laptop reports it.</summary>
    private static HealthInputs Healthy() => new(
        new BatteryActionInfo(HibernateAvailable: true, WindowsHealth.ActionHibernate, 5),
        new UpdatesInfo(AutomaticUpdatesOff: false, Now.AddDays(-9)),
        new VirusProtectionInfo(DefenderRealTimeOn: true, []),
        SecurityNotificationsOff: false,
        MemoryIntegrityRunning: true,
        DisabledServices: [],
        GpuDriverErrorsLastDay: 0,
        new SystemDriveInfo("C:\\", 300 * Gb, 939 * Gb),
        new BatteryWearInfo(60000, 55000));

    private static HealthItem Item(HealthInputs inputs, HealthCheck check) => HealthReport.Evaluate(inputs, Now).Single(i => i.Check == check);

    [Fact]
    public void AHealthyLaptop_NeedsNoAttention()
    {
        var items = HealthReport.Evaluate(Healthy(), Now);

        Assert.Equal(Enum.GetValues<HealthCheck>(), items.Select(i => i.Check));
        Assert.DoesNotContain(items, i => i.State is HealthState.Attention or HealthState.Unknown);
    }

    [Fact]
    public void HibernateOnLowBattery_WithHibernationOff_NeedsAttention()
    {
        // As on the LOQ on 2026-11-20: powercfg /a "Hibernation has not been enabled", critical action 2 (hibernate).
        var item = Item(Healthy() with { BatteryAction = new BatteryActionInfo(false, WindowsHealth.ActionHibernate, 5) }, HealthCheck.BatteryAction);

        Assert.Equal(HealthState.Attention, item.State);
        Assert.Contains("5%", item.Detail);
        Assert.Equal(HealthAction.PowerOptions, item.Action);
    }

    [Theory]
    [InlineData(false, WindowsHealth.ActionShutDown, HealthState.Ok)]       // hibernation off is fine when the action doesn't need it
    [InlineData(false, WindowsHealth.ActionSleep, HealthState.Ok)]
    [InlineData(true, WindowsHealth.ActionHibernate, HealthState.Ok)]
    [InlineData(true, WindowsHealth.ActionNothing, HealthState.Attention)]  // the battery just runs out
    public void TheLowBatteryAction_IsOnlyAProblemWhenItCantSaveYourWork(bool hibernateAvailable, int action, HealthState expected) =>
        Assert.Equal(expected, Item(Healthy() with { BatteryAction = new BatteryActionInfo(hibernateAvailable, action, 5) }, HealthCheck.BatteryAction).State);

    [Fact]
    public void AutomaticUpdatesOff_NeedsAttention_AndSaysWhenTheLastUpdateWas()
    {
        // As on the LOQ: NoAutoUpdate = 1, last update installed on 2026-11-11.
        var item = Item(Healthy() with { Updates = new UpdatesInfo(true, new DateTime(2026, 11, 11)) }, HealthCheck.WindowsUpdate);

        Assert.Equal(HealthState.Attention, item.State);
        Assert.Contains("9 days ago", item.Detail);
    }

    [Theory]
    [InlineData(45, HealthState.Ok)]
    [InlineData(46, HealthState.Attention)]
    public void UpdatesOn_ButNoneForOver45Days_NeedsAttention(int daysAgo, HealthState expected) =>
        Assert.Equal(expected, Item(Healthy() with { Updates = new UpdatesInfo(false, Now.AddDays(-daysAgo)) }, HealthCheck.WindowsUpdate).State);

    [Fact]
    public void AnUnknownLastUpdateDate_IsSaidSo_NotGuessed()
    {
        var item = Item(Healthy() with { Updates = new UpdatesInfo(false, null) }, HealthCheck.WindowsUpdate);

        Assert.Equal(HealthState.Ok, item.State);
        Assert.Contains("isn't known", item.Detail);
    }

    [Fact]
    public void DefenderOff_IsFine_WhenAnotherAntivirusIsOn()
    {
        Assert.Equal(HealthState.Ok, Item(Healthy() with { VirusProtection = new VirusProtectionInfo(false, ["Kaspersky"]) }, HealthCheck.VirusProtection).State);
        Assert.Equal(HealthState.Attention, Item(Healthy() with { VirusProtection = new VirusProtectionInfo(false, []) }, HealthCheck.VirusProtection).State);
    }

    [Fact]
    public void HiddenSecurityWarnings_NeedAttention() =>
        Assert.Equal(HealthState.Attention, Item(Healthy() with { SecurityNotificationsOff = true }, HealthCheck.SecurityNotifications).State);

    [Fact]
    public void MemoryIntegrity_IsInformation_EitherWay()
    {
        Assert.Equal(HealthState.Info, Item(Healthy() with { MemoryIntegrityRunning = true }, HealthCheck.MemoryIntegrity).State);
        Assert.Equal(HealthState.Info, Item(Healthy() with { MemoryIntegrityRunning = false }, HealthCheck.MemoryIntegrity).State);
    }

    [Fact]
    public void DisabledServices_AreNamed_WithWhatBreaks()
    {
        // As on the LOQ: Xbox sign-in and Xbox accessories disabled by a tweak tool.
        var item = Item(Healthy() with { DisabledServices = ["XblAuthManager", "XboxGipSvc"] }, HealthCheck.DisabledServices);

        Assert.Equal(HealthState.Attention, item.State);
        Assert.Contains("Xbox sign-in", item.Detail);
        Assert.Contains("controllers", item.Detail);
    }

    [Fact]
    public void EveryWatchedService_HasAnExplanation() =>
        Assert.All(HealthReport.WatchedServices, name => Assert.False(string.IsNullOrWhiteSpace(
            LenovoLegionToolkit.WPF.Resources.Resource.ResourceManager.GetString($"Health_Service_{name}")), name));

    [Fact]
    public void GpuDriverErrors_NeedAttention() =>
        Assert.Equal(HealthState.Attention, Item(Healthy() with { GpuDriverErrorsLastDay = 3 }, HealthCheck.GpuDriverErrors).State);

    [Theory]
    [InlineData(300, 939, HealthState.Ok)]
    [InlineData(14, 939, HealthState.Attention)]    // under 15 GB
    [InlineData(20, 256, HealthState.Attention)]    // under 10 %
    [InlineData(30, 256, HealthState.Ok)]
    public void LowDiskSpace_NeedsAttention(long freeGb, long totalGb, HealthState expected) =>
        Assert.Equal(expected, Item(Healthy() with { SystemDrive = new SystemDriveInfo("C:\\", freeGb * Gb, totalGb * Gb) }, HealthCheck.DiskSpace).State);

    [Theory]
    [InlineData(47400, HealthState.Attention)]   // 79 %
    [InlineData(48000, HealthState.Info)]        // 80 %
    [InlineData(48600, HealthState.Info)]        // 81 %
    public void AWornBattery_NeedsAttention_Below80Percent(int fullCharge, HealthState expected) =>
        Assert.Equal(expected, Item(Healthy() with { BatteryWear = new BatteryWearInfo(60000, fullCharge) }, HealthCheck.BatteryWear).State);

    [Fact]
    public void WhatWindowsDidntAnswer_IsShownAsCouldntCheck()
    {
        var unanswered = new HealthInputs(null, null, null, null, null, null, null, null, null);

        Assert.All(HealthReport.Evaluate(unanswered, Now), item => Assert.Equal(HealthState.Unknown, item.State));
        Assert.Equal(HealthState.Unknown, Item(Healthy() with { BatteryWear = new BatteryWearInfo(0, 50000) }, HealthCheck.BatteryWear).State);
    }

    // ---- Notifications ----

    private static HealthInputs TwoProblems() => Healthy() with
    {
        BatteryAction = new BatteryActionInfo(false, WindowsHealth.ActionHibernate, 5),
        Updates = new UpdatesInfo(true, Now.AddDays(-9))
    };

    [Fact]
    public void ANewProblem_NotifiesOnce()
    {
        var first = HealthNotifier.Next([], HealthReport.Evaluate(TwoProblems(), Now));
        Assert.Equal([HealthCheck.BatteryAction, HealthCheck.WindowsUpdate], first.ToNotify.Select(i => i.Check));

        var second = HealthNotifier.Next(first.Notified, HealthReport.Evaluate(TwoProblems(), Now));
        Assert.Empty(second.ToNotify);
        Assert.Equal(first.Notified, second.Notified);
    }

    [Fact]
    public void AProblemThatWasFixed_NotifiesAgainIfItComesBack()
    {
        var notified = HealthNotifier.Next([], HealthReport.Evaluate(TwoProblems(), Now)).Notified;

        var fixedNow = HealthNotifier.Next(notified, HealthReport.Evaluate(Healthy(), Now));
        Assert.Empty(fixedNow.Notified);

        var back = HealthNotifier.Next(fixedNow.Notified, HealthReport.Evaluate(TwoProblems(), Now));
        Assert.Equal(2, back.ToNotify.Count);
    }

    [Fact]
    public void AProblemThatCouldntBeCheckedThisTime_IsntNotifiedAgainLater()
    {
        var notified = HealthNotifier.Next([], HealthReport.Evaluate(TwoProblems(), Now)).Notified;

        var unanswered = HealthNotifier.Next(notified, HealthReport.Evaluate(TwoProblems() with { BatteryAction = null }, Now));
        Assert.Contains(HealthCheck.BatteryAction, unanswered.Notified);

        var answeredAgain = HealthNotifier.Next(unanswered.Notified, HealthReport.Evaluate(TwoProblems(), Now));
        Assert.Empty(answeredAgain.ToNotify);
    }

    [Fact]
    public void GpuDriverErrors_AreNeverNotifiedHere_TheyAreAlreadyNotifiedWhenTheyHappen()
    {
        var result = HealthNotifier.Next([], HealthReport.Evaluate(Healthy() with { GpuDriverErrorsLastDay = 2 }, Now));

        Assert.Empty(result.ToNotify);
        Assert.Empty(result.Notified);
    }

    // ---- Where it appears ----

    [Fact]
    public void EveryCheck_HasHoverHelp()
    {
        // The page sets HelpKey = Help_Health_<check> on each card.
        foreach (var check in Enum.GetValues<HealthCheck>())
        {
            Assert.False(string.IsNullOrWhiteSpace(LenovoLegionToolkit.WPF.Resources.Resource.ResourceManager.GetString($"Help_Health_{check}_What")), check.ToString());
            Assert.False(string.IsNullOrWhiteSpace(LenovoLegionToolkit.WPF.Resources.Resource.ResourceManager.GetString($"Help_Health_{check}_When")), check.ToString());
        }
    }

    [Fact]
    public void TheHealthPage_IsRightUnderTheDashboardInTheSidebar()
    {
        var xaml = File.ReadAllText(Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.WPF", "Windows", "MainWindow.xaml"));
        var tags = Regex.Matches(xaml, "PageTag=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(["dashboard", "health"], tags.Take(2));
    }
}
