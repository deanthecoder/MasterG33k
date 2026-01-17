// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any non-commercial
// purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using DTC.Z80;

namespace UnitTests.FuseUtils;

/// <summary>
/// Fuse test port device: returns the upper address byte.
/// </summary>
public sealed class FusePortDevice : IPortDevice
{
    public byte Read8(ushort portAddress) => (byte)(portAddress >> 8);

    public void Write8(ushort portAddress, byte value)
    {
    }
}
