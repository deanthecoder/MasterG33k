// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using DTC.Z80.Devices;

namespace DTC.Z80;

/// <summary>
/// Minimal Z80 bus with device mapping for memory and ports.
/// </summary>
public sealed class Bus
{
    private readonly IMemDevice[] m_devices = new IMemDevice[0x10000];

    public Memory MainMemory { get; }
    public IPortDevice PortDevice { get; private set; }

    public Bus(Memory memory, IPortDevice portDevice = null)
    {
        MainMemory = memory ?? throw new ArgumentNullException(nameof(memory));
        PortDevice = portDevice ?? DefaultPortDevice.Instance;
        Attach(MainMemory);
    }
    
    public void Attach(IMemDevice device)
    {
        if (device == null)
            throw new ArgumentNullException(nameof(device));

        for (var addr = (int)device.FromAddr; addr <= device.ToAddr; addr++)
            m_devices[addr] = device;
    }

    public byte Read8(ushort address) =>
        m_devices[address]?.Read8(address) ?? 0xFF;

    public void Write8(ushort address, byte value) =>
        m_devices[address]?.Write8(address, value);

    public ushort Read16(ushort address)
    {
        var lo = Read8(address);
        var hi = Read8((ushort)(address + 1));
        return (ushort)(hi << 8 | lo);
    }

    public void Write16(ushort address, ushort value)
    {
        Write8(address, (byte)(value & 0xFF));
        Write8((ushort)(address + 1), (byte)(value >> 8));
    }

    public byte ReadPort(ushort portAddress) =>
        PortDevice?.Read8(portAddress) ?? 0xFF;

    public void WritePort(ushort portAddress, byte value) =>
        PortDevice?.Write8(portAddress, value);
}
