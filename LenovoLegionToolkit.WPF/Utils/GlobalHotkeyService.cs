using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// System-wide keyboard shortcuts, registered the standard Windows way (<c>RegisterHotKey</c>) on a hidden
/// message-only window. Windows itself tells LLT when the combination is pressed: there is no keyboard hook,
/// no reading of other keys and no simulated input, which keeps it out of anti-cheat territory.
/// </summary>
public sealed class GlobalHotkeyService(OsdSettings osdSettings) : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    private const int OsdToggleId = 0x4C01;

    private HwndSource? _window;
    private bool _osdToggleRegistered;

    /// <summary>Registers the saved OSD shortcut, if any. Call once on the UI thread at startup.</summary>
    public void Start()
    {
        EnsureWindow();
        var binding = osdSettings.Store.ToggleHotkey;
        if (binding is not null && !TrySetOsdToggle(binding, out var error))
            Log.Instance.Trace($"Couldn't register the OSD hotkey: {error}");
    }

    /// <summary>
    /// Replaces the OSD shortcut (null removes it). Returns false with a reason when Windows refuses,
    /// usually because another app already owns that combination.
    /// </summary>
    public bool TrySetOsdToggle(HotkeyBinding? binding, out string? error)
    {
        EnsureWindow();
        error = null;

        if (_osdToggleRegistered)
        {
            UnregisterHotKey(_window!.Handle, OsdToggleId);
            _osdToggleRegistered = false;
        }

        if (binding is null)
            return true;

        if (RegisterHotKey(_window!.Handle, OsdToggleId, binding.Modifiers | MOD_NOREPEAT, binding.VirtualKey))
        {
            _osdToggleRegistered = true;
            return true;
        }

        var code = Marshal.GetLastWin32Error();
        error = code == ERROR_HOTKEY_ALREADY_REGISTERED ? "in use" : $"Windows error {code}";
        return false;
    }

    public bool IsOsdToggleRegistered => _osdToggleRegistered;

    private void EnsureWindow()
    {
        if (_window is not null)
            return;

        // HWND_MESSAGE (-3) makes it a message-only window: never shown, never in Alt+Tab.
        _window = new HwndSource(new HwndSourceParameters("LenovoLegionToolkit.Hotkeys")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
        _window.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == OsdToggleId)
        {
            handled = true;
            MessagingCenter.Publish(new OsdChangedMessage(ToggleState.Toggle));
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_window is null)
            return;

        if (_osdToggleRegistered)
            UnregisterHotKey(_window.Handle, OsdToggleId);

        _window.RemoveHook(WndProc);
        _window.Dispose();
        _window = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
