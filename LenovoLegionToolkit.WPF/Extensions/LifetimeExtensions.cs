using System;
using System.Windows;

namespace LenovoLegionToolkit.WPF.Extensions;

public static class LifetimeExtensions
{
    /// <summary>
    /// Subscribes to a long-lived object's event (a listener, a controller) only while <paramref name="element"/> is on
    /// screen: from Loaded to Unloaded, at most once at a time. A control that subscribed in its constructor and never
    /// unsubscribed stayed alive for as long as the app ran, even after it was taken off the page, and kept reacting to
    /// every event. Cards refresh when they become visible again, so nothing is missed while they are off screen.
    /// </summary>
    public static void WhileLoaded(this FrameworkElement element, Action subscribe, Action unsubscribe)
    {
        var subscribed = false;

        void On()
        {
            if (subscribed)
                return;
            subscribe();
            subscribed = true;
        }

        void Off()
        {
            if (!subscribed)
                return;
            unsubscribe();
            subscribed = false;
        }

        element.Loaded += (_, _) => On();
        element.Unloaded += (_, _) => Off();

        if (element.IsLoaded)
            On();
    }
}
