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
    private byte m_ioControl;

    public SmsPortDevice(SmsVdp vdp, SmsJoypad joypad = null, SmsMemoryController memoryController = null, IPortDevice fallback = null)
    {
        m_vdp = vdp ?? throw new ArgumentNullException(nameof(vdp));
        m_joypad = joypad;
        m_memoryController = memoryController;
        m_fallback = fallback ?? DefaultPortDevice.Instance;
    }

    public byte Read8(ushort portAddress)
    {
        var port = (byte)portAddress;
        switch (port)
        {
            case 0x3E:
                return m_memoryController?.Control ?? 0xFF;
            case 0x3F:
                return m_ioControl;
            case 0xBE:
            case 0xBC:
                return m_vdp.ReadData();
            case 0xBF:
            case 0xBD:
                return m_vdp.ReadStatus();
            case 0x7E:
                return m_vdp.ReadVCounter();
            case 0x7F:
                return m_vdp.ReadHCounter();
            case 0xC0:
            case 0xDC:
                return ReadJoypadPortA();
            case 0xC1:
            case 0xDD:
                return ReadJoypadPortB();
            default:
                return m_fallback.Read8(portAddress);
        }
    }

    public void Write8(ushort portAddress, byte value)
    {
        var port = (byte)portAddress;
        switch (port)
        {
            case 0x3E:
                m_memoryController?.WriteControl(value);
                return;
            case 0x3F:
                HandleIoControlWrite(value);
                return;
            case 0xBE:
                m_vdp.WriteData(value);
                return;
            case 0xBC:
                m_vdp.WriteData(value);
                return;
            case 0xBF:
                m_vdp.WriteControl(value);
                return;
            case 0xBD:
                m_vdp.WriteControl(value);
                return;
            case 0xC0:
            case 0xC1:
            case 0xDC:
            case 0xDD:
                return; // Write-protected joypad ports.
            default:
                m_fallback.Write8(portAddress, value);
                return;
        }
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

    private byte ReadJoypadPortB() => 0xFF;
}
