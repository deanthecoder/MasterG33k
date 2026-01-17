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
/// Default port device that returns the upper address byte (suitable for Fuse tests).
/// </summary>
/// <remarks>
/// Acts as a stub for unmapped I/O by mirroring the high port byte on reads.
/// </remarks>
public sealed class DefaultPortDevice : IPortDevice
{
    public static DefaultPortDevice Instance { get; } = new();

    private DefaultPortDevice()
    {
    }

    public byte Read8(ushort portAddress) => (byte)(portAddress >> 8);

    public void Write8(ushort portAddress, byte value)
    {
    }
}
