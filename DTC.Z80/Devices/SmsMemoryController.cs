// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any
// purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using DTC.Emulation.Snapshot;

using DTC.Emulation;

namespace DTC.Z80.Devices;

/// <summary>
/// Controls SMS memory slot enabling via port $3E and routes reads to BIOS/cartridge.
/// </summary>
public sealed class SmsMemoryController : IMemDevice
{
    private const int SlotSize = 0x4000;
    private byte[] m_bios;
    private bool m_forceCartridgeEnabled;

    public ushort FromAddr => 0x0000;
    public ushort ToAddr => 0xBFFF;

    public void Reset()
    {
        // Power-on defaults: BIOS, RAM and IO enabled; expansion/card/cartridge disabled.
        Control = (byte)(m_bios == null && BiosRom == null ? 0x00 : 0xE0);
        if (m_forceCartridgeEnabled)
            Control = (byte)(Control & ~0x40);
    }

    public void SetBios(byte[] bios)
    {
        m_bios = bios;
        BiosRom = null;
    }

    public void SetBiosRom(SmsRomDevice biosRom)
    {
        BiosRom = biosRom;
        m_bios = null;
    }

    public void SetCartridge(SmsRomDevice cartridge, bool forceEnabled = false)
    {
        Cartridge = cartridge;
        m_forceCartridgeEnabled = forceEnabled;
        if (m_forceCartridgeEnabled)
            Control = (byte)(Control & ~0x40);
    }

    public void WriteControl(byte value)
    {
        if (m_forceCartridgeEnabled)
            value = (byte)(value & ~0x40);
        Control = value;
    }

    public byte Control { get; private set; }

    public byte Read8(ushort addr)
    {
        if (IsBiosEnabled)
        {
            if (BiosRom != null)
                return BiosRom.Read8(addr);
            if (addr < SlotSize && m_bios?.Length > 0)
                return m_bios[addr % m_bios.Length];
        }

        if (IsCartridgeEnabled && Cartridge != null)
            return Cartridge.Read8(addr);

        return 0xFF;
    }

    public void Write8(ushort addr, byte value)
    {
    }

    public bool IsBiosEnabled => (Control & 0x08) == 0;

    public bool IsCartridgeEnabled => (Control & 0x40) == 0;

    public bool IsRamEnabled => (Control & 0x10) == 0;

    public SmsRomDevice BiosRom { get; private set; }

    public SmsRomDevice Cartridge { get; private set; }

    internal int GetStateSize() =>
        sizeof(byte) + sizeof(byte);

    internal void SaveState(ref StateWriter writer)
    {
        writer.WriteByte(Control);
        writer.WriteBool(m_forceCartridgeEnabled);
    }

    internal void LoadState(ref StateReader reader)
    {
        var control = reader.ReadByte();
        m_forceCartridgeEnabled = reader.ReadBool();
        Control = control;
        if (m_forceCartridgeEnabled)
            Control = (byte)(Control & ~0x40);
    }
}

