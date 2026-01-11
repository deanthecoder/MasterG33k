// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using SharpHook;
using SharpHook.Native;

namespace DTC.Z80;

/// <summary>
/// Captures global keyboard input and tracks Sega Master System button state.
/// </summary>
public sealed class SmsJoypad : IDisposable
{
    private const int AutoFireIntervalMs = 60; // ~8 presses per second (50% duty cycle).
    private readonly SimpleGlobalHook m_keyboardHook;
    private readonly Lock m_stateLock = new();
    private readonly Timer m_autoFireTimer;
    private SmsJoypadButtons m_physicalButtons;
    private SmsJoypadButtons m_pressedButtons;
    private bool m_handlePressEvents = true;
    private SmsJoypadButtons m_autoFireHeldButtons;
    private bool m_autoFirePulseOn;

    public SmsJoypad()
    {
        m_keyboardHook = new SimpleGlobalHook();
        m_keyboardHook.KeyPressed += (_, args) => HandleKey(args.Data.KeyCode, true);
        m_keyboardHook.KeyReleased += (_, args) => HandleKey(args.Data.KeyCode, false);
        m_autoFireTimer = new Timer(_ => AutoFireTick(), null, Timeout.Infinite, Timeout.Infinite);

        m_keyboardHook.RunAsync();
    }

    /// <summary>
    /// Gets or sets whether key press events should update the button state.
    /// </summary>
    private bool HandlePressEvents
    {
        get => m_handlePressEvents;
        set
        {
            if (m_handlePressEvents == value)
                return;

            m_handlePressEvents = value;
            if (!value)
                ClearState();
        }
    }

    public SmsJoypadButtons GetPressedButtons()
    {
        lock (m_stateLock)
            return m_pressedButtons;
    }

    public IDisposable CreatePressBlocker() =>
        new PressBlocker(this);

    private void HandleKey(KeyCode keyCode, bool isPressed)
    {
        if (!m_handlePressEvents)
            return;

        if (keyCode == KeyCode.VcA)
        {
            SetAutoFireHeld(SmsJoypadButtons.Button1, isPressed);
            return;
        }

        if (keyCode == KeyCode.VcS)
        {
            SetAutoFireHeld(SmsJoypadButtons.Button2, isPressed);
            return;
        }

        if (!TryMapButton(keyCode, out var button))
            return;

        lock (m_stateLock)
        {
            if (isPressed)
                m_physicalButtons |= button;
            else
                m_physicalButtons &= ~button;

            RecomputeButtons();
        }
    }

    private static bool TryMapButton(KeyCode keyCode, out SmsJoypadButtons button)
    {
        switch (keyCode)
        {
            case KeyCode.VcUp:
                button = SmsJoypadButtons.Up;
                return true;
            case KeyCode.VcDown:
                button = SmsJoypadButtons.Down;
                return true;
            case KeyCode.VcLeft:
                button = SmsJoypadButtons.Left;
                return true;
            case KeyCode.VcRight:
                button = SmsJoypadButtons.Right;
                return true;
            case KeyCode.VcZ:
                button = SmsJoypadButtons.Button1; // Button 1 (Start).
                return true;
            case KeyCode.VcX:
                button = SmsJoypadButtons.Button2;
                return true;
            default:
                button = SmsJoypadButtons.None;
                return false;
        }
    }

    private void ClearState()
    {
        lock (m_stateLock)
        {
            m_physicalButtons = SmsJoypadButtons.None;
            ResetAutoFireStateInternal();
            RecomputeButtons();
        }
    }

    public void Dispose()
    {
        m_autoFireTimer?.Dispose();
        m_keyboardHook.Dispose();
    }

    [Flags]
    public enum SmsJoypadButtons
    {
        None = 0,
        Up = 1 << 0,
        Down = 1 << 1,
        Left = 1 << 2,
        Right = 1 << 3,
        Button1 = 1 << 4,
        Button2 = 1 << 5
    }

    private sealed class PressBlocker : IDisposable
    {
        private readonly SmsJoypad m_joypad;
        private readonly bool m_oldHandlePressEvents;

        internal PressBlocker(SmsJoypad joypad)
        {
            m_joypad = joypad;
            m_oldHandlePressEvents = joypad.HandlePressEvents;
            joypad.HandlePressEvents = false;
        }

        public void Dispose() =>
            m_joypad.HandlePressEvents = m_oldHandlePressEvents;
    }

    private void SetAutoFireHeld(SmsJoypadButtons button, bool isPressed)
    {
        lock (m_stateLock)
        {
            var wasHeld = (m_autoFireHeldButtons & button) != 0;
            if (wasHeld == isPressed)
                return;

            var hadAnyHeld = m_autoFireHeldButtons != SmsJoypadButtons.None;
            if (isPressed)
                m_autoFireHeldButtons |= button;
            else
                m_autoFireHeldButtons &= ~button;

            if (m_autoFireHeldButtons == SmsJoypadButtons.None)
            {
                ResetAutoFireStateInternal();
                RecomputeButtons();
                return;
            }

            if (!hadAnyHeld)
            {
                m_autoFirePulseOn = true; // Press immediately on engage.
                RecomputeButtons();
                m_autoFireTimer.Change(AutoFireIntervalMs, AutoFireIntervalMs);
                return;
            }

            RecomputeButtons();
        }
    }

    private void AutoFireTick()
    {
        lock (m_stateLock)
        {
            if (m_autoFireHeldButtons == SmsJoypadButtons.None)
            {
                ResetAutoFireStateInternal();
                RecomputeButtons();
                return;
            }

            m_autoFirePulseOn = !m_autoFirePulseOn;
            RecomputeButtons();
        }
    }

    private void RecomputeButtons()
    {
        var combined = m_physicalButtons;
        if (m_autoFireHeldButtons != SmsJoypadButtons.None && m_autoFirePulseOn)
            combined |= m_autoFireHeldButtons;

        m_pressedButtons = combined;
    }

    private void ResetAutoFireStateInternal()
    {
        m_autoFireHeldButtons = SmsJoypadButtons.None;
        m_autoFirePulseOn = false;
        m_autoFireTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }
}
