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
/// System RAM gated by port $3E RAM enable bit.
/// </summary>
public sealed class SmsSystemRamDevice : IMemDevice
{
    private readonly IMemDevice m_ram;
    private readonly SmsMemoryController m_controller;

    public ushort FromAddr => 0xC000;
    public ushort ToAddr => 0xDFFF;

    public SmsSystemRamDevice(IMemDevice ram, SmsMemoryController controller)
    {
        m_ram = ram ?? throw new ArgumentNullException(nameof(ram));
        m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public byte Read8(ushort addr)
    {
        if (m_controller.IsRamEnabled || addr == 0xC000)
            return m_ram.Read8(addr);

        return 0xFF;
    }

    public void Write8(ushort addr, byte value)
    {
        if (m_controller.IsRamEnabled || addr == 0xC000)
            m_ram.Write8(addr, value);
    }
}
