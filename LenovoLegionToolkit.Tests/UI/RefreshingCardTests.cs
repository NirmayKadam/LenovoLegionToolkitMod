using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.WPF.Controls;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// The refresh behind most Dashboard cards (<see cref="AbstractRefreshingControl"/>), on a real WPF dispatcher. What the
/// card reads is scripted by the test (the real cards read the firmware); the hiding, showing and retrying are the real
/// code's.
/// </summary>
public class RefreshingCardTests
{
    /// <summary>A card whose reads answer from a list: an exception to throw, or null for a good read.</summary>
    private sealed class ScriptedCard : AbstractRefreshingControl
    {
        public Queue<Func<Task>> Reads { get; } = new();
        public int ReadCount { get; private set; }

        public Task Refresh() => RefreshAsync();

        protected override void OnFinishedLoading() { }

        protected override Task OnRefreshAsync()
        {
            ReadCount++;
            return Reads.Count > 0 ? Reads.Dequeue()() : Task.CompletedTask;
        }
    }

    private static Func<Task> Fails(Exception ex) => () => Task.FromException(ex);

    /// <summary>A small window off screen: a card refreshes when it becomes visible, and is retried only while loaded.</summary>
    private static Window Host(AbstractRefreshingControl card) =>
        new() { Content = card, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Width = 10, Height = 10, Left = -10000, Top = -10000 };

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 250; i++)   // 5 s at most
        {
            if (condition())
                return true;
            await Task.Delay(20);
        }
        return condition();
    }

    [Fact]
    public void AFailureThatPasses_HidesTheCardOnlyUntilTheRetryWorks()
    {
        StaRunner.RunWithDispatcher(async () =>
        {
            var card = new ScriptedCard { RetryDelayForTests = attempt => attempt < 3 ? TimeSpan.FromMilliseconds(50) : null };
            card.Reads.Enqueue(Fails(new TimeoutException("the firmware answered too late")));
            var window = Host(card);
            window.Show();   // shown: the card refreshes, and that first read fails
            try
            {
                Assert.True(await EventuallyAsync(() => card.ReadCount >= 1), "The card never refreshed.");
                Assert.True(await EventuallyAsync(() => card.ReadCount >= 2 && card.Visibility == Visibility.Visible), "The card stayed hidden after the failure passed.");
                Assert.True(card.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AnUnsupportedFeature_StaysHidden_AndIsNotTriedAgain()
    {
        StaRunner.RunWithDispatcher(async () =>
        {
            var card = new ScriptedCard { RetryDelayForTests = _ => TimeSpan.FromMilliseconds(10) };
            card.Reads.Enqueue(Fails(new NotSupportedException()));
            var window = Host(card);
            window.Show();
            try
            {
                Assert.True(await EventuallyAsync(() => card.ReadCount >= 1 && card.Visibility == Visibility.Collapsed), "The card wasn't hidden.");
                await Task.Delay(200);   // several retry delays, in which nothing may be read

                Assert.Equal(Visibility.Collapsed, card.Visibility);
                Assert.Equal(1, card.ReadCount);
            }
            finally
            {
                window.Close();
            }
        });
    }
    [Fact]
    public void ARefreshAskedForDuringAnother_ReadsAgainAfterIt()
    {
        StaRunner.RunWithDispatcher(async () =>
        {
            var card = new ScriptedCard();
            var firstRead = new TaskCompletionSource();
            card.Reads.Enqueue(() => firstRead.Task);   // still reading when the state changes

            var first = card.Refresh();
            var second = card.Refresh();               // e.g. the mode was changed by Fn+Q meanwhile
            firstRead.SetResult();
            await Task.WhenAll(first, second);

            Assert.Equal(2, card.ReadCount);
        });
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 15)]
    [InlineData(2, 45)]
    public void FailedRefreshes_AreTriedAgain_ThreeTimesOverAMinute(int retriesSoFar, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), AbstractRefreshingControl.RetryDelay(retriesSoFar));

    [Fact]
    public void AfterThreeRetries_TheCardStaysHidden() => Assert.Null(AbstractRefreshingControl.RetryDelay(3));
}
