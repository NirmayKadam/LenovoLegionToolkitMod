using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>Tests that use the process-wide WPF <see cref="Application"/> run alone, and put back what they add.</summary>
[CollectionDefinition(nameof(WpfApplicationCollection), DisableParallelization = true)]
public sealed class WpfApplicationCollection;

[Collection(nameof(WpfApplicationCollection))]
public class SkinThemeSwitchTests
{
    [Fact]
    public void SkinDictionaryMergedLast_WinsOverWpfUiTheme_AndSurvivesThemeSwitch()
    {
        // ThemeManager relies on this: WPF-UI's Theme.Apply swaps its theme dictionary in place, so a skin
        // dictionary merged after it keeps winning across Light/Dark changes. Uses the real WPF-UI calls, which work
        // on the one Application of the process; what this test adds to it is removed again at the end.
        StaRunner.Run(() =>
        {
            var app = Application.Current ?? new Application();
            var hadMainWindow = app.MainWindow is not null;
            // As in App.xaml.cs: the main window exists (not yet shown) when the theme is first applied.
            app.MainWindow ??= new Window();
            var merged = app.Resources.MergedDictionaries;
            var countBefore = merged.Count;
            var theme = new ThemesDictionary { Theme = ThemeType.Dark };
            merged.Add(theme);

            var probe = SkinPaletteTests.ProbePalette();
            var skin = probe.ToResourceDictionary();
            merged.Add(skin);

            try
            {
                Color Resolve(string key) => ((SolidColorBrush)app.FindResource(key)).Color;

                // Not overridden by the skin, so it shows which WPF-UI theme is active.
                Assert.Equal(Color.FromRgb(0x9D, 0x9D, 0x9D), Resolve("SystemFillColorSolidNeutralBrush"));
                Assert.Equal(probe.Colors!.TextPrimary, Resolve("TextFillColorPrimaryBrush"));

                Theme.Apply(ThemeType.Light, BackgroundType.None, false);

                Assert.Equal(Color.FromRgb(0x8A, 0x8A, 0x8A), Resolve("SystemFillColorSolidNeutralBrush"));
                Assert.Same(skin, merged[merged.Count - 1]);
                Assert.Equal(probe.Colors.TextPrimary, Resolve("TextFillColorPrimaryBrush"));
                Assert.Equal(probe.Colors.ApplicationBackground, Resolve("ApplicationBackgroundBrush"));
            }
            finally
            {
                // Theme.Apply replaces the theme dictionary in place, so everything after the original ones is ours.
                while (merged.Count > countBefore)
                    merged.RemoveAt(merged.Count - 1);
                if (!hadMainWindow)
                {
                    app.MainWindow?.Close();
                    app.MainWindow = null;
                }
            }
        });
    }
}
