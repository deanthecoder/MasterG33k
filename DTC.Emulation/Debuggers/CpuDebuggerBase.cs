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
/// Base class for CPU debuggers with no-op implementations.
/// </summary>
public abstract class CpuDebuggerBase : ICpuDebugger
{
    public virtual void BeforeInstruction(CpuBase cpu, ushort opcodeAddress, byte opcode)
    {
    }

    public virtual void AfterStep(CpuBase cpu)
    {
    }

    public virtual void OnMemoryRead(CpuBase cpu, ushort address, byte value)
    {
    }

    public virtual void OnMemoryWrite(CpuBase cpu, ushort address, byte value)
    {
    }
}
