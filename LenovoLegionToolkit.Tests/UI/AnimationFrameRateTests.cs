using System.Windows.Media.Animation;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

// The app-wide 30 fps cap for animations, checked on WPF's real property metadata.
public class AnimationFrameRateTests
{
    [Fact]
    public void AfterTheCap_EveryAnimationDefaultsTo30Fps_AndASecondCallIsHarmless() => StaRunner.Run(() =>
    {
        Motion.CapAnimationFrameRate();
        Motion.CapAnimationFrameRate();

        Assert.Equal(30, Timeline.DesiredFrameRateProperty.GetMetadata(typeof(Timeline)).DefaultValue);
        Assert.Equal(30, Timeline.GetDesiredFrameRate(new DoubleAnimation()));
    });
}
