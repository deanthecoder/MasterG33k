// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
namespace DTC.Z80.Devices;

/// <summary>
/// Simple linear RAM device.
/// </summary>
/// <remarks>
/// Provides flat read/write storage for the full 64K address space by default.
/// </remarks>
public sealed class Memory : IMemDevice
{
    public byte[] Data { get; }
    public ushort FromAddr { get; }
    public ushort ToAddr { get; }

    public Memory(int size = 0x10000)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        Data = new byte[size];
        FromAddr = 0x0000;
        ToAddr = (ushort)(size - 1);
    }

    public byte Read8(ushort address) => Data[address % Data.Length];

    public void Write8(ushort address, byte value) => Data[address % Data.Length] = value;
}
