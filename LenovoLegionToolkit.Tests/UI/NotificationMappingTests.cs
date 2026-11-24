using System;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// Every notification type can be shown. Extreme mode's had no case in two of the lists, so showing it threw and the
/// notification was silently lost. The real lists and the app's resources are used.
/// </summary>
public class NotificationMappingTests
{
    [Fact]
    public void EveryNotificationType_HasItsSetting_ItsText_AndItsIcon()
    {
        var store = new NotificationSettings.NotificationSettingsStore();

        foreach (var type in Enum.GetValues<NotificationType>())
        {
            var error = Record.Exception(() =>
            {
                _ = NotificationsManager.IsAllowed(type, store);
                _ = NotificationsManager.Text(type, "x");
                _ = NotificationsManager.GetDefaultSymbol(type);
                _ = NotificationsManager.GetDefaultOverlaySymbol(type);
            });

            Assert.True(error is null, $"{type}: {error?.Message}");
        }
    }

    [Fact]
    public void ExtremeMode_FollowsThePowerModeSetting_LikeTheOtherModes()
    {
        var store = new NotificationSettings.NotificationSettingsStore();

        store.Notifications.PowerMode = true;
        Assert.True(NotificationsManager.IsAllowed(NotificationType.PowerModeExtreme, store));
        store.Notifications.PowerMode = false;
        Assert.False(NotificationsManager.IsAllowed(NotificationType.PowerModeExtreme, store));
        Assert.Equal("Extreme", NotificationsManager.Text(NotificationType.PowerModeExtreme, "Extreme"));
    }
}
