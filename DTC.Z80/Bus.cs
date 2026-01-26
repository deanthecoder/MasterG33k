// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using DTC.Emulation;
using DTC.Z80.Devices;

namespace DTC.Z80;

/// <summary>
/// Minimal Z80 bus with device mapping for memory and ports.
/// </summary>
public sealed class Bus : BusBase
{
    private readonly IPortDevice m_portDevice;

    public Bus(Memory memory, IPortDevice portDevice = null, int byteSize = 0x10000)
        : base(memory, byteSize)
    {
        m_portDevice = portDevice ?? DefaultPortDevice.Instance;
        MainMemory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public new Memory MainMemory { get; }

    public override byte ReadPort(ushort portAddress) =>
        m_portDevice?.Read8(portAddress) ?? 0xFF;

    public override void WritePort(ushort portAddress, byte value) =>
        m_portDevice?.Write8(portAddress, value);
}
