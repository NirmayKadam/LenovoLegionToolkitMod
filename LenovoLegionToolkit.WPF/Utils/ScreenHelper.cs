using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace LenovoLegionToolkit.WPF.Utils;

public static unsafe class ScreenHelper
{
    public static List<ScreenInfo> Screens { get; } = [];

    public static ScreenInfo? PrimaryScreen => Screens.FirstOrDefault(s => s.IsPrimary);

    public static void UpdateScreenInfos()
    {
        Screens.Clear();
        PInvoke.EnumDisplayMonitors(default, null, MonitorEnumProc, default);
    }

    private static BOOL MonitorEnumProc(HMONITOR hMonitor, HDC hdcMonitor, RECT* lprcMonitor, LPARAM dwData)
    {
        MONITORINFO monitorInfo = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };

        if (!PInvoke.GetMonitorInfo(hMonitor, &monitorInfo))
            return (BOOL)true;

#pragma warning disable CA1416
        if (!PInvoke.GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY).Succeeded)
#pragma warning restore CA1416
            return (BOOL)true;

        var workArea = monitorInfo.rcWork;
        var multiplierX = 96d / dpiX;
        var multiplierY = 96d / dpiY;

        Screens.Add(new ScreenInfo(
            new Rect(workArea.left, workArea.top, (workArea.right - workArea.left) * multiplierX, (workArea.bottom - workArea.top) * multiplierY),
            dpiX, dpiY,
            (monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0
        ));

        return (BOOL)true;
    }
}
