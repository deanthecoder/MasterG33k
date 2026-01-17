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
/// Represents a port-mapped device.
/// </summary>
/// <remarks>
/// Implementations translate 8-bit I/O port accesses to device behavior.
/// </remarks>
public interface IPortDevice
{
    byte Read8(ushort portAddress);
    void Write8(ushort portAddress, byte value);
}
