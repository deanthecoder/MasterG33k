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

using CSharp.Core;

namespace DTC.Z80.Debuggers;

/// <summary>
/// Detects tight CPU loops and logs diagnostic info once a threshold is hit.
/// </summary>
public sealed class CpuLoopDebugger : CpuDebuggerBase
{
    private readonly Func<string> m_extraInfo;
    private ushort m_prevPc;
    private ushort m_prevPrevPc;
    private bool m_hasPrev;
    private int m_samePcCount;
    private int m_toggleCount;
    private bool m_triggered;

    public bool IsEnabled { get; set; }

    public int SamePcThreshold { get; set; } = 2048;

    public int ToggleThreshold { get; set; } = 4096;

    public CpuLoopDebugger(Func<string> extraInfo = null)
    {
        m_extraInfo = extraInfo;
    }

    public override void AfterStep(Cpu cpu)
    {
        if (!IsEnabled)
            return;

        var pc = cpu.Reg.PC;
        if (!m_hasPrev)
        {
            m_prevPc = pc;
            m_hasPrev = true;
            return;
        }

        if (pc == m_prevPc)
            m_samePcCount++;
        else
            m_samePcCount = 0;

        if (m_prevPrevPc == pc && m_prevPc != pc)
            m_toggleCount++;
        else
            m_toggleCount = 0;

        if (!m_triggered && (m_samePcCount >= SamePcThreshold || m_toggleCount >= ToggleThreshold))
        {
            m_triggered = true;
            var reason = m_samePcCount >= SamePcThreshold ? "stuck PC" : "2-step loop";
            var extra = m_extraInfo?.Invoke();
            var message = $"[LoopDetector] {reason} @ PC={pc:X4} (samePc={m_samePcCount}, toggle={m_toggleCount}).";
            if (!string.IsNullOrWhiteSpace(extra))
                message = $"{message} {extra}";
            cpu.InstructionLogger.Write(() => message);
            Logger.Instance.Warn(message);
            cpu.InstructionLogger.DumpToConsole();
        }

        m_prevPrevPc = m_prevPc;
        m_prevPc = pc;
    }

    public void Reset()
    {
        m_prevPc = 0;
        m_prevPrevPc = 0;
        m_hasPrev = false;
        m_samePcCount = 0;
        m_toggleCount = 0;
        m_triggered = false;
    }
}
