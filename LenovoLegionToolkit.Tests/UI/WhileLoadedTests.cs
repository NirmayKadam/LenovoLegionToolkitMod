using System;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.WPF.Extensions;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// Dashboard and automation cards listen to long-lived listeners only while they are on screen. They used to subscribe
/// in their constructor and never unsubscribe, so every card ever built stayed alive and kept reacting to events.
/// Loaded and Unloaded are raised the way WPF raises them when a card comes on and goes off screen.
/// </summary>
public class WhileLoadedTests
{
    /// <summary>Stands in for a listener that lives as long as the app.</summary>
    private sealed class LongLivedSource
    {
        public event EventHandler? Changed;

        public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
    }

    private static void Load(FrameworkElement element) => element.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

    private static void Unload(FrameworkElement element) => element.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

    private static (UserControl Card, LongLivedSource Source) CreateCard()
    {
        var source = new LongLivedSource();
        var card = new UserControl();
        EventHandler handler = (_, _) => { };
        card.WhileLoaded(() => source.Changed += handler, () => source.Changed -= handler);
        return (card, source);
    }

    [Fact]
    public void ACard_ListensOnlyWhileOnScreen() => StaRunner.Run(() =>
    {
        var (card, source) = CreateCard();
        Assert.Equal(0, source.Subscribers);

        Load(card);
        Assert.Equal(1, source.Subscribers);

        Unload(card);
        Assert.Equal(0, source.Subscribers);
    });

    [Fact]
    public void ComingBackOnScreen_DoesNotAddASecondSubscription() => StaRunner.Run(() =>
    {
        var (card, source) = CreateCard();

        Load(card);
        Load(card);       // WPF can raise Loaded again without an Unloaded in between
        Unload(card);
        Load(card);

        Assert.Equal(1, source.Subscribers);
    });

    [Fact]
    public void ACardTakenOffScreenForGood_LeavesNothingBehindOnTheListener() => StaRunner.Run(() =>
    {
        var (card, source) = CreateCard();

        for (var visit = 0; visit < 20; visit++)
        {
            Load(card);
            Unload(card);
        }

        Assert.Equal(0, source.Subscribers);
    });
}
