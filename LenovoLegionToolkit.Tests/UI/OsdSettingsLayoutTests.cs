using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The OSD settings window (reported 2026-11-25): its General tab had no scroll bar, so the last cards were cut off,
/// and its fixed-width boxes made the tab wider than the window, which pushed everything, title bar included, off-centre;
/// the window couldn't be moved. The XAML is read as XML; how the window looks is on the laptop checklist.
/// </summary>
public class OsdSettingsLayoutTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static XDocument Window() => XDocument.Load(Path.Combine(HelpContentTests.FindRepositoryRoot(),
        "LenovoLegionToolkit.WPF", "Windows", "Osd", "OsdSettingsWindow.xaml"));

    [Fact]
    public void EveryTab_Scrolls()
    {
        var tabs = Window().Descendants(Wpf + "TabItem").ToList();

        Assert.Equal(4, tabs.Count);
        Assert.All(tabs, tab => Assert.Equal("ScrollViewer", tab.Elements().Single().Name.LocalName));
    }

    [Fact]
    public void TheGeneralTab_IsNeverWiderThanTheWindow()
    {
        var general = Window().Descendants(Wpf + "TabItem").First().Elements().Single();

        Assert.Equal("Disabled", (string?)general.Attribute("HorizontalScrollBarVisibility"));
    }

    [Fact]
    public void TheTitle_MovesTheWindow_WithoutCoveringTheCloseButton()
    {
        var title = Window().Descendants(Wpf + "Border").Single(b => (string?)b.Attribute("MouseLeftButtonDown") == "TitleArea_MouseLeftButtonDown");

        Assert.Equal("Transparent", (string?)title.Attribute("Background"));   // without a background it can't be clicked
        Assert.EndsWith(",48,0", (string?)title.Attribute("Margin"));          // the close button's space on the right
    }
}
