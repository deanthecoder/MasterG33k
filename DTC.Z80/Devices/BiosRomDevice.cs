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
/// Simple ROM device for a Master System BIOS image.
/// </summary>
/// <remarks>
/// Maps a read-only ROM blob into a fixed address range.
/// </remarks>
public sealed class BiosRomDevice : IMemDevice
{
    private readonly byte[] m_data;

    public ushort FromAddr { get; }
    public ushort ToAddr { get; }

    public BiosRomDevice(byte[] data, ushort fromAddr = 0x0000)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (data.Length == 0)
            throw new ArgumentException("BIOS data is empty.", nameof(data));

        var endAddr = fromAddr + data.Length - 1;
        if (endAddr > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(data), "BIOS data exceeds addressable memory.");

        m_data = data;
        FromAddr = fromAddr;
        ToAddr = (ushort)endAddr;
    }

    public byte Read8(ushort addr) => m_data[addr - FromAddr];

    public void Write8(ushort addr, byte value)
    {
    }
}
