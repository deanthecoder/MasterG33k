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
/// Mirrors SMS system RAM (0xC000-0xDFFF) into 0xE000-0xFFFB with RAM gating.
/// </summary>
public sealed class SmsSystemRamMirrorDevice : IMemDevice
{
    private readonly IMemDevice m_ram;
    private readonly SmsMemoryController m_controller;

    public ushort FromAddr => 0xE000;
    public ushort ToAddr => 0xFFFB;

    public SmsSystemRamMirrorDevice(IMemDevice ram, SmsMemoryController controller)
    {
        m_ram = ram ?? throw new ArgumentNullException(nameof(ram));
        m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public byte Read8(ushort addr)
    {
        var ramAddr = (ushort)(addr - 0x2000);
        if (m_controller.IsRamEnabled || ramAddr == 0xC000)
            return m_ram.Read8(ramAddr);

        return 0xFF;
    }

    public void Write8(ushort addr, byte value)
    {
        var ramAddr = (ushort)(addr - 0x2000);
        if (m_controller.IsRamEnabled || ramAddr == 0xC000)
            m_ram.Write8(ramAddr, value);
    }
}
