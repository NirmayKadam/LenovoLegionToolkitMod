using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The first-run tour: every step has its text and opens a page that really exists (read from the real
/// MainWindow.xaml and SettingsPage sources), and when it opens by itself.
/// </summary>
public class TourTests
{
    private static readonly string Wpf = Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.WPF");

    [Fact]
    public void EveryStep_HasATitleAndText()
    {
        Assert.NotEmpty(TourSteps.All);
        Assert.All(TourSteps.All, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title));
            Assert.False(string.IsNullOrWhiteSpace(step.Text));
        });
    }

    [Fact]
    public void EveryStep_OpensAPageTheMainWindowHas()
    {
        var xaml = File.ReadAllText(Path.Combine(Wpf, "Windows", "MainWindow.xaml"));
        var tags = Regex.Matches(xaml, "PageTag=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

        Assert.All(TourSteps.All, step => Assert.Contains(step.PageTag, tags));
    }

    [Fact]
    public void EverySettingsStep_OpensACategoryTheSettingsPageHas()
    {
        var source = File.ReadAllText(Path.Combine(Wpf, "Pages", "SettingsPage.xaml.cs"));
        var keys = Regex.Matches(source, "Key = \"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

        Assert.All(TourSteps.All.Where(s => s.SettingsTab is not null), step =>
        {
            Assert.Equal("settings", step.PageTag);
            Assert.Contains(step.SettingsTab!, keys);
        });
    }

    [Theory]
    [InlineData(false, false, true)]    // first normal start: shown
    [InlineData(true, false, false)]    // already shown
    [InlineData(false, true, false)]    // tray / autostart start: wait for a normal start
    [InlineData(true, true, false)]
    public void ShowsByItself_OnlyOnce_OnANormalStart(bool tourShown, bool startedMinimized, bool expected)
    {
        Assert.Equal(expected, TourSteps.ShouldShowAtStart(tourShown, startedMinimized));
    }
}
