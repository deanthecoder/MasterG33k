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
/// Debugger that triggers a callback when a specific memory address is read.
/// </summary>
public sealed class MemoryReadDebugger : CpuDebuggerBase
{
    private readonly ushort m_targetAddress;
    private readonly byte? m_targetValue;
    private readonly Action<byte> m_onRead;

    public MemoryReadDebugger(ushort targetAddress, byte targetValue, Action<byte> onRead)
    {
        m_targetAddress = targetAddress;
        m_targetValue = targetValue;
        m_onRead = onRead;
    }

    public MemoryReadDebugger(ushort targetAddress, Action<byte> onRead)
    {
        m_targetAddress = targetAddress;
        m_targetValue = null;
        m_onRead = onRead;
    }

    public override void OnMemoryRead(CpuBase cpu, ushort address, byte value)
    {
        if (address != m_targetAddress)
            return;

        if (m_targetValue.HasValue && value != m_targetValue.Value)
            return;

        m_onRead?.Invoke(value);
    }
}
