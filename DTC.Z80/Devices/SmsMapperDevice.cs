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
    private readonly SmsRomDevice m_rom;

    public ushort FromAddr => 0xFFFC;
    public ushort ToAddr => 0xFFFF;

    public SmsMapperDevice(SmsRomDevice rom)
    {
        m_rom = rom ?? throw new ArgumentNullException(nameof(rom));
    }

    public byte Read8(ushort addr) => addr switch
    {
        0xFFFC => m_rom.Control,
        0xFFFD => m_rom.Bank0,
        0xFFFE => m_rom.Bank1,
        0xFFFF => m_rom.Bank2,
        _ => 0xFF
    };

    public void Write8(ushort addr, byte value)
    {
        switch (addr)
        {
            case 0xFFFC:
                m_rom.SetControl(value);
                break;
            case 0xFFFD:
                m_rom.SetBank0(value);
                break;
            case 0xFFFE:
                m_rom.SetBank1(value);
                break;
            case 0xFFFF:
                m_rom.SetBank2(value);
                break;
        }
    }
}
