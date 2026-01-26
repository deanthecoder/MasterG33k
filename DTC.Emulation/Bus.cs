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
using DTC.Emulation.Devices;

namespace DTC.Emulation;

/// <summary>
/// Minimal bus with device mapping for memory and ports.
/// </summary>
public sealed class Bus
{
    private readonly IMemDevice[] m_devices;
    private readonly IPortDevice m_portDevice;

    public Bus(int byteSize, IPortDevice portDevice = null)
        : this(new Memory(byteSize), portDevice)
    {
    }

    public Bus(Memory memory, IPortDevice portDevice = null)
    {
        MainMemory = memory ?? throw new ArgumentNullException(nameof(memory));
        if (MainMemory.ToAddr < MainMemory.FromAddr)
            throw new ArgumentOutOfRangeException(nameof(memory), "Memory address range is invalid.");

        m_portDevice = portDevice;
        var size = MainMemory.ToAddr + 1;
        m_devices = new IMemDevice[size];
        Attach(MainMemory);
    }

    public Memory MainMemory { get; }

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

    public byte ReadPort(ushort portAddress) =>
        m_portDevice?.Read8(portAddress) ?? 0xFF;

    public void WritePort(ushort portAddress, byte value) =>
        m_portDevice?.Write8(portAddress, value);
}
