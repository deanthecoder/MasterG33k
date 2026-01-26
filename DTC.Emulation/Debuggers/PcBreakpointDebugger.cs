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

namespace DTC.Emulation.Debuggers;

/// <summary>
/// Debugger that triggers a callback when the CPU reaches a specific opcode address.
/// </summary>
public sealed class PcBreakpointDebugger : CpuDebuggerBase
{
    private readonly ushort m_breakAddress;
    private readonly Action m_action;

    public PcBreakpointDebugger(ushort breakAddress, Action action)
    {
        m_breakAddress = breakAddress;
        m_action = action;
    }

    public override void BeforeInstruction(CpuBase cpu, ushort opcodeAddress, byte opcode)
    {
        if (opcodeAddress != m_breakAddress)
            return;

        m_action?.Invoke();
    }
}
