// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.Z80;

/// <summary>
/// Minimal Z80 bus stub. Provides a flat 64KB address space for early CPU testing.
/// </summary>
public sealed class Bus
{
    private readonly Memory m_memory;

    public Bus(Memory memory)
    {
        m_memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public byte Read8(ushort address) => m_memory.Read8(address);

    public void Write8(ushort address, byte value) => m_memory.Write8(address, value);

    public ushort Read16(ushort address) => m_memory.Read16(address);

    public void Write16(ushort address, ushort value) => m_memory.Write16(address, value);

    
}
