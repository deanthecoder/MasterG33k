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
using DTC.Emulation.Audio;
using DTC.Z80.Devices;

namespace DTC.Z80;

/// <summary>
/// Creates Master System hardware components in one explicit location.
/// </summary>
internal sealed class MachineFactory : MachineFactoryBase<Bus, Cpu, SmsVdp, SmsPsg, SmsJoypad>
{
    private readonly IMachineDescriptor m_descriptor;
    private readonly SoundDevice m_audioSink;
    private SmsVdp m_vdp;
    private SmsPsg m_psg;

    public MachineFactory(IMachineDescriptor descriptor, SoundDevice audioSink)
    {
        m_descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        m_audioSink = audioSink ?? throw new ArgumentNullException(nameof(audioSink));
    }

    public SmsMemoryController MemoryController { get; private set; }

    public SmsPortDevice PortDevice { get; private set; }

    protected override SmsJoypad CreateInput() => new SmsJoypad();

    protected override Bus CreateBus()
    {
        m_vdp = new SmsVdp();
        MemoryController = new SmsMemoryController();
        m_psg = new SmsPsg(m_audioSink, (int)m_descriptor.CpuHz, m_descriptor.AudioSampleRateHz);
        PortDevice = new SmsPortDevice(m_vdp, Input, MemoryController, psg: m_psg);
        return new Bus(0x10000, PortDevice);
    }

    protected override Cpu CreateCpu(Bus bus)
    {
        var cpu = new Cpu(bus);
        cpu.Bus.Attach(new SmsRamMirrorDevice(cpu.MainMemory));
        cpu.Bus.Attach(MemoryController);
        return cpu;
    }

    protected override SmsVdp CreateVideo(Bus bus) => m_vdp;

    protected override SmsPsg CreateAudio(Bus bus) => m_psg;
}
