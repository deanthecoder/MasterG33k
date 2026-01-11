// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Z80.Devices;

namespace DTC.Z80;

/// <summary>
/// Minimal Z80 CPU scaffold. Instruction execution is intentionally stubbed.
/// </summary>
public sealed class Cpu
{
    private bool m_interruptPending;

    public Registers Reg { get; }
    public Registers TheRegisters { get; }
    public InstructionLogger InstructionLogger { get; } = new();
    public Memory MainMemory { get; }
    public Alu Alu { get; }

    public bool IsHalted { get; set; }
    // ReSharper disable InconsistentNaming
    public long TStates { get; private set; }
    public long TStatesSinceCpuStart => TStates;
    // ReSharper restore InconsistentNaming

    public Cpu(Bus bus)
    {
        Bus = bus ?? throw new ArgumentNullException(nameof(bus));
        MainMemory = Bus.MainMemory;
        TheRegisters = new Registers();
        Reg = TheRegisters;
        Alu = new Alu(TheRegisters);
    }

    public Bus Bus { get; }

    public void Reset()
    {
        TheRegisters.Clear();
        IsHalted = false;
        TStates = 0;
    }

    public void Step()
    {
        if (IsHalted)
        {
            TheRegisters.IncrementR();
            InternalWait(4);
            ServiceInterrupts();
            return;
        }

        var pc = TheRegisters.PC;
        var isLogging = InstructionLogger.IsEnabled;
        string preRegState = null;
        string preFlags = null;
        if (isLogging)
        {
            preRegState = Reg.ToString();
            preFlags = Reg.FlagsAsString();
        }

        var opcode = FetchOpcode8();
        var instruction = Instructions.Instructions.Table[opcode];
        if (isLogging)
        {
            var disassembly = Disassembler.GetInstructionWithOperands(Bus, pc);
            InstructionLogger.Write(() => $"{disassembly,-19}|{preRegState,-32}|{preFlags}");
        }
        instruction?.Execute(this);
        ServiceInterrupts();
    }

    public void RequestInterrupt() => m_interruptPending = true;

    private void ServiceInterrupts()
    {
        if (!m_interruptPending)
            return;

        if (!Reg.IFF1)
            return;

        m_interruptPending = false;
        Reg.IFF1 = false;
        Reg.IFF2 = false;
        IsHalted = false;

        PushPC();
        Reg.PC = Reg.IM switch
        {
            2 => (ushort)((Reg.I << 8) | 0xFF),
            _ => 0x0038
        };

        InternalWait(7);
    }

    public byte Fetch8()
    {
        var value = Read8(TheRegisters.PC);
        TheRegisters.PC++;
        return value;
    }

    public byte FetchOpcode8()
    {
        var value = Bus.Read8(TheRegisters.PC);
        TheRegisters.PC++;
        TheRegisters.IncrementR();
        InternalWait(4);
        return value;
    }

    public ushort Fetch16() =>
        (ushort)(Fetch8() | (Fetch8() << 8));

    public byte Read8(ushort address)
    {
        var value = Bus.Read8(address);
        InternalWait(3);
        return value;
    }

    public void Write8(ushort address, byte value)
    {
        Bus.Write8(address, value);
        InternalWait(3);
    }

    public void Write16(ushort address, ushort value)
    {
        Write8(address, (byte)(value & 0xFF));
        Write8((ushort)(address + 1), (byte)(value >> 8));
    }

    /// <summary>
    /// Adds extra internal CPU wait cycles (T-states) beyond the implicit opcode fetch.
    /// Known waits: opcode fetch is 4T, memory read/write is 3T per byte.
    /// </summary>
    public void InternalWait(int tStates) =>
        TStates += tStates;

    public void PushPC()
    {
        Write8((ushort)(Reg.SP - 1), (byte)(Reg.PC >> 8));
        Write8((ushort)(Reg.SP - 2), (byte)(Reg.PC & 0xFF));
        Reg.SP -= 2;
    }
}
