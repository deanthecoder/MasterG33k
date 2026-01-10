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
    public Registers Reg { get; } = new Registers();
    public Bus Bus { get; }

    public bool IsHalted { get; private set; }
    public long TStates { get; private set; }

    public Cpu(Bus bus)
    {
        Bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    public void Reset()
    {
        Reg.Clear();
        IsHalted = false;
        TStates = 0;
    }

    public void Step()
    {
        if (IsHalted)
        {
            TStates += 4;
            return;
        }

        var opcode = Fetch8();
        Execute(opcode);
    }

    private byte Fetch8()
    {
        var value = Bus.Read8(Reg.PC);
        Reg.PC++;
        return value;
    }

    private void Execute(byte opcode)
    {
        // Stub for future Z80 instruction decode/execute.
        // For now, treat any opcode as a NOP.
        TStates += 4;
    }
}
