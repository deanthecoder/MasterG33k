// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.Z80;

/// <summary>
/// Minimal Z80 CPU scaffold. Instruction execution is intentionally stubbed.
/// </summary>
public sealed class Cpu
{
    public Registers Reg { get; }
    public Registers TheRegisters { get; }
    public InstructionLogger InstructionLogger { get; } = new();
    public Memory MainMemory { get; }

    public bool IsHalted { get; set; }
    // ReSharper disable InconsistentNaming
    public long TStates { get; private set; }
    public long TStatesSinceCpuStart => TStates;
    // ReSharper restore InconsistentNaming

    public Cpu(Memory memory)
    {
        MainMemory = memory ?? throw new ArgumentNullException(nameof(memory));
        TheRegisters = new Registers();
        Reg = TheRegisters;
        Bus = new Bus(MainMemory);
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
            InternalWaitM();
            return;
        }

        var pc = TheRegisters.PC;
        var opcode = FetchOpcode8();
        var instruction = Instructions.Instructions.Table[opcode];
        if (InstructionLogger.IsEnabled)
            InstructionLogger.Write(() => $"{pc:X4}: {opcode:X2} {instruction?.Mnemonic ?? "??"}");
        instruction?.Execute(this);
    }

    public byte Fetch8()
    {
        var value = Read8(TheRegisters.PC);
        TheRegisters.PC++;
        return value;
    }

    public byte FetchOpcode8()
    {
        TheRegisters.IncrementR();
        return Fetch8();
    }

    public ushort Fetch16() =>
        (ushort)(Fetch8() | (Fetch8() << 8));

    public byte Read8(ushort address)
    {
        var value = MainMemory.Read8(address);
        InternalWaitM();
        return value;
    }

    public void Write8(ushort address, byte value)
    {
        MainMemory.Write8(address, value);
        InternalWaitM();
    }

    public void Write16(ushort address, ushort value)
    {
        Write8(address, (byte)(value & 0xFF));
        Write8((ushort)(address + 1), (byte)(value >> 8));
    }

    public void InternalWaitM() =>
        TStates += 4;


    public void PushPC()
    {
        Write8((ushort)(Reg.SP - 1), (byte)(Reg.PC >> 8));
        Write8((ushort)(Reg.SP - 2), (byte)(Reg.PC & 0xFF));
        Reg.SP -= 2;
    }
}
