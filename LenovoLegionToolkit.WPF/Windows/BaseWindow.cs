using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Utils.Skins;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Windows;

public class BaseWindow : UiWindow
{
    protected BaseWindow()
    {
        SnapsToDevicePixels = true;
        ExtendsContentIntoTitleBar = true;

        // Skins with a solid background (Neon, Retro) turn the backdrop off.
        WindowBackdropType = ThemeManager.GetBackgroundType(IoCContainer.Resolve<ThemeManager>().GetEffectiveBackdrop());

        // The skin decides the font; Fluent's value is the system font windows use by default.
        SetResourceReference(FontFamilyProperty, SkinKeys.FontFamily);

        DpiChanged += BaseWindow_DpiChanged;

        PreviewKeyDown += (s, e) => {
            if (e.Key == Key.System && e.SystemKey == Key.LeftAlt)
            {
                e.Handled = true;
                Keyboard.ClearFocus();
            }
        };  
    }

    private void BaseWindow_DpiChanged(object sender, DpiChangedEventArgs e) => VisualTreeHelper.SetRootDpi(this, e.NewDpi);
}
