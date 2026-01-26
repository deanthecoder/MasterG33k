// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation.Snapshot;
using DTC.Z80.Devices;

namespace DTC.Z80.Snapshot;

public static class SmsSnapshot
{
    private const int StateHeaderSize = sizeof(uint) + sizeof(ushort) + sizeof(ushort);

    public static int GetStateSize(Cpu cpu, Memory memory, SmsMemoryController memoryController, SmsPortDevice portDevice, SmsVdp vdp, SmsPsg psg)
    {
        if (cpu == null)
            throw new ArgumentNullException(nameof(cpu));
        if (memory == null)
            throw new ArgumentNullException(nameof(memory));
        if (memoryController == null)
            throw new ArgumentNullException(nameof(memoryController));
        if (portDevice == null)
            throw new ArgumentNullException(nameof(portDevice));
        if (vdp == null)
            throw new ArgumentNullException(nameof(vdp));
        if (psg == null)
            throw new ArgumentNullException(nameof(psg));

        var size = StateHeaderSize;
        size += cpu.GetStateSize();
        size += memory.GetStateSize();
        size += memoryController.GetStateSize();
        size += GetRomStateSize(memoryController.BiosRom);
        size += GetRomStateSize(memoryController.Cartridge);
        size += portDevice.GetStateSize();
        size += vdp.GetStateSize();
        size += psg.GetStateSize();
        return size;
    }

    public static void Save(MachineState state, Cpu cpu, Memory memory, SmsMemoryController memoryController, SmsPortDevice portDevice, SmsVdp vdp, SmsPsg psg)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        var expectedSize = GetStateSize(cpu, memory, memoryController, portDevice, vdp, psg);
        if (state.Size != expectedSize)
            throw new InvalidOperationException($"State buffer size mismatch. Expected {expectedSize} bytes.");

        var writer = state.CreateWriter();
        writer.WriteUInt32(MachineState.Magic);
        writer.WriteUInt16(MachineState.Version);
        writer.WriteUInt16(0);

        cpu.SaveState(ref writer);
        memory.SaveState(ref writer);
        memoryController.SaveState(ref writer);
        SaveRomState(ref writer, memoryController.BiosRom);
        SaveRomState(ref writer, memoryController.Cartridge);
        portDevice.SaveState(ref writer);
        vdp.SaveState(ref writer);
        psg.SaveState(ref writer);

        if (writer.Offset != state.Size)
            throw new InvalidOperationException($"State buffer write size mismatch. Wrote {writer.Offset} bytes, expected {state.Size}.");
    }

    public static void Load(MachineState state, Cpu cpu, Memory memory, SmsMemoryController memoryController, SmsPortDevice portDevice, SmsVdp vdp, SmsPsg psg)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        var expectedSize = GetStateSize(cpu, memory, memoryController, portDevice, vdp, psg);
        if (state.Size != expectedSize)
            throw new InvalidOperationException($"State buffer size mismatch. Expected {expectedSize} bytes.");

        var reader = state.CreateReader();
        var magic = reader.ReadUInt32();
        if (magic != MachineState.Magic)
            throw new InvalidOperationException("Invalid state buffer (bad magic).");

        var version = reader.ReadUInt16();
        if (version != MachineState.Version)
            throw new InvalidOperationException($"Unsupported state version {version}.");

        reader.ReadUInt16(); // reserved

        cpu.LoadState(ref reader);
        memory.LoadState(ref reader);
        memoryController.LoadState(ref reader);
        LoadRomState(ref reader, memoryController.BiosRom, "BIOS");
        LoadRomState(ref reader, memoryController.Cartridge, "cartridge");
        portDevice.LoadState(ref reader);
        vdp.LoadState(ref reader);
        psg.LoadState(ref reader);

        if (reader.Offset != state.Size)
            throw new InvalidOperationException("State buffer read size mismatch.");
    }

    private static int GetRomStateSize(SmsRomDevice rom) =>
        sizeof(byte) + (rom?.GetStateSize() ?? 0);

    private static void SaveRomState(ref StateWriter writer, SmsRomDevice rom)
    {
        var hasRom = rom != null;
        writer.WriteBool(hasRom);
        if (hasRom)
            rom.SaveState(ref writer);
    }

    private static void LoadRomState(ref StateReader reader, SmsRomDevice rom, string label)
    {
        var hasRom = reader.ReadBool();
        if (!hasRom)
            return;

        if (rom == null)
            throw new InvalidOperationException($"State expects {label} ROM, but none is loaded.");

        rom.LoadState(ref reader);
    }
}
