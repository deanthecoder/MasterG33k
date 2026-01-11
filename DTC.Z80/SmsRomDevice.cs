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

namespace DTC.Z80;

/// <summary>
/// Mapper-backed ROM device for Sega Master System cartridges or BIOS+game images.
/// </summary>
public sealed class SmsRomDevice : IMemDevice
{
    private const int BankSize = 0x4000;
    private readonly byte[] m_data;
    private readonly int m_bankCount;
    private byte m_bank0;
    private byte m_bank1;
    private byte m_bank2;
    private byte m_control;

    public ushort FromAddr => 0x0000;
    public ushort ToAddr => 0xBFFF;

    public SmsRomDevice(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (data.Length == 0)
            throw new ArgumentException("ROM data is empty.", nameof(data));

        m_data = data;
        m_bankCount = Math.Max(1, (data.Length + BankSize - 1) / BankSize);
        SetBank0(0);
        SetBank1(1);
        SetBank2(2);
    }

    public void SetControl(byte value) => m_control = value;

    public byte Control => m_control;

    public void SetBank0(byte value) => m_bank0 = NormalizeBank(value);

    public void SetBank1(byte value) => m_bank1 = NormalizeBank(value);

    public void SetBank2(byte value) => m_bank2 = NormalizeBank(value);

    public byte Bank0 => m_bank0;
    public byte Bank1 => m_bank1;
    public byte Bank2 => m_bank2;
    public int BankCount => m_bankCount;
    public int RomSize => m_data.Length;

    public byte Read8(ushort addr)
    {
        if (addr <= 0x3FFF)
            return ReadBanked(m_bank0, addr);
        if (addr <= 0x7FFF)
            return ReadBanked(m_bank1, addr - 0x4000);
        return ReadBanked(m_bank2, addr - 0x8000);
    }

    public void Write8(ushort addr, byte value)
    {
    }

    private byte ReadBanked(byte bank, int offset)
    {
        var index = bank * BankSize + offset;
        if (index < 0 || index >= m_data.Length)
            return 0xFF;
        return m_data[index];
    }

    private byte NormalizeBank(byte bank) =>
        (byte)(m_bankCount == 0 ? 0 : bank % m_bankCount);
}
