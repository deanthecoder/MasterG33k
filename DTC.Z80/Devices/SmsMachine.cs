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
using DTC.Emulation.Snapshot;
using DTC.Z80.HostDevices;
using DTC.Z80.Snapshot;

namespace DTC.Z80.Devices;

/// <summary>
/// Wires together SMS-specific devices into a single emulated machine.
/// </summary>
public sealed class SmsMachine : IMachine, IMachineSnapshotter
{
    private readonly SmsMemoryController m_memoryController;
    private readonly SmsPortDevice m_portDevice;
    private readonly SmsJoypad m_joypad;
    private readonly SmsVdp m_vdp;
    private readonly SmsPsg m_psg;
    private readonly Cpu m_cpu;

    public SmsMachine(IMachineDescriptor descriptor, SoundDevice audioSink)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        if (audioSink == null)
            throw new ArgumentNullException(nameof(audioSink));

        m_vdp = new SmsVdp();
        m_joypad = new SmsJoypad();
        m_memoryController = new SmsMemoryController();
        m_psg = new SmsPsg(audioSink, (int)Descriptor.CpuHz, Descriptor.AudioSampleRateHz);
        m_portDevice = new SmsPortDevice(m_vdp, m_joypad, m_memoryController, psg: m_psg);
        m_cpu = new Cpu(new Bus(new Memory(), m_portDevice));
        m_cpu.Bus.Attach(new SmsRamMirrorDevice(m_cpu.MainMemory));
        m_cpu.Bus.Attach(m_memoryController);
    }

    public IMachineDescriptor Descriptor { get; private set; }

    public string Name => Descriptor?.Name ?? "MasterG33k";

    public long CpuTicks => m_cpu.TStatesSinceCpuStart;

    public bool HasLoadedCartridge => m_memoryController.Cartridge != null;

    public IVideoSource Video => m_vdp;

    public IAudioSource Audio => m_psg;

    public IMachineSnapshotter Snapshotter => this;

    public SmsVdp Vdp => m_vdp;

    public SmsPsg Psg => m_psg;

    public SmsJoypad Joypad => m_joypad;

    public SmsMemoryController MemoryController => m_memoryController;

    public Cpu Cpu => m_cpu;

    public void UpdateDescriptor(IMachineDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        m_psg.SetCpuClockHz((int)Descriptor.CpuHz);
    }

    public void Reset()
    {
        m_cpu.Reset();
        m_vdp.Reset();
        m_memoryController.Reset();
        m_psg.Reset();
    }

    public void LoadRom(byte[] romData, string romName)
    {
        if (romData == null || romData.Length == 0)
            throw new ArgumentException("ROM data is empty.", nameof(romData));

        var romDevice = new SmsRomDevice(romData);
        var mapper = new SmsMapperDevice(m_memoryController, m_cpu.MainMemory);
        m_cpu.Bus.Attach(mapper);
        m_memoryController.SetBios(null);
        m_memoryController.SetCartridge(romDevice, forceEnabled: false);
        InitializePostRomState(romDevice);
    }

    public void StepCpu() => m_cpu.Step();

    public void AdvanceDevices(long deltaTicks)
    {
        if (deltaTicks <= 0)
            return;
        m_vdp.AdvanceCycles(deltaTicks);
        m_psg.AdvanceT(deltaTicks);
    }

    public bool TryConsumeInterrupt() => m_vdp.TryConsumeInterrupt();

    public void RequestInterrupt() => m_cpu.RequestInterrupt();

    public void SetInputActive(bool isActive) =>
        m_joypad.SetInputEnabled(isActive);

    public void SetVideoStandard(bool isPal)
    {
        m_vdp.SetIsPal(isPal);
        m_psg.SetCpuClockHz((int)Descriptor.CpuHz);
    }

    public void SetLayerVisibility(bool isBackgroundVisible, bool areSpritesVisible)
    {
        m_vdp.IsBackgroundVisible = isBackgroundVisible;
        m_vdp.AreSpritesVisible = areSpritesVisible;
    }

    private void InitializePostRomState(SmsRomDevice romDevice)
    {
        if (romDevice == null)
            throw new ArgumentNullException(nameof(romDevice));

        Array.Clear(m_cpu.MainMemory.Data, 0, m_cpu.MainMemory.Data.Length);
        m_cpu.Reset();
        m_vdp.ApplyPostBiosState();
        m_memoryController.Reset();
        m_psg.Reset();

        // Post-BIOS slot state: BIOS disabled, cartridge/RAM/IO enabled, expansion/card disabled.
        m_memoryController.WriteControl(0xA8);

        // BIOS stores the last port $3E value at $C000; some titles read it back on boot.
        m_cpu.MainMemory.Data[0xC000] = 0xA8;
        m_cpu.Bus.Write8(0xFFFC, romDevice.Control);
        m_cpu.Bus.Write8(0xFFFD, romDevice.Bank0);
        m_cpu.Bus.Write8(0xFFFE, romDevice.Bank1);
        m_cpu.Bus.Write8(0xFFFF, romDevice.Bank2);

        m_cpu.Reg.PC = 0x0000;
        m_cpu.Reg.SP = 0xDFF0;
        m_cpu.Reg.AF = 0x0000;
        m_cpu.Reg.BC = 0x0000;
        m_cpu.Reg.DE = 0x0000;
        m_cpu.Reg.HL = 0x0000;
        m_cpu.Reg.IX = 0x0000;
        m_cpu.Reg.IY = 0x0000;
        m_cpu.Reg.IM = 1;
        m_cpu.Reg.IFF1 = false;
        m_cpu.Reg.IFF2 = false;
    }

    int IMachineSnapshotter.GetStateSize() =>
        SmsSnapshot.GetStateSize(m_cpu, m_cpu.MainMemory, m_memoryController, m_portDevice, m_vdp, m_psg);

    void IMachineSnapshotter.Save(MachineState state, Span<byte> frameBuffer)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        SmsSnapshot.Save(state, m_cpu, m_cpu.MainMemory, m_memoryController, m_portDevice, m_vdp, m_psg);
        m_vdp.CopyFrameBuffer(frameBuffer);
    }

    void IMachineSnapshotter.Load(MachineState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        SmsSnapshot.Load(state, m_cpu, m_cpu.MainMemory, m_memoryController, m_portDevice, m_vdp, m_psg);
    }
}
