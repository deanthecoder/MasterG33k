// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using DTC.Core.Extensions;
using DTC.Emulation.Snapshot;

using DTC.Emulation;

namespace DTC.Z80.Devices;

/// <summary>
/// Minimal Sega Master System port device wrapper for the VDP.
/// </summary>
/// <remarks>
/// Routes common VDP I/O ports to the VDP and forwards everything else to a fallback device.
/// </remarks>
public sealed class SmsPortDevice : IPortDevice
{
    private readonly SmsVdp m_vdp;
    private readonly SmsJoypad m_joypad;
    private readonly SmsMemoryController m_memoryController;
    private readonly IPortDevice m_fallback;
    private readonly SmsPsg m_psg;
    private byte m_ioControl;
    private byte m_audioControl = 0x02;

    public SmsPortDevice(SmsVdp vdp, SmsJoypad joypad = null, SmsMemoryController memoryController = null, IPortDevice fallback = null, SmsPsg psg = null)
    {
        m_vdp = vdp ?? throw new ArgumentNullException(nameof(vdp));
        m_joypad = joypad;
        m_memoryController = memoryController;
        m_fallback = fallback ?? DefaultPortDevice.Instance;
        m_psg = psg;
    }

    public byte Read8(ushort portAddress)
    {
        var port = (byte)portAddress;
        switch (port)
        {
            // Memory control.
            case 0x3E:
                return m_memoryController?.Control ?? 0xFF;
            // I/O control (TH lines, region detection).
            case 0x3F:
                return m_ioControl;
            // VDP data port.
            case 0xBE:
            case 0xBC:
                return m_vdp.ReadData();
            // VDP control/status port.
            case 0xBF:
            case 0xBD:
                return m_vdp.ReadStatus();
            // V counter.
            case 0x7E:
                return m_vdp.ReadVCounter();
            // H counter.
            case 0x7F:
                return m_vdp.ReadHCounter();
            // Joypad port A.
            case 0xC0:
            case 0xDC:
                return ReadJoypadPortA();
            // Joypad port B.
            case 0xC1:
            case 0xDD:
                return ReadJoypadPortB();
            // Audio control (return non-FM signature).
            case 0xF2:
                return m_audioControl;
            default:
                return m_fallback.Read8(portAddress);
        }
    }

    public void Write8(ushort portAddress, byte value)
    {
        var port = (byte)portAddress;
        switch (port)
        {
            // Memory control.
            case 0x3E:
                m_memoryController?.WriteControl(value);
                return;
            // I/O control (TH lines, region detection).
            case 0x3F:
                HandleIoControlWrite(value);
                return;
            // VDP data port.
            case 0xBE:
                m_vdp.WriteData(value);
                return;
            // VDP control port.
            case 0xBF:
                m_vdp.WriteControl(value);
                return;
            // YM2413 address/data (ignored; FM not emulated).
            case 0xF0:
            case 0xF1:
                return;
            // Audio control (return non-FM signature regardless of writes).
            case 0xF2:
                m_audioControl = 0x02;
                return;
            // Joypad ports (write-protected).
            case 0xC0:
            case 0xC1:
            case 0xDC:
            case 0xDD:
                return; // Write-protected joypad ports.
        }

        // PSG mirrors: any port where (port & 0xC1) == 0x40/0x41.
        if ((port & 0xC1) is 0x40 or 0x41)
        {
            m_psg?.Write(value);
            return;
        }

        m_fallback.Write8(portAddress, value);
    }

    private void HandleIoControlWrite(byte value)
    {
        var previous = m_ioControl;
        m_ioControl = value;

        var risingTh0 = (previous & 0x01) == 0 && (value & 0x01) != 0;
        var risingTh1 = (previous & 0x02) == 0 && (value & 0x02) != 0;
        if (risingTh0 || risingTh1)
            m_vdp.LatchHCounter();
    }

    private byte ReadJoypadPortA()
    {
        var state = m_joypad?.GetPressedButtons() ?? SmsJoypad.SmsJoypadButtons.None;
        var value = (byte)0xFF;

        if ((state & SmsJoypad.SmsJoypadButtons.Up) != 0)
            value = value.ResetBit(0);
        if ((state & SmsJoypad.SmsJoypadButtons.Down) != 0)
            value = value.ResetBit(1);
        if ((state & SmsJoypad.SmsJoypadButtons.Left) != 0)
            value = value.ResetBit(2);
        if ((state & SmsJoypad.SmsJoypadButtons.Right) != 0)
            value = value.ResetBit(3);
        if ((state & SmsJoypad.SmsJoypadButtons.Button1) != 0)
            value = value.ResetBit(4);
        if ((state & SmsJoypad.SmsJoypadButtons.Button2) != 0)
            value = value.ResetBit(5);

        return value;
    }

    private static byte ReadJoypadPortB() => 0xFF;

    internal int GetStateSize() =>
        sizeof(byte) * 2;

    internal void SaveState(ref StateWriter writer)
    {
        writer.WriteByte(m_ioControl);
        writer.WriteByte(m_audioControl);
    }

    internal void LoadState(ref StateReader reader)
    {
        m_ioControl = reader.ReadByte();
        m_audioControl = reader.ReadByte();
    }
}

