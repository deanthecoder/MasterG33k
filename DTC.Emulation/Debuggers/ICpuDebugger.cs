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
/// Receives CPU debugging callbacks before and after instructions.
/// </summary>
public interface ICpuDebugger
{
    void BeforeInstruction(CpuBase cpu, ushort opcodeAddress, byte opcode);
    void AfterStep(CpuBase cpu);
    void OnMemoryRead(CpuBase cpu, ushort address, byte value);
    void OnMemoryWrite(CpuBase cpu, ushort address, byte value);
}
