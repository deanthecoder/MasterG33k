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
/// Mirrors mapper registers at 0xFFFC-0xFFFF into the 0xDFFC-0xDFFF RAM region.
/// </summary>
public sealed class SmsMapperMirrorDevice : IMemDevice
{
    private readonly SmsMapperDevice m_mapper;

    public ushort FromAddr => 0xDFFC;
    public ushort ToAddr => 0xDFFF;

    public SmsMapperMirrorDevice(SmsMapperDevice mapper)
    {
        m_mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public byte Read8(ushort addr) =>
        m_mapper.Read8((ushort)(addr + 0x2000));

    public void Write8(ushort addr, byte value) =>
        m_mapper.Write8((ushort)(addr + 0x2000), value);
}
