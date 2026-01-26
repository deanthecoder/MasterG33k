// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using System.Diagnostics;
using DTC.Emulation.Debuggers;

namespace DTC.Emulation;

/// <summary>
/// Base CPU type that provides bus access and debugger notifications.
/// </summary>
public abstract class CpuBase
{
    private readonly List<ICpuDebugger> m_debuggers = [];

    protected CpuBase(Bus bus)
    {
        Bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    public Bus Bus { get; }

    public IReadOnlyCollection<ICpuDebugger> Debuggers => m_debuggers.AsReadOnly();

    public void AddDebugger(ICpuDebugger debugger)
    {
        if (debugger == null)
            throw new ArgumentNullException(nameof(debugger));

        m_debuggers.Add(debugger);
    }

    public abstract void Reset();
    public abstract void Step();
    public abstract byte Read8(ushort address);
    public abstract void Write8(ushort address, byte value);

    [Conditional("DEBUG")]
    protected void NotifyBeforeInstruction(ushort opcodeAddress, byte opcode)
    {
        if (m_debuggers.Count == 0)
            return;

        foreach (var debugger in m_debuggers)
            debugger.BeforeInstruction(this, opcodeAddress, opcode);
    }

    [Conditional("DEBUG")]
    protected void NotifyAfterStep()
    {
        if (m_debuggers.Count == 0)
            return;

        foreach (var debugger in m_debuggers)
            debugger.AfterStep(this);
    }

    [Conditional("DEBUG")]
    protected void NotifyMemoryRead(ushort address, byte value)
    {
        if (m_debuggers.Count == 0)
            return;

        foreach (var debugger in m_debuggers)
            debugger.OnMemoryRead(this, address, value);
    }

    [Conditional("DEBUG")]
    protected void NotifyMemoryWrite(ushort address, byte value)
    {
        if (m_debuggers.Count == 0)
            return;

        foreach (var debugger in m_debuggers)
            debugger.OnMemoryWrite(this, address, value);
    }
}
