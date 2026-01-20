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
/// Mapper-backed ROM device for Sega Master System cartridges or BIOS+game images.
/// </summary>
/// <remarks>
/// Exposes a banked 0x0000-0xBFFF ROM window controlled by mapper registers.
/// </remarks>
public sealed class SmsRomDevice : IMemDevice
{
    private const int BankSize = 0x4000;
    private readonly byte[] m_data;
    private readonly int m_bankCount;
    private byte m_bank0Raw;
    private byte m_bank1Raw;
    private byte m_bank2Raw;
    private byte m_bank0Mapped;
    private byte m_bank1Mapped;
    private byte m_bank2Mapped;

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

        // SMS expectation: slot 0 = bank 0; slot 1 = bank 1 (or 0 if single bank);
        // slot 2 (0x8000-0xBFFF) defaults to the last bank of the ROM.
        SetBank0(0);
        SetBank1((byte)Math.Min(1, m_bankCount - 1));
        SetBank2((byte)(m_bankCount - 1));
    }

    public void SetControl(byte value)
    {
    }

    public void SetBank0(byte value)
    {
        m_bank0Raw = NormalizeBank(value);
        m_bank0Mapped = ApplyBankShift(m_bank0Raw);
    }

    public void SetBank1(byte value)
    {
        m_bank1Raw = NormalizeBank(value);
        m_bank1Mapped = ApplyBankShift(m_bank1Raw);
    }

    public void SetBank2(byte value)
    {
        m_bank2Raw = NormalizeBank(value);
        m_bank2Mapped = ApplyBankShift(m_bank2Raw);
    }

    public byte Bank0 => m_bank0Raw;
    public byte Bank1 => m_bank1Raw;
    public byte Bank2 => m_bank2Raw;
    public int BankCount => m_bankCount;
    public int RomSize => m_data.Length;

    public byte Read8(ushort addr)
    {
        if (addr <= 0x3FFF)
        {
            if (addr < 0x0400)
                return ReadBanked(0, addr);
            return ReadBanked(m_bank0Mapped, addr);
        }
        if (addr <= 0x7FFF)
            return ReadBanked(m_bank1Mapped, addr - 0x4000);
        return ReadBanked(m_bank2Mapped, addr - 0x8000);
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

    private byte ApplyBankShift(byte bank)
    {
        if (m_bankCount == 0)
            return 0;

        // Standard Sega mapper does not apply bank shifting via 0xFFFC.
        return bank;
    }
}
