using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Help;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

// The real control with a real help key from the app's resources. The title is rebuilt whenever the title, the help
// key or the hover-help setting changes; rebuilding twice used to throw "Specified element is already the logical
// child of another element" and stopped the dashboard from loading.
[Collection("HelpPresenter")]
public class CardHeaderControlTests
{
    private const string RealHelpKey = "Help_Feature_AlwaysOnUSBState";

    private static CardHeaderControl CreateInitialized()
    {
        var control = new CardHeaderControl();
        control.BeginInit();
        control.EndInit();
        return control;
    }

    private static int HelpIconCount(CardHeaderControl control)
    {
        var grid = Assert.IsType<Grid>(control.Content);
        var title = grid.Children.OfType<TextBlock>().Single();
        return title.Inlines.OfType<InlineUIContainer>().Count();
    }

    [Fact]
    public void RealHelpKey_HasText() => Assert.NotNull(HelpContent.Resolve(RealHelpKey));

    [Fact]
    public void TitleSetAfterHelpKey_DoesNotThrow() => StaRunner.Run(() =>
    {
        var control = CreateInitialized();

        control.HelpKey = RealHelpKey;
        control.Title = "Always on USB";
        control.Title = "Always on USB (renamed)";
    });

    [Fact]
    public void SeveralRebuilds_LeaveExactlyOneHelpIcon() => StaRunner.Run(() =>
    {
        var control = CreateInitialized();

        control.Title = "Always on USB";
        control.HelpKey = RealHelpKey;
        control.Title = "Always on USB (renamed)";
        control.HelpKey = RealHelpKey;

        Assert.Equal(1, HelpIconCount(control));
    });

    [Fact]
    public void TurningHoverHelpOffAndOn_RebuildsWithoutThrowing() => StaRunner.Run(() =>
    {
        var wasEnabled = HelpPresenter.IsEnabled;
        var delay = HelpPresenter.InitialDelayMs;

        try
        {
            HelpPresenter.Configure(true, delay);

            var control = CreateInitialized();
            control.HelpKey = RealHelpKey;
            control.Title = "Always on USB";
            Assert.Equal(1, HelpIconCount(control));

            // The control listens for the setting only while loaded in a window, so the change is followed by a
            // title update here, which rebuilds the same way.
            HelpPresenter.Configure(false, delay);
            control.Title = "Always on USB";
            Assert.Equal(0, HelpIconCount(control));

            HelpPresenter.Configure(true, delay);
            control.Title = "Always on USB";
            Assert.Equal(1, HelpIconCount(control));
        }
        finally
        {
            HelpPresenter.Configure(wasEnabled, delay);
        }
    });
}
