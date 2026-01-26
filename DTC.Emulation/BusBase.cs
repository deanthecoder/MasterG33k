// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
namespace DTC.Emulation;

/// <summary>
/// Base bus implementation for mapping memory devices into an address space.
/// </summary>
public abstract class BusBase
{
    private readonly IMemDevice[] m_devices;

    protected BusBase(IMemDevice mainMemory, int byteSize)
    {
        if (byteSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteSize));

        MainMemory = mainMemory ?? throw new ArgumentNullException(nameof(mainMemory));
        m_devices = new IMemDevice[byteSize];
        Attach(MainMemory);
    }

    public IMemDevice MainMemory { get; }

    public void Attach(IMemDevice device)
    {
        if (device == null)
            throw new ArgumentNullException(nameof(device));

        if (device.FromAddr >= m_devices.Length || device.ToAddr >= m_devices.Length)
            throw new ArgumentOutOfRangeException(nameof(device), "Device address range is outside bus space.");

        for (var addr = (int)device.FromAddr; addr <= device.ToAddr; addr++)
            m_devices[addr] = device;
    }

    public byte Read8(ushort address)
    {
        if (address >= m_devices.Length)
            return 0xFF;
        return m_devices[address]?.Read8(address) ?? 0xFF;
    }

    public void Write8(ushort address, byte value)
    {
        if (address >= m_devices.Length)
            return;
        m_devices[address]?.Write8(address, value);
    }

    public ushort Read16(ushort address)
    {
        var lo = Read8(address);
        var hi = Read8((ushort)(address + 1));
        return (ushort)(hi << 8 | lo);
    }

    public virtual byte ReadPort(ushort portAddress) => 0xFF;

    public virtual void WritePort(ushort portAddress, byte value)
    {
    }
}
