using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace LenovoLegionToolkit.Lib.Features;

public class PrecisionTouchpadLockFeature : IFeature<TouchpadLockState>
{
    private const short PrecisionTouchPad = 0x12;
    private const uint Supported = 0x18;

    public async Task<bool> IsSupportedAsync()
    {
        if (AppFlags.Instance.Debug)
        {
            return true;
        }

        return await WMI.LenovoUtilityData.GetFeatureSupportStateAsync(PrecisionTouchPad, Supported).ConfigureAwait(false);
    }

    public Task<TouchpadLockState> GetStateAsync()
    {
        try
        {
            return Task.FromResult(ReadState());
        }
        catch (Exception ex)
        {
            // Errors still arrive through the task, as callers expect.
            return Task.FromException<TouchpadLockState>(ex);
        }
    }

    private static TouchpadLockState ReadState()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status", false);

        object? value = key?.GetValue("Enabled");
        if (value is int enabled && enabled != 0)
        {
            return TouchpadLockState.Off;
        }

        return TouchpadLockState.On;
    }

    public Task<TouchpadLockState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<TouchpadLockState>());

    public async Task SetStateAsync(TouchpadLockState state)
    {
        var currentState = await GetStateAsync();
        if (currentState == state)
        {
            return;
        }

        SendCtrlWinF24();
    }

    public void SendCtrlWinF24()
    {
        const ushort VK_CONTROL = 0x11;
        const ushort VK_LWIN = 0x5B;
        const ushort VK_F24 = 0x87;

        var inputs = new INPUT[]
        {
            KeyboardInput(VK_CONTROL, false),
            KeyboardInput(VK_LWIN,    false),
            KeyboardInput(VK_F24,     false),
            KeyboardInput(VK_F24,     true),
            KeyboardInput(VK_LWIN,    true),
            KeyboardInput(VK_CONTROL, true)
        };

        int size = Marshal.SizeOf<INPUT>(default);
        PInvoke.SendInput(new ReadOnlySpan<INPUT>(inputs), size);
    }

    private static INPUT KeyboardInput(ushort key, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = (VIRTUAL_KEY)key,
                    dwFlags = keyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0,
                    dwExtraInfo = 0
                }
            }
        };
    }
}