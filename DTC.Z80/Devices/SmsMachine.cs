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
using DTC.Emulation.Snapshot;
using DTC.Z80.Snapshot;

namespace DTC.Z80.Devices;

/// <summary>
/// Wires together SMS-specific devices into a single emulated machine.
/// </summary>
public sealed class SmsMachine : IMachine, IMachineSnapshotter
{
    private readonly SmsPortDevice m_portDevice;

    public SmsMachine(IMachineDescriptor descriptor, SoundDevice audioSink)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        if (audioSink == null)
            throw new ArgumentNullException(nameof(audioSink));

        Vdp = new SmsVdp();
        Joypad = new SmsJoypad();
        MemoryController = new SmsMemoryController();
        Psg = new SmsPsg(audioSink, (int)Descriptor.CpuHz, Descriptor.AudioSampleRateHz);
        m_portDevice = new SmsPortDevice(Vdp, Joypad, MemoryController, psg: Psg);
        Cpu = new Cpu(new Bus(0x10000, m_portDevice));
        Cpu.Bus.Attach(new SmsRamMirrorDevice(Cpu.MainMemory));
        Cpu.Bus.Attach(MemoryController);
    }

    public IMachineDescriptor Descriptor { get; private set; }

    public string Name => Descriptor?.Name ?? "MasterG33k";

    public long CpuTicks => Cpu.TStatesSinceCpuStart;

    public bool HasLoadedCartridge => MemoryController.Cartridge != null;

    public IVideoSource Video => Vdp;

    public IAudioSource Audio => Psg;

    public IMachineSnapshotter Snapshotter => this;

    public SmsVdp Vdp { get; }

    public SmsPsg Psg { get; }

    public SmsJoypad Joypad { get; }

    public SmsMemoryController MemoryController { get; }

    public Cpu Cpu { get; }

    public void UpdateDescriptor(IMachineDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Psg.SetCpuClockHz((int)Descriptor.CpuHz);
    }

    public void Reset()
    {
        Cpu.Reset();
        Vdp.Reset();
        MemoryController.Reset();
        Psg.Reset();
    }

    public void LoadRom(byte[] romData, string romName)
    {
        if (romData == null || romData.Length == 0)
            throw new ArgumentException("ROM data is empty.", nameof(romData));

        var romDevice = new SmsRomDevice(romData);
        var mapper = new SmsMapperDevice(MemoryController, Cpu.MainMemory);
        Cpu.Bus.Attach(mapper);
        MemoryController.SetBios(null);
        MemoryController.SetCartridge(romDevice, forceEnabled: false);
        InitializePostRomState(romDevice);
    }

    public void StepCpu() => Cpu.Step();

    public void AdvanceDevices(long deltaTicks)
    {
        if (deltaTicks <= 0)
            return;
        Vdp.AdvanceCycles(deltaTicks);
        Psg.AdvanceT(deltaTicks);
    }

    public bool TryConsumeInterrupt() => Vdp.TryConsumeInterrupt();

    public void RequestInterrupt() => Cpu.RequestInterrupt();

    public void SetInputActive(bool isActive) =>
        Joypad.SetInputEnabled(isActive);

    public void SetVideoStandard(bool isPal)
    {
        Vdp.SetIsPal(isPal);
        Psg.SetCpuClockHz((int)Descriptor.CpuHz);
    }

    public void SetLayerVisibility(bool isBackgroundVisible, bool areSpritesVisible)
    {
        Vdp.IsBackgroundVisible = isBackgroundVisible;
        Vdp.AreSpritesVisible = areSpritesVisible;
    }

    private void InitializePostRomState(SmsRomDevice romDevice)
    {
        if (romDevice == null)
            throw new ArgumentNullException(nameof(romDevice));

        Array.Clear(Cpu.MainMemory.Data, 0, Cpu.MainMemory.Data.Length);
        Cpu.Reset();
        Vdp.ApplyPostBiosState();
        MemoryController.Reset();
        Psg.Reset();

        // Post-BIOS slot state: BIOS disabled, cartridge/RAM/IO enabled, expansion/card disabled.
        MemoryController.WriteControl(0xA8);

        // BIOS stores the last port $3E value at $C000; some titles read it back on boot.
        Cpu.MainMemory.Data[0xC000] = 0xA8;
        Cpu.Bus.Write8(0xFFFC, romDevice.Control);
        Cpu.Bus.Write8(0xFFFD, romDevice.Bank0);
        Cpu.Bus.Write8(0xFFFE, romDevice.Bank1);
        Cpu.Bus.Write8(0xFFFF, romDevice.Bank2);

        Cpu.Reg.PC = 0x0000;
        Cpu.Reg.SP = 0xDFF0;
        Cpu.Reg.AF = 0x0000;
        Cpu.Reg.BC = 0x0000;
        Cpu.Reg.DE = 0x0000;
        Cpu.Reg.HL = 0x0000;
        Cpu.Reg.IX = 0x0000;
        Cpu.Reg.IY = 0x0000;
        Cpu.Reg.IM = 1;
        Cpu.Reg.IFF1 = false;
        Cpu.Reg.IFF2 = false;
    }

    int IMachineSnapshotter.GetStateSize() =>
        SmsSnapshot.GetStateSize(Cpu, Cpu.MainMemory, MemoryController, m_portDevice, Vdp, Psg);

    void IMachineSnapshotter.Save(MachineState state, Span<byte> frameBuffer)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        SmsSnapshot.Save(state, Cpu, Cpu.MainMemory, MemoryController, m_portDevice, Vdp, Psg);
        Vdp.CopyFrameBuffer(frameBuffer);
    }

    void IMachineSnapshotter.Load(MachineState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        SmsSnapshot.Load(state, Cpu, Cpu.MainMemory, MemoryController, m_portDevice, Vdp, Psg);
    }
}
