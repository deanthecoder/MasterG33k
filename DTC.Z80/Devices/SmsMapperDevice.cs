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
/// Mapper register device for Sega Master System ROM banking.
/// </summary>
/// <remarks>
/// Provides the 0xFFFC-0xFFFF control registers that select ROM banks.
/// </remarks>
public sealed class SmsMapperDevice : IMemDevice
{
    private readonly SmsMemoryController m_memoryController;
    private readonly IMemDevice m_ram;

    public ushort FromAddr => 0xFFFC;
    public ushort ToAddr => 0xFFFF;

    public SmsMapperDevice(SmsMemoryController memoryController, IMemDevice ram)
    {
        m_memoryController = memoryController ?? throw new ArgumentNullException(nameof(memoryController));
        m_ram = ram ?? throw new ArgumentNullException(nameof(ram));
    }

    public byte Read8(ushort addr)
    {
        var ramAddr = (ushort)(addr - 0x2000);
        return m_ram.Read8(ramAddr);
    }

    public void Write8(ushort addr, byte value)
    {
        var ramAddr = (ushort)(addr - 0x2000);
        m_ram.Write8(ramAddr, value);
        var rom = GetActiveRom();
        if (rom == null)
            return;
        switch (addr)
        {
            case 0xFFFC:
                rom.SetControl(value);
                break;
            case 0xFFFD:
                rom.SetBank0(value);
                break;
            case 0xFFFE:
                rom.SetBank1(value);
                break;
            case 0xFFFF:
                rom.SetBank2(value);
                break;
        }
    }

    private SmsRomDevice GetActiveRom()
    {
        if (m_memoryController.IsCartridgeEnabled)
            return m_memoryController.Cartridge;
        if (m_memoryController.IsBiosEnabled)
            return m_memoryController.BiosRom;
        return null;
    }
}
