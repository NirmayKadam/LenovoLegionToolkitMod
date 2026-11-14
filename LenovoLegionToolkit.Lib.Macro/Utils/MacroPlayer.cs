using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Macro.Utils;

internal class MacroPlayer
{
    private const int MAGIC_NUMBER = 1337;

    private readonly ThreadSafeBool _isPlayingInterruptableSequence = new();

    private Task _playTask = Task.CompletedTask;
    private CancellationTokenSource _cancellationTokenSource = new();

    public void InterruptIfNeeded(KBDLLHOOKSTRUCT kbStruct)
    {
        if (!_isPlayingInterruptableSequence.Value)
            return;
        if (kbStruct.flags != 0)
            return;
        if (kbStruct.dwExtraInfo == MAGIC_NUMBER)
            return;

        _cancellationTokenSource.Cancel();
    }

    /// <summary>Stops a sequence that is playing, without starting another.</summary>
    public void Stop() => _cancellationTokenSource.Cancel();

    public async Task StartPlayingAsync(MacroSequence sequence)
    {
        await _cancellationTokenSource.CancelAsync();
        try { await _playTask; }
        catch (OperationCanceledException) { }

        _cancellationTokenSource = new();
        var token = _cancellationTokenSource.Token;

        _playTask = Task.Run(async () =>
        {
            _isPlayingInterruptableSequence.Value = sequence.InterruptOnOtherKey;

            // Keys and mouse buttons this sequence has pressed and not yet released. If it is stopped half-way
            // (another key pressed, or game-safe mode starting), they are released, so nothing stays held down.
            var held = new HeldInputs();
            try
            {
                for (var i = 0; i < sequence.RepeatCount; i++)
                {
                    foreach (var macroEvent in sequence.Events ?? [])
                    {
                        if (!sequence.IgnoreDelays)
                            await Task.Delay(macroEvent.Delay, token).ConfigureAwait(false);

                        token.ThrowIfCancellationRequested();

                        if (Send(macroEvent))
                            held.Track(macroEvent);
                    }
                }
            }
            finally
            {
                foreach (var release in held.Releases())
                    Send(release);
            }
        }, token);
    }

    /// <summary>Sends one event as simulated input. False if Windows refused it.</summary>
    private static bool Send(MacroEvent macroEvent)
    {
        try
        {
            var input = ToInput(macroEvent, Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty);
            var result = PInvoke.SendInput(MemoryMarshal.CreateSpan(ref input, 1), Marshal.SizeOf<INPUT>());
            if (result == 0)
                PInvokeExtensions.ThrowIfWin32Error($"Failed to send input. Return code was {result}.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to send input for event {macroEvent}", ex);
            return false;
        }
    }

    /// <summary>
    /// Which keys and mouse buttons a playing sequence holds down: a "down" adds one, the matching "up" removes it.
    /// Wheel and move events hold nothing.
    /// </summary>
    internal sealed class HeldInputs
    {
        private readonly List<(MacroSource Source, uint Key)> _held = [];

        public void Track(MacroEvent macroEvent)
        {
            var id = (macroEvent.Source, macroEvent.Key);
            if (macroEvent.Direction == MacroDirection.Down)
            {
                if (!_held.Contains(id))
                    _held.Add(id);
            }
            else if (macroEvent.Direction == MacroDirection.Up)
            {
                _held.Remove(id);
            }
        }

        /// <summary>The "up" events for everything still held, most recently pressed first.</summary>
        public IReadOnlyList<MacroEvent> Releases()
        {
            var releases = new List<MacroEvent>();
            for (var i = _held.Count - 1; i >= 0; i--)
                releases.Add(new MacroEvent { Source = _held[i].Source, Key = _held[i].Key, Direction = MacroDirection.Up });
            return releases;
        }
    }

    private static INPUT ToInput(MacroEvent macroEvent, Rectangle screenArea) => macroEvent.Source switch
    {
        MacroSource.Keyboard => ToKeyboardInput(macroEvent),
        MacroSource.Mouse => ToMouseInput(macroEvent, screenArea),
        MacroSource.Unknown => throw new ArgumentException(null, nameof(macroEvent)),
        _ => throw new ArgumentOutOfRangeException(nameof(macroEvent))
    };

    private static INPUT ToKeyboardInput(MacroEvent macroEvent) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT
            {
                wVk = (VIRTUAL_KEY)macroEvent.Key,
                dwFlags = macroEvent.Direction switch
                {
                    MacroDirection.Up => KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP,
                    _ => 0
                },
                dwExtraInfo = MAGIC_NUMBER
            }
        }
    };

    private static INPUT ToMouseInput(MacroEvent macroEvent, Rectangle screenArea) => new()
    {
        type = INPUT_TYPE.INPUT_MOUSE,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            mi = new MOUSEINPUT
            {
                dwFlags = (macroEvent.Direction, macroEvent.Key) switch
                {
                    (MacroDirection.Up, 1) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP,
                    (MacroDirection.Down, 1) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN,
                    (MacroDirection.Up, 2) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP,
                    (MacroDirection.Down, 2) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN,
                    (MacroDirection.Up, 3) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP,
                    (MacroDirection.Down, 3) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN,
                    (MacroDirection.Up, > 0xFF) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP,
                    (MacroDirection.Down, > 0xFF) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN,
                    (MacroDirection.Wheel, _) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL,
                    (MacroDirection.Move, _) => MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE,
                    _ => 0
                },
                mouseData = (macroEvent.Direction, macroEvent.Key) switch
                {
                    (MacroDirection.Up, >= 0xFF) => macroEvent.Key >> 16,
                    (MacroDirection.Down, >= 0xFF) => macroEvent.Key >> 16,
                    (MacroDirection.Wheel, _) => macroEvent.Key,
                    _ => 0
                },
                dx = macroEvent.Direction switch
                {
                    MacroDirection.Move => (int)(65535.0f * (macroEvent.Point.X / (float)screenArea.Width) + 0.5f),
                    _ => 0
                },
                dy = macroEvent.Direction switch
                {
                    MacroDirection.Move => (int)(65535.0f * (macroEvent.Point.Y / (float)screenArea.Height) + 0.5f),
                    _ => 0
                },
                dwExtraInfo = MAGIC_NUMBER
            }
        }
    };
}
