using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Dashboard;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Extensions;

public static class DashboardItemExtensions
{
    public static SymbolRegular GetIcon(this DashboardItem dashboardItem) => dashboardItem switch
    {
        DashboardItem.PowerMode => SymbolRegular.Gauge24,
        DashboardItem.BatteryMode => SymbolRegular.BatteryCharge24,
        DashboardItem.BatteryNightChargeMode => SymbolRegular.WeatherMoon24,
        DashboardItem.AlwaysOnUsb => SymbolRegular.UsbStick24,
        DashboardItem.InstantBoot => SymbolRegular.PlugDisconnected24,
        DashboardItem.HybridMode => SymbolRegular.LeafOne24,
        DashboardItem.DiscreteGpu => SymbolRegular.DeveloperBoard24,
        DashboardItem.OverclockDiscreteGpu => SymbolRegular.DeveloperBoardLightning20,
        DashboardItem.Resolution => SymbolRegular.ScaleFill24,
        DashboardItem.RefreshRate => SymbolRegular.DesktopPulse24,
        DashboardItem.DpiScale => SymbolRegular.TextFontSize24,
        DashboardItem.Hdr => SymbolRegular.Hdr24,
        DashboardItem.AutoColorManagement => SymbolRegular.Color24,
        DashboardItem.OverDrive => SymbolRegular.TopSpeed24,
        DashboardItem.PanelLogoBacklight => SymbolRegular.LightbulbCircle24,
        DashboardItem.PortsBacklight => SymbolRegular.UsbPlug24,
        DashboardItem.TurnOffMonitors => SymbolRegular.Desktop24,
        DashboardItem.Microphone => SymbolRegular.Mic24,
        DashboardItem.FlipToStart => SymbolRegular.Power24,
        DashboardItem.TouchpadLock => SymbolRegular.Tablet24,
        DashboardItem.FnLock => SymbolRegular.Keyboard24,
        DashboardItem.WinKeyLock => SymbolRegular.Keyboard24,
        DashboardItem.WhiteKeyboardBacklight => SymbolRegular.Keyboard24,
        DashboardItem.ItsMode => SymbolRegular.Gauge24,
        _ => throw new InvalidOperationException($"Invalid DashboardItem {dashboardItem}"),
    };

    public static string GetTitle(this DashboardItem dashboardItem) => dashboardItem switch
    {
        DashboardItem.PowerMode => Resource.PowerModeControl_Title,
        DashboardItem.BatteryMode => Resource.BatteryModeControl_Title,
        DashboardItem.BatteryNightChargeMode => Resource.BatteryNightChargeModeControl_Title,
        DashboardItem.AlwaysOnUsb => Resource.AlwaysOnUSBControl_Title,
        DashboardItem.InstantBoot => Resource.InstantBootControl_Title,
        DashboardItem.HybridMode => $"{Resource.ComboBoxHybridModeControl_Title} / {Resource.ToggleHybridModeControl_Title}",
        DashboardItem.DiscreteGpu => Resource.DiscreteGPUControl_Title,
        DashboardItem.OverclockDiscreteGpu => Resource.OverclockDiscreteGPUControl_Title,
        DashboardItem.Resolution => Resource.ResolutionControl_Title,
        DashboardItem.RefreshRate => Resource.RefreshRateControl_Title,
        DashboardItem.DpiScale => Resource.DpiScaleControl_Title,
        DashboardItem.Hdr => Resource.HDRControl_Title,
        DashboardItem.AutoColorManagement => Resource.AutoColorManagementControl_Title,
        DashboardItem.OverDrive => Resource.OverDriveControl_Title,
        DashboardItem.PanelLogoBacklight => Resource.PanelLogoBacklightControl_Title,
        DashboardItem.PortsBacklight => Resource.PortsBacklightControl_Title,
        DashboardItem.TurnOffMonitors => Resource.TurnOffMonitorsControl_Title,
        DashboardItem.Microphone => Resource.MicrophoneControl_Title,
        DashboardItem.FlipToStart => Resource.FlipToStartControl_Title,
        DashboardItem.TouchpadLock => Resource.TouchpadLockControl_Title,
        DashboardItem.FnLock => Resource.FnLockControl_Title,
        DashboardItem.WinKeyLock => Resource.WinKeyControl_Title,
        DashboardItem.WhiteKeyboardBacklight => Resource.WhiteKeyboardBacklightControl_Title,
        DashboardItem.ItsMode => Resource.ITSModeControl_Title,
        _ => throw new InvalidOperationException($"Invalid DashboardItem {dashboardItem}"),
    };

    /// <summary>The hover-help key of the item's dashboard card, so the "Add" list explains it the same way.</summary>
    public static string GetHelpKey(this DashboardItem dashboardItem) => dashboardItem switch
    {
        DashboardItem.PowerMode => "Help_Dashboard_PowerMode",
        DashboardItem.BatteryMode => "Help_Feature_BatteryState",
        DashboardItem.BatteryNightChargeMode => "Help_Feature_BatteryNightChargeState",
        DashboardItem.AlwaysOnUsb => "Help_Feature_AlwaysOnUSBState",
        DashboardItem.InstantBoot => "Help_Feature_InstantBootState",
        DashboardItem.HybridMode => "Help_Feature_HybridModeState",
        DashboardItem.DiscreteGpu => "Help_Dashboard_DiscreteGpu",
        DashboardItem.OverclockDiscreteGpu => "Help_Dashboard_OverclockDiscreteGpu",
        DashboardItem.Resolution => "Help_Feature_Resolution",
        DashboardItem.RefreshRate => "Help_Feature_RefreshRate",
        DashboardItem.DpiScale => "Help_Feature_DpiScale",
        DashboardItem.Hdr => "Help_Feature_HDRState",
        DashboardItem.AutoColorManagement => "Help_Feature_AutoColorManagementState",
        DashboardItem.OverDrive => "Help_Feature_OverDriveState",
        DashboardItem.PanelLogoBacklight => "Help_Feature_PanelLogoBacklightState",
        DashboardItem.PortsBacklight => "Help_Feature_PortsBacklightState",
        DashboardItem.TurnOffMonitors => "Help_Dashboard_TurnOffMonitors",
        DashboardItem.Microphone => "Help_Feature_MicrophoneState",
        DashboardItem.FlipToStart => "Help_Feature_FlipToStartState",
        DashboardItem.TouchpadLock => "Help_Feature_TouchpadLockState",
        DashboardItem.FnLock => "Help_Feature_FnLockState",
        DashboardItem.WinKeyLock => "Help_Feature_WinKeyState",
        DashboardItem.WhiteKeyboardBacklight => "Help_Feature_WhiteKeyboardBacklightState",
        DashboardItem.ItsMode => "Help_Feature_ITSMode",
        _ => throw new InvalidOperationException($"Invalid DashboardItem {dashboardItem}"),
    };

    public static async Task<IEnumerable<AbstractRefreshingControl>> GetControlAsync(this DashboardItem dashboardItem) => dashboardItem switch
    {
        DashboardItem.PowerMode => [new PowerModeControl()],
        DashboardItem.BatteryMode => [new BatteryModeControl()],
        DashboardItem.BatteryNightChargeMode => [new BatteryNightChargeModeControl()],
        DashboardItem.AlwaysOnUsb => [new AlwaysOnUSBControl()],
        DashboardItem.InstantBoot => [new InstantBootControl()],
        DashboardItem.HybridMode => [await HybridModeControlFactory.GetControlAsync()],
        DashboardItem.DiscreteGpu => [new DiscreteGPUControl()],
        DashboardItem.OverclockDiscreteGpu => [new OverclockDiscreteGPUControl()],
        DashboardItem.Resolution => [new ResolutionControl()],
        DashboardItem.RefreshRate => [new RefreshRateControl()],
        DashboardItem.DpiScale => [new DpiScaleControl()],
        DashboardItem.Hdr => [new HDRControl()],
        DashboardItem.AutoColorManagement => [new AutoColorManagementControl()],
        DashboardItem.OverDrive => [new OverDriveControl()],
        DashboardItem.PanelLogoBacklight => [new PanelLogoBacklightControl()],
        DashboardItem.PortsBacklight => [new PortsBacklightControl()],
        DashboardItem.TurnOffMonitors => [new TurnOffMonitorsControl()],
        DashboardItem.Microphone => [new MicrophoneControl()],
        DashboardItem.FlipToStart => [new FlipToStartControl()],
        DashboardItem.TouchpadLock => [new TouchpadLockControl()],
        DashboardItem.FnLock => [new FnLockControl()],
        DashboardItem.WinKeyLock => [new WinKeyControl()],
        DashboardItem.WhiteKeyboardBacklight => [new WhiteKeyboardBacklightControl(), new OneLevelWhiteKeyboardBacklightControl()],
        DashboardItem.ItsMode => [new ITSModeControl()],
        _ => throw new InvalidOperationException($"Invalid DashboardItem {dashboardItem}"),
    };
}
