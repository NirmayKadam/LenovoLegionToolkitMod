using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.WPF.Controls;
using Wpf.Ui.Controls;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

// The loading ring of pages and windows. WPF keeps an endless animation running whether or not its element is
// visible, so a ring that is merely hidden after loading kept animating (the trace showed it ticking with the
// window closed). The real control is used here.
public class LoadableControlTests
{
    private static (LoadableControl Control, ProgressRing Ring) Create()
    {
        var control = new LoadableControl { Content = new TextBlock() };
        control.BeginInit();
        control.EndInit();
        var ring = ((Grid)control.Content).Children.OfType<ProgressRing>().Single();
        return (control, ring);
    }

    [Fact]
    public void WhileLoading_TheRingSpins() => StaRunner.Run(() =>
    {
        var (_, ring) = Create();

        Assert.True(ring.IsIndeterminate);
        Assert.Equal(Visibility.Visible, ring.Visibility);
    });

    [Fact]
    public void AfterLoading_TheRingStopsAndIsRemovedFromLayout() => StaRunner.Run(() =>
    {
        var (control, ring) = Create();

        control.IsLoading = false;

        Assert.False(ring.IsIndeterminate);
        Assert.Equal(Visibility.Collapsed, ring.Visibility);
    });

    [Fact]
    public void LoadingAgain_SpinsAgain() => StaRunner.Run(() =>
    {
        var (control, ring) = Create();

        control.IsLoading = false;
        control.IsLoading = true;

        Assert.True(ring.IsIndeterminate);
        Assert.Equal(Visibility.Visible, ring.Visibility);
    });

    [Fact]
    public void ProgressMode_SetByTheCaller_IsKeptAcrossLoads() => StaRunner.Run(() =>
    {
        // The Packages page shows real download progress (not spinning) while loading.
        var (control, ring) = Create();
        control.IsIndeterminate = false;

        control.IsLoading = false;
        control.IsLoading = true;

        Assert.False(ring.IsIndeterminate);
        Assert.False(control.IsIndeterminate);
        Assert.Equal(Visibility.Visible, ring.Visibility);
    });

    [Fact]
    public void SwitchingToSpinning_WhileNotLoading_StartsOnlyWhenLoading() => StaRunner.Run(() =>
    {
        var (control, ring) = Create();
        control.IsLoading = false;

        control.IsIndeterminate = true;
        Assert.False(ring.IsIndeterminate);   // not loading, so nothing animates
        Assert.True(control.IsIndeterminate);  // but the caller's choice is remembered

        control.IsLoading = true;
        Assert.True(ring.IsIndeterminate);
    });
}
