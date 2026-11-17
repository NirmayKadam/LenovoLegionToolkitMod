using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.WPF.Controls.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

public class WindowsPowerCardsTests
{
    /// <summary>
    /// The Power mode profiles window shows the Windows setting LLT really changes with the power mode: the plan
    /// cards for the plan mapping, the power-mode cards for the power-mode mapping (only where Windows has power
    /// modes), and a note when the mapping is off.
    /// </summary>
    [Theory]
    [InlineData(PowerModeMappingMode.WindowsPowerPlan, true, WindowsPowerSection.PowerPlan)]
    [InlineData(PowerModeMappingMode.WindowsPowerPlan, false, WindowsPowerSection.PowerPlan)]
    [InlineData(PowerModeMappingMode.WindowsPowerMode, true, WindowsPowerSection.PowerMode)]
    [InlineData(PowerModeMappingMode.WindowsPowerMode, false, WindowsPowerSection.None)]   // older Windows: no power modes to set
    [InlineData(PowerModeMappingMode.Disabled, true, WindowsPowerSection.None)]
    public void SectionFor_MatchesWhatLltChanges(PowerModeMappingMode mapping, bool overlaySupported, WindowsPowerSection expected) =>
        Assert.Equal(expected, WindowsPowerCards.SectionFor(mapping, overlaySupported));
}
