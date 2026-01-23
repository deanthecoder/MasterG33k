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
using DTC.Core;

namespace DTC.Z80.Debuggers;

public sealed class IncrementingCounterDebugger : CpuDebuggerBase
{
    private readonly Dictionary<ushort, CandidateState> m_states = new();
    private readonly HashSet<ushort> m_candidates = [];
    private readonly List<(ushort From, ushort To)> m_ranges =
    [
        (0xC000, 0xDFFF), // System RAM.
        (0xE000, 0xFFFB)  // RAM mirror (exclude mapper registers at 0xFFFC-0xFFFF).
    ];
    private readonly byte m_startValue;
    private readonly byte m_targetValue;
    private int m_lastCandidateCount = -1;

    private struct CandidateState
    {
        // ReSharper disable InconsistentNaming
        public byte LastValue;
        public bool IsArmed;
        public bool HasHitTarget;
        // ReSharper restore InconsistentNaming
    }

    private bool m_isInitialized;
    private bool m_reportedEmpty;
    private bool m_reportedSingleCandidate;
    private ushort m_singleCandidate;

    public IncrementingCounterDebugger(byte startValue, byte targetValue)
    {
        m_startValue = startValue;
        m_targetValue = targetValue;
    }

    public override void AfterStep(Cpu cpu)
    {
        if (!m_isInitialized)
        {
            Initialize(cpu);
            return;
        }

        if (m_candidates.Count == 0)
        {
            if (m_reportedEmpty || HasPendingCandidatesWaitingToArm())
                return;
            const string message = "[Counter Detector] No incrementing candidates remain.";
            cpu.InstructionLogger.Write(() => message);
            Logger.Instance.Info(message);
            m_reportedEmpty = true;
            return;
        }

        ScanArmedCandidates(cpu);

        if (!m_reportedSingleCandidate && m_candidates.Count == 1)
            AnnounceSingleCandidate(cpu);
    }

    public override void OnMemoryWrite(Cpu cpu, ushort address, byte value)
    {
        if (!m_isInitialized)
            return;
        if (!m_states.TryGetValue(address, out var state))
            return;

        if (!state.IsArmed)
        {
            state.LastValue = value;
            if (value == m_startValue)
            {
                state.IsArmed = true;
                m_candidates.Add(address);
                m_reportedEmpty = false;
                if (!m_reportedSingleCandidate && m_candidates.Count == 1)
                    AnnounceSingleCandidate(cpu);
            }
            m_states[address] = state;
            return;
        }

        var previous = state.LastValue;
        if (value == previous)
            return;

        var delta = (byte)(value - previous);
        if (delta != 1)
        {
            RemoveCandidate(address);
            return;
        }

        ProcessValueChange(cpu, address, previous, value, ref state);
        m_states[address] = state;
    }

    private void Initialize(Cpu cpu)
    {
        foreach (var (from, to) in m_ranges)
        {
            if (to < from)
                continue;

            var address = from;
            while (true)
            {
                var value = cpu.Bus.Read8(address);
                var isArmed = value == m_startValue;
                m_states[address] = new CandidateState
                {
                    LastValue = value,
                    IsArmed = isArmed,
                    HasHitTarget = false
                };
                if (isArmed)
                    m_candidates.Add(address);

                if (address == to)
                    break;
                address++;
            }
        }

        m_isInitialized = true;
    }

    private void RemoveCandidate(ushort address)
    {
        m_candidates.Remove(address);
        m_states.Remove(address);

        if (m_singleCandidate != address)
            return;
        m_reportedSingleCandidate = false;
        m_singleCandidate = 0;
    }

    private void ScanArmedCandidates(Cpu cpu)
    {
        var bus = cpu.Bus;
        foreach (var candidate in m_candidates.ToArray())
        {
            if (!m_states.TryGetValue(candidate, out var state))
                continue;

            var current = bus.Read8(candidate);
            if (current == state.LastValue)
                continue;

            var previous = state.LastValue;
            var delta = (byte)(current - previous);
            if (delta != 1)
            {
                RemoveCandidate(candidate);
                continue;
            }

            ProcessValueChange(cpu, candidate, previous, current, ref state);
            m_states[candidate] = state;
        }
    }

    private void AnnounceSingleCandidate(Cpu cpu)
    {
        foreach (var candidate in m_candidates)
        {
            m_singleCandidate = candidate;
            m_reportedSingleCandidate = true;
            var message = $"[Counter Detector] Single candidate remains at {candidate:X4} (value {m_states[candidate].LastValue:X2}).";
            cpu.InstructionLogger.Write(() => message);
            Logger.Instance.Info(message);
            break;
        }
    }

    private bool HasPendingCandidatesWaitingToArm()
    {
        foreach (var state in m_states.Values)
        {
            if (!state.IsArmed)
                return true;
        }

        return false;
    }

    private void ProcessValueChange(Cpu cpu, ushort address, byte previous, byte current, ref CandidateState state)
    {
        state.LastValue = current;
        var message = $"[Counter Detector] {address:X4} incremented from {previous:X2} to {current:X2}.";
        cpu.InstructionLogger.Write(() => message);
        Logger.Instance.Info(message);
        LogCandidateCountChange(cpu);

        if (state.HasHitTarget || current != m_targetValue)
            return;
        state.HasHitTarget = true;
        var targetMessage = $"[Counter Detector] {address:X4} reached target {m_targetValue:X2}.";
        cpu.InstructionLogger.Write(() => targetMessage);
        Logger.Instance.Info(targetMessage);
    }

    private void LogCandidateCountChange(Cpu cpu)
    {
        var count = m_candidates.Count;
        if (count == m_lastCandidateCount)
            return;

        m_lastCandidateCount = count;
        var message = $"[Counter Detector] Candidate count now {count} (start {m_startValue:X2}, target {m_targetValue:X2}).";
        cpu.InstructionLogger.Write(() => message);
        Logger.Instance.Info(message);
    }
}
