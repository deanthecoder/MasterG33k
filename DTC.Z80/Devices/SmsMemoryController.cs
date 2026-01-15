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

namespace DTC.Z80.Devices;

/// <summary>
/// Controls SMS memory slot enabling via port $3E and routes reads to BIOS/cartridge.
/// </summary>
public sealed class SmsMemoryController : IMemDevice
{
    private const int SlotSize = 0x4000;
    private byte[] m_bios;
    private SmsRomDevice m_biosRom;
    private SmsRomDevice m_cartridge;
    private byte m_port3E;
    private bool m_forceCartridgeEnabled;

    public ushort FromAddr => 0x0000;
    public ushort ToAddr => 0xBFFF;

    public void Reset()
        // Power-on defaults: BIOS, RAM and IO enabled; expansion/card/cartridge disabled.
    {
        m_port3E = (byte)((m_bios == null && m_biosRom == null) ? 0x00 : 0xE0);
        if (m_forceCartridgeEnabled)
            m_port3E = (byte)(m_port3E & ~0x40);
    }

    public void SetBios(byte[] bios)
    {
        m_bios = bios;
        m_biosRom = null;
    }

    public void SetBiosRom(SmsRomDevice biosRom)
    {
        m_biosRom = biosRom;
        m_bios = null;
    }

    public void SetCartridge(SmsRomDevice cartridge, bool forceEnabled = false)
    {
        m_cartridge = cartridge;
        m_forceCartridgeEnabled = forceEnabled;
        if (m_forceCartridgeEnabled)
            m_port3E = (byte)(m_port3E & ~0x40);
    }

    public void WriteControl(byte value)
    {
        if (m_forceCartridgeEnabled)
            value = (byte)(value & ~0x40);
        m_port3E = value;
    }

    public byte Control => m_port3E;

    public byte Read8(ushort addr)
    {
        if (IsBiosEnabled)
        {
            if (m_biosRom != null)
                return m_biosRom.Read8(addr);
            if (addr < SlotSize && m_bios?.Length > 0)
                return m_bios[addr % m_bios.Length];
        }

        if (IsCartridgeEnabled && m_cartridge != null)
            return m_cartridge.Read8(addr);

        return 0xFF;
    }

    public void Write8(ushort addr, byte value)
    {
    }

    public bool IsBiosEnabled => (m_port3E & 0x08) == 0;

    public bool IsCartridgeEnabled => (m_port3E & 0x40) == 0;

    public bool IsRamEnabled => (m_port3E & 0x10) == 0;

    public bool IsIoEnabled => (m_port3E & 0x04) == 0;

    public string GetDebugSummary() =>
        $"0x{m_port3E:X2} BIOS={(IsBiosEnabled ? "on" : "off")} CART={(IsCartridgeEnabled ? "on" : "off")} RAM={(IsRamEnabled ? "on" : "off")} IO={(IsIoEnabled ? "on" : "off")} BIOSSize={GetBiosSize()} Vec38={GetBiosVectorByte():X2}";

    public SmsRomDevice BiosRom => m_biosRom;

    public SmsRomDevice Cartridge => m_cartridge;

    private byte GetBiosVectorByte()
    {
        if (m_biosRom != null)
            return m_biosRom.Read8(0x0038);
        if (m_bios == null || m_bios.Length <= 0x38)
            return 0xFF;

        return m_bios[0x38];
    }

    private int GetBiosSize() => m_biosRom?.RomSize ?? m_bios?.Length ?? 0;
}
