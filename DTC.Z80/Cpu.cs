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
using DTC.Z80.Debuggers;
using DTC.Z80.Devices;
using DTC.Z80.Snapshot;

namespace DTC.Z80;

/// <summary>
/// Minimal Z80 CPU scaffold. Instruction execution is intentionally stubbed.
/// </summary>
public sealed class Cpu
{
    private bool m_interruptPending;
    private int m_eiDelay;
    private readonly List<ICpuDebugger> m_debuggers = [];
    private bool m_nmiPending;

    public Registers Reg { get; }
    public Registers TheRegisters { get; }
    public InstructionLogger InstructionLogger { get; } = new();
    public Memory MainMemory { get; }
    public Alu Alu { get; }

    public bool IsHalted { get; set; }

    public IReadOnlyCollection<ICpuDebugger> Debuggers => m_debuggers.AsReadOnly();

    public ushort CurrentInstructionAddress { get; private set; }
    
    // ReSharper disable InconsistentNaming
    public long TStatesSinceCpuStart { get; private set; }

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
        TStatesSinceCpuStart = 0;
        m_eiDelay = 0;
    }

    public void AddDebugger(ICpuDebugger debugger)
    {
        if (debugger == null)
            throw new ArgumentNullException(nameof(debugger));

        m_debuggers.Add(debugger);
    }

    public void Step()
    {
        if (IsHalted)
        {
            TheRegisters.IncrementR();
            InternalWait(4);
            ServiceInterrupts();
            NotifyAfterStep();
            return;
        }

        var pc = TheRegisters.PC;
        CurrentInstructionAddress = pc;
        var isLogging = InstructionLogger.IsEnabled;
        string preRegState = null;
        string preFlags = null;
        if (isLogging)
        {
            preRegState = Reg.ToString();
            preFlags = Reg.FlagsAsString();
        }

        var opcode = FetchOpcode8();
        NotifyBeforeInstruction(pc, opcode);
        var instruction = Instructions.Instructions.Table[opcode];
        if (isLogging)
        {
            var disassembly = Disassembler.GetInstructionWithOperands(Bus, pc);
            InstructionLogger.Write(() => $"{disassembly,-19}|{preRegState,-32}|{preFlags}");
        }
        instruction?.Execute(this);
        ServiceInterrupts();
        NotifyAfterStep();
    }

    public void RequestInterrupt() => m_interruptPending = true;

    public void RequestNmi() => m_nmiPending = true;

    internal void ScheduleEi()
    {
        Reg.IFF1 = true;
        Reg.IFF2 = true;
        m_eiDelay = 1;
    }

    internal void CancelEi() => m_eiDelay = 0;

    private void ServiceInterrupts()
    {
        // NMI has priority over maskable interrupts
        if (m_nmiPending)
        {
            m_nmiPending = false;
            IsHalted = false;

            // On NMI, IFF1 is cleared; IFF2 retains the previous IFF1 state (already tracked)
            Reg.IFF1 = false;
            PushPC();
            Reg.PC = 0x0066;

            // Approximate timing and refresh increment on interrupt entry
            InternalWait(7);
            Reg.IncrementR();
            return;
        }

        if (m_eiDelay > 0)
        {
            m_eiDelay--;
            return;
        }

        if (!m_interruptPending)
            return;

        if (!Reg.IFF1)
            return;

        m_interruptPending = false;
        if (IsHalted)
        {
            IsHalted = false;
            Reg.PC++;
        }
        Reg.IFF1 = false;
        Reg.IFF2 = false;

        PushPC();
        ushort target;
        if (Reg.IM == 2)
        {
            // In IM2, fetch 16-bit target vector from I:FF (VDP drives 0xFF)
            var vectorAddr = (ushort)((Reg.I << 8) | 0xFF);
            target = Bus.Read16(vectorAddr);

            // Two memory reads (approximate timing)
            InternalWait(6);
        }
        else
        {
            // IM0/IM1 default to RST 38h
            target = 0x0038;
        }

        Reg.PC = target;
        InternalWait(7);
        Reg.IncrementR();
    }

    public byte Fetch8()
    {
        var value = Read8(TheRegisters.PC);
        TheRegisters.PC++;
        return value;
    }

    public byte FetchOpcode8()
    {
        var address = TheRegisters.PC;
        var value = Bus.Read8(address);
        TheRegisters.PC++;
        TheRegisters.IncrementR();
        InternalWait(4);
        NotifyMemoryRead(address, value);
        return value;
    }

    public ushort Fetch16() =>
        (ushort)(Fetch8() | (Fetch8() << 8));

    public byte Read8(ushort address)
    {
        var value = Bus.Read8(address);
        InternalWait(3);
        NotifyMemoryRead(address, value);
        return value;
    }

    public void Write8(ushort address, byte value)
    {
        Bus.Write8(address, value);
        InternalWait(3);
        NotifyMemoryWrite(address, value);
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
        TStatesSinceCpuStart += tStates;

    public void PushPC()
    {
        Write8((ushort)(Reg.SP - 1), (byte)(Reg.PC >> 8));
        Write8((ushort)(Reg.SP - 2), (byte)(Reg.PC & 0xFF));
        Reg.SP -= 2;
    }

    [Conditional("DEBUG")]
    private void NotifyBeforeInstruction(ushort opcodeAddress, byte opcode)
    {
        if (m_debuggers.Count == 0)
            return;

        foreach (var debugger in m_debuggers)
            debugger.BeforeInstruction(this, opcodeAddress, opcode);
    }

    [Conditional("DEBUG")]
    private void NotifyAfterStep()
    {
        if (m_debuggers.Count == 0)
            return;

        foreach (var debugger in m_debuggers)
            debugger.AfterStep(this);
    }

    [Conditional("DEBUG")]
    private void NotifyMemoryRead(ushort address, byte value)
    {
        if (m_debuggers.Count == 0)
            return;

        foreach (var debugger in m_debuggers)
            debugger.OnMemoryRead(this, address, value);
    }

    [Conditional("DEBUG")]
    private void NotifyMemoryWrite(ushort address, byte value)
    {
        if (m_debuggers.Count == 0)
            return;

        foreach (var debugger in m_debuggers)
            debugger.OnMemoryWrite(this, address, value);
    }

    internal int GetStateSize() =>
        sizeof(byte) * 16 + // Main + alt register sets.
        sizeof(ushort) * 4 + // IX, IY, SP, PC.
        sizeof(byte) * 2 + // I, R.
        sizeof(byte) * 5 + // IFF1, IFF2, IsHalted, interrupt pending, NMI pending.
        sizeof(byte) + // IM.
        sizeof(int) + // EI delay.
        sizeof(long) + // CPU ticks.
        sizeof(ushort); // CurrentInstructionAddress.

    internal void SaveState(ref StateWriter writer)
    {
        ref var main = ref Reg.Main;
        ref var alt = ref Reg.Alt;
        writer.WriteByte(main.A);
        writer.WriteByte(main.F);
        writer.WriteByte(main.B);
        writer.WriteByte(main.C);
        writer.WriteByte(main.D);
        writer.WriteByte(main.E);
        writer.WriteByte(main.H);
        writer.WriteByte(main.L);

        writer.WriteByte(alt.A);
        writer.WriteByte(alt.F);
        writer.WriteByte(alt.B);
        writer.WriteByte(alt.C);
        writer.WriteByte(alt.D);
        writer.WriteByte(alt.E);
        writer.WriteByte(alt.H);
        writer.WriteByte(alt.L);

        writer.WriteUInt16(Reg.IX);
        writer.WriteUInt16(Reg.IY);
        writer.WriteUInt16(Reg.SP);
        writer.WriteUInt16(Reg.PC);
        writer.WriteByte(Reg.I);
        writer.WriteByte(Reg.R);
        writer.WriteBool(Reg.IFF1);
        writer.WriteBool(Reg.IFF2);
        writer.WriteByte(Reg.IM);

        writer.WriteBool(IsHalted);
        writer.WriteBool(m_interruptPending);
        writer.WriteBool(m_nmiPending);
        writer.WriteInt32(m_eiDelay);
        writer.WriteInt64(TStatesSinceCpuStart);
        writer.WriteUInt16(CurrentInstructionAddress);
    }

    internal void LoadState(ref StateReader reader)
    {
        ref var main = ref Reg.Main;
        ref var alt = ref Reg.Alt;
        main.A = reader.ReadByte();
        main.F = reader.ReadByte();
        main.B = reader.ReadByte();
        main.C = reader.ReadByte();
        main.D = reader.ReadByte();
        main.E = reader.ReadByte();
        main.H = reader.ReadByte();
        main.L = reader.ReadByte();

        alt.A = reader.ReadByte();
        alt.F = reader.ReadByte();
        alt.B = reader.ReadByte();
        alt.C = reader.ReadByte();
        alt.D = reader.ReadByte();
        alt.E = reader.ReadByte();
        alt.H = reader.ReadByte();
        alt.L = reader.ReadByte();

        Reg.IX = reader.ReadUInt16();
        Reg.IY = reader.ReadUInt16();
        Reg.SP = reader.ReadUInt16();
        Reg.PC = reader.ReadUInt16();
        Reg.I = reader.ReadByte();
        Reg.R = reader.ReadByte();
        Reg.IFF1 = reader.ReadBool();
        Reg.IFF2 = reader.ReadBool();
        Reg.IM = reader.ReadByte();

        IsHalted = reader.ReadBool();
        m_interruptPending = reader.ReadBool();
        m_nmiPending = reader.ReadBool();
        m_eiDelay = reader.ReadInt32();
        TStatesSinceCpuStart = reader.ReadInt64();
        CurrentInstructionAddress = reader.ReadUInt16();
    }
}
