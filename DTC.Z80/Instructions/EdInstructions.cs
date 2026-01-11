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

using CSharp.Core.Extensions;

namespace DTC.Z80.Instructions;

public static class EdInstructions
{
    public static readonly Instruction[] Table = Build();

    private static Instruction[] Build()
    {
        var table = new Instruction[256];
        for (var i = 0; i < table.Length; i++)
            table[i] = new Instruction("NOP", static _ => { });

        table[0x40] = new Instruction("IN B,(C)", static cpu => { DoIn(cpu, ref cpu.Reg.B); });
        table[0x41] = new Instruction("OUT (C),B", static cpu => { DoOut(cpu, cpu.Reg.B); });
        table[0x42] = new Instruction("SBC HL,BC", static cpu => { DoSbcHl(cpu, cpu.Reg.BC); });
        table[0x43] = new Instruction("LD (nn),BC", static cpu => { StoreRegisterPair(cpu, cpu.Reg.BC); });
        table[0x44] = new Instruction("NEG", static cpu => { cpu.Reg.A = cpu.Alu.SubtractAndSetFlags((byte)0, cpu.Reg.A, subCf: false); });
        table[0x45] = new Instruction("RETN", static cpu => { DoReti(cpu); });
        table[0x46] = new Instruction("IM 0", static cpu => { cpu.Reg.IM = 0; });
        table[0x47] = new Instruction("LD I,A", static cpu => { cpu.Reg.I = cpu.Reg.A; cpu.InternalWait(1); });
        table[0x48] = new Instruction("IN C,(C)", static cpu => { DoIn(cpu, ref cpu.Reg.C); });
        table[0x49] = new Instruction("OUT (C),C", static cpu => { DoOut(cpu, cpu.Reg.C); });
        table[0x4A] = new Instruction("ADC HL,BC", static cpu => { DoAdcHl(cpu, cpu.Reg.BC); });
        table[0x4B] = new Instruction("LD BC,(nn)", static cpu => { cpu.Reg.BC = LoadRegisterPair(cpu); });
        table[0x4C] = new Instruction("NEG", static cpu => { cpu.Reg.A = cpu.Alu.SubtractAndSetFlags((byte)0, cpu.Reg.A, subCf: false); });
        table[0x4D] = new Instruction("RETI", static cpu => { DoReti(cpu); });
        table[0x4E] = new Instruction("IM 0", static cpu => { cpu.Reg.IM = 0; });
        table[0x4F] = new Instruction("LD R,A", static cpu => { cpu.Reg.R = cpu.Reg.A; cpu.InternalWait(1); });

        table[0x50] = new Instruction("IN D,(C)", static cpu => { DoIn(cpu, ref cpu.Reg.D); });
        table[0x51] = new Instruction("OUT (C),D", static cpu => { DoOut(cpu, cpu.Reg.D); });
        table[0x52] = new Instruction("SBC HL,DE", static cpu => { DoSbcHl(cpu, cpu.Reg.DE); });
        table[0x53] = new Instruction("LD (nn),DE", static cpu => { StoreRegisterPair(cpu, cpu.Reg.DE); });
        table[0x54] = new Instruction("NEG", static cpu => { cpu.Reg.A = cpu.Alu.SubtractAndSetFlags((byte)0, cpu.Reg.A, subCf: false); });
        table[0x55] = new Instruction("RETN", static cpu => { DoReti(cpu); });
        table[0x56] = new Instruction("IM 1", static cpu => { cpu.Reg.IM = 1; });
        table[0x57] = new Instruction("LD A,I", static cpu => { LoadAFromInterruptRegister(cpu, cpu.Reg.I); });
        table[0x58] = new Instruction("IN E,(C)", static cpu => { DoIn(cpu, ref cpu.Reg.E); });
        table[0x59] = new Instruction("OUT (C),E", static cpu => { DoOut(cpu, cpu.Reg.E); });
        table[0x5A] = new Instruction("ADC HL,DE", static cpu => { DoAdcHl(cpu, cpu.Reg.DE); });
        table[0x5B] = new Instruction("LD DE,(nn)", static cpu => { cpu.Reg.DE = LoadRegisterPair(cpu); });
        table[0x5C] = new Instruction("NEG", static cpu => { cpu.Reg.A = cpu.Alu.SubtractAndSetFlags((byte)0, cpu.Reg.A, subCf: false); });
        table[0x5D] = new Instruction("RETN", static cpu => { DoReti(cpu); });
        table[0x5E] = new Instruction("IM 2", static cpu => { cpu.Reg.IM = 2; });
        table[0x5F] = new Instruction("LD A,R", static cpu => { LoadAFromInterruptRegister(cpu, cpu.Reg.R); });

        table[0x60] = new Instruction("IN H,(C)", static cpu => { DoIn(cpu, ref cpu.Reg.H); });
        table[0x61] = new Instruction("OUT (C),H", static cpu => { DoOut(cpu, cpu.Reg.H); });
        table[0x62] = new Instruction("SBC HL,HL", static cpu => { DoSbcHl(cpu, cpu.Reg.HL); });
        table[0x63] = new Instruction("LD (nn),HL", static cpu => { StoreRegisterPair(cpu, cpu.Reg.HL); });
        table[0x64] = new Instruction("NEG", static cpu => { cpu.Reg.A = cpu.Alu.SubtractAndSetFlags((byte)0, cpu.Reg.A, subCf: false); });
        table[0x65] = new Instruction("RETN", static cpu => { DoReti(cpu); });
        table[0x66] = new Instruction("IM 0", static cpu => { cpu.Reg.IM = 0; });
        table[0x67] = new Instruction("RRD", static cpu => { DoRrd(cpu); });
        table[0x68] = new Instruction("IN L,(C)", static cpu => { DoIn(cpu, ref cpu.Reg.L); });
        table[0x69] = new Instruction("OUT (C),L", static cpu => { DoOut(cpu, cpu.Reg.L); });
        table[0x6A] = new Instruction("ADC HL,HL", static cpu => { DoAdcHl(cpu, cpu.Reg.HL); });
        table[0x6B] = new Instruction("LD HL,(nn)", static cpu => { cpu.Reg.HL = LoadRegisterPair(cpu); });
        table[0x6C] = new Instruction("NEG", static cpu => { cpu.Reg.A = cpu.Alu.SubtractAndSetFlags((byte)0, cpu.Reg.A, subCf: false); });
        table[0x6D] = new Instruction("RETN", static cpu => { DoReti(cpu); });
        table[0x6E] = new Instruction("IM 0", static cpu => { cpu.Reg.IM = 0; });
        table[0x6F] = new Instruction("RLD", static cpu => { DoRld(cpu); });

        table[0x70] = new Instruction("IN (C)", static cpu => { DoInDiscard(cpu); });
        table[0x71] = new Instruction("OUT (C),0", static cpu => { DoOut(cpu, 0); });
        table[0x72] = new Instruction("SBC HL,SP", static cpu => { DoSbcHl(cpu, cpu.Reg.SP); });
        table[0x73] = new Instruction("LD (nn),SP", static cpu => { StoreRegisterPair(cpu, cpu.Reg.SP); });
        table[0x74] = new Instruction("NEG", static cpu => { cpu.Reg.A = cpu.Alu.SubtractAndSetFlags((byte)0, cpu.Reg.A, subCf: false); });
        table[0x75] = new Instruction("RETN", static cpu => { DoReti(cpu); });
        table[0x76] = new Instruction("IM 1", static cpu => { cpu.Reg.IM = 1; });
        table[0x78] = new Instruction("IN A,(C)", static cpu => { DoIn(cpu, ref cpu.Reg.A); });
        table[0x79] = new Instruction("OUT (C),A", static cpu => { DoOut(cpu, cpu.Reg.A); });
        table[0x7A] = new Instruction("ADC HL,SP", static cpu => { DoAdcHl(cpu, cpu.Reg.SP); });
        table[0x7B] = new Instruction("LD SP,(nn)", static cpu => { cpu.Reg.SP = LoadRegisterPair(cpu); });
        table[0x7C] = new Instruction("NEG", static cpu => { cpu.Reg.A = cpu.Alu.SubtractAndSetFlags((byte)0, cpu.Reg.A, subCf: false); });
        table[0x7D] = new Instruction("RETN", static cpu => { DoReti(cpu); });
        table[0x7E] = new Instruction("IM 2", static cpu => { cpu.Reg.IM = 2; });

        table[0xA0] = new Instruction("LDI", static cpu => { DoLdi(cpu); });
        table[0xA1] = new Instruction("CPI", static cpu => { DoCpi(cpu); });
        table[0xA2] = new Instruction("INI", static cpu => { DoIni(cpu, repeat: false); });
        table[0xA3] = new Instruction("OUTI", static cpu => { DoOuti(cpu, repeat: false); });
        table[0xA8] = new Instruction("LDD", static cpu => { DoLdd(cpu); });
        table[0xA9] = new Instruction("CPD", static cpu => { DoCpd(cpu); });
        table[0xAA] = new Instruction("IND", static cpu => { DoInd(cpu, repeat: false); });
        table[0xAB] = new Instruction("OUTD", static cpu => { DoOutd(cpu, repeat: false); });

        table[0xB0] = new Instruction("LDIR", static cpu => { DoLdir(cpu); });
        table[0xB1] = new Instruction("CPIR", static cpu => { DoCpir(cpu); });
        table[0xB2] = new Instruction("INIR", static cpu => { DoIni(cpu, repeat: true); });
        table[0xB3] = new Instruction("OTIR", static cpu => { DoOuti(cpu, repeat: true); });
        table[0xB8] = new Instruction("LDDR", static cpu => { DoLddr(cpu); });
        table[0xB9] = new Instruction("CPDR", static cpu => { DoCpdr(cpu); });
        table[0xBA] = new Instruction("INDR", static cpu => { DoInd(cpu, repeat: true); });
        table[0xBB] = new Instruction("OTDR", static cpu => { DoOutd(cpu, repeat: true); });

        return table;
    }

    private static void DoIn(Cpu cpu, ref byte reg)
    {
        var value = ReadPortValue(cpu, cpu.Reg.BC);
        reg = value;
        SetInFlags(cpu, value);
        cpu.InternalWait(4);
    }

    private static void DoInDiscard(Cpu cpu)
    {
        var value = ReadPortValue(cpu, cpu.Reg.BC);
        SetInFlags(cpu, value);
        cpu.InternalWait(4);
    }

    private static void DoOut(Cpu cpu, byte value)
    {
        cpu.Bus.WritePort(cpu.Reg.BC, value);
        cpu.InternalWait(4);
    }

    private static void DoAdcHl(Cpu cpu, ushort value)
    {
        cpu.Reg.HL = cpu.Alu.AddAndSetFlags(cpu.Reg.HL, value, addCf: true);
        cpu.InternalWait(7);
    }

    private static void DoSbcHl(Cpu cpu, ushort value)
    {
        cpu.Reg.HL = cpu.Alu.SubtractAndSetFlags(cpu.Reg.HL, value, subCf: true);
        cpu.InternalWait(7);
    }

    private static ushort LoadRegisterPair(Cpu cpu)
    {
        var address = cpu.Fetch16();
        var lo = cpu.Read8(address);
        var hi = cpu.Read8((ushort)(address + 1));
        return (ushort)((hi << 8) | lo);
    }

    private static void StoreRegisterPair(Cpu cpu, ushort value)
    {
        var address = cpu.Fetch16();
        cpu.Write16(address, value);
    }

    private static void DoReti(Cpu cpu)
    {
        var lo = cpu.Read8(cpu.Reg.SP++);
        var hi = cpu.Read8(cpu.Reg.SP++);
        cpu.Reg.PC = (ushort)((hi << 8) | lo);
        cpu.Reg.IFF1 = cpu.Reg.IFF2;
    }

    private static void LoadAFromInterruptRegister(Cpu cpu, byte value)
    {
        var carry = cpu.Reg.Cf;
        cpu.Reg.A = value;
        cpu.Reg.Sf = (value & 0x80) != 0;
        cpu.Reg.Zf = value == 0;
        cpu.Reg.Hf = false;
        cpu.Reg.Nf = false;
        cpu.Reg.Pf = cpu.Reg.IFF2;
        cpu.Reg.SetFlags53From(value);
        cpu.Reg.Cf = carry;
        cpu.InternalWait(1);
    }

    private static void SetInFlags(Cpu cpu, byte value)
    {
        var carry = cpu.Reg.Cf;
        cpu.Reg.Sf = (value & 0x80) != 0;
        cpu.Reg.Zf = value == 0;
        cpu.Reg.Hf = false;
        cpu.Reg.Nf = false;
        cpu.Reg.Pf = Alu.IsEvenParity(value);
        cpu.Reg.SetFlags53From(value);
        cpu.Reg.Cf = carry;
    }

    private static byte ReadPortValue(Cpu cpu, ushort portAddress) =>
        cpu.Bus.ReadPort(portAddress);

    private static void DoLdi(Cpu cpu)
    {
        var value = cpu.Read8(cpu.Reg.HL);
        cpu.Write8(cpu.Reg.DE, value);
        cpu.Reg.HL++;
        cpu.Reg.DE++;
        cpu.Reg.BC--;
        cpu.Reg.Hf = false;
        cpu.Reg.Pf = cpu.Reg.BC != 0;
        cpu.Reg.Nf = false;
        var sum = (byte)(cpu.Reg.A + value);
        cpu.Reg.Flag5 = sum.IsBitSet(1);
        cpu.Reg.Flag3 = sum.IsBitSet(3);
        cpu.InternalWait(2);
    }

    private static void DoLdd(Cpu cpu)
    {
        var value = cpu.Read8(cpu.Reg.HL);
        cpu.Write8(cpu.Reg.DE, value);
        cpu.Reg.HL--;
        cpu.Reg.DE--;
        cpu.Reg.BC--;
        cpu.Reg.Hf = false;
        cpu.Reg.Pf = cpu.Reg.BC != 0;
        cpu.Reg.Nf = false;
        var sum = (byte)(cpu.Reg.A + value);
        cpu.Reg.Flag5 = sum.IsBitSet(1);
        cpu.Reg.Flag3 = sum.IsBitSet(3);
        cpu.InternalWait(2);
    }

    private static void DoLdir(Cpu cpu)
    {
        DoLdi(cpu);
        if (cpu.Reg.BC == 0)
            return;
        cpu.Reg.PC -= 2;
        cpu.InternalWait(5);
    }

    private static void DoLddr(Cpu cpu)
    {
        DoLdd(cpu);
        if (cpu.Reg.BC == 0)
            return;
        cpu.Reg.PC -= 2;
        cpu.InternalWait(5);
    }

    private static void DoCpi(Cpu cpu)
    {
        var oldCarry = cpu.Reg.Cf;
        var value = cpu.Read8(cpu.Reg.HL);
        cpu.Alu.SubtractAndSetFlags(cpu.Reg.A, value, subCf: false);
        var adjusted = (byte)(cpu.Reg.A - value);
        if (cpu.Reg.Hf)
            adjusted--;
        cpu.Reg.HL++;
        cpu.Reg.BC--;
        cpu.Reg.Pf = cpu.Reg.BC != 0;
        cpu.Reg.Nf = true;
        cpu.Reg.Cf = oldCarry;
        cpu.Reg.Flag3 = adjusted.IsBitSet(3);
        cpu.Reg.Flag5 = adjusted.IsBitSet(1);
        cpu.InternalWait(5);
    }

    private static void DoCpd(Cpu cpu)
    {
        var oldCarry = cpu.Reg.Cf;
        var value = cpu.Read8(cpu.Reg.HL);
        cpu.Alu.SubtractAndSetFlags(cpu.Reg.A, value, subCf: false);
        var adjusted = (byte)(cpu.Reg.A - value);
        if (cpu.Reg.Hf)
            adjusted--;
        cpu.Reg.HL--;
        cpu.Reg.BC--;
        cpu.Reg.Pf = cpu.Reg.BC != 0;
        cpu.Reg.Nf = true;
        cpu.Reg.Cf = oldCarry;
        cpu.Reg.Flag3 = adjusted.IsBitSet(3);
        cpu.Reg.Flag5 = adjusted.IsBitSet(1);
        cpu.InternalWait(5);
    }

    private static void DoCpir(Cpu cpu)
    {
        DoCpi(cpu);
        if (cpu.Reg.Zf || cpu.Reg.BC == 0)
            return;
        cpu.Reg.PC -= 2;
        cpu.InternalWait(5);
    }

    private static void DoCpdr(Cpu cpu)
    {
        DoCpd(cpu);
        if (cpu.Reg.Zf || cpu.Reg.BC == 0)
            return;
        cpu.Reg.PC -= 2;
        cpu.InternalWait(5);
    }

    private static void DoIni(Cpu cpu, bool repeat)
    {
        var portValue = ReadPortValue(cpu, cpu.Reg.BC);
        cpu.Write8(cpu.Reg.HL, portValue);
        cpu.Reg.HL++;
        cpu.Reg.B = cpu.Alu.DecAndSetFlags(cpu.Reg.B);

        var incC = (cpu.Reg.C + 1) & 0xFF;
        cpu.Reg.Hf = portValue + incC > 0xFF;
        cpu.Reg.Cf = cpu.Reg.Hf;
        cpu.Reg.Pf = Alu.IsEvenParity((byte)(((portValue + incC) & 7) ^ cpu.Reg.B));
        cpu.Reg.Nf = portValue.IsBitSet(7);

        cpu.InternalWait(5);
        if (!repeat || cpu.Reg.B == 0)
            return;
        cpu.Reg.PC -= 2;
        cpu.InternalWait(5);
    }

    private static void DoInd(Cpu cpu, bool repeat)
    {
        var portValue = ReadPortValue(cpu, cpu.Reg.BC);
        cpu.Write8(cpu.Reg.HL, portValue);
        cpu.Reg.HL--;
        cpu.Reg.B = cpu.Alu.DecAndSetFlags(cpu.Reg.B);

        var decC = (cpu.Reg.C - 1) & 0xFF;
        cpu.Reg.Hf = portValue + decC > 0xFF;
        cpu.Reg.Cf = cpu.Reg.Hf;
        cpu.Reg.Pf = Alu.IsEvenParity((byte)(((portValue + decC) & 7) ^ cpu.Reg.B));
        cpu.Reg.Nf = portValue.IsBitSet(7);

        cpu.InternalWait(5);
        if (!repeat || cpu.Reg.B == 0)
            return;
        cpu.Reg.PC -= 2;
        cpu.InternalWait(5);
    }

    private static void DoOuti(Cpu cpu, bool repeat)
    {
        var value = cpu.Read8(cpu.Reg.HL);
        cpu.Reg.B = cpu.Alu.DecAndSetFlags(cpu.Reg.B);
        cpu.Reg.HL++;
        cpu.Bus.WritePort(cpu.Reg.BC, value);

        cpu.Reg.Hf = value + cpu.Reg.L > 0xFF;
        cpu.Reg.Cf = cpu.Reg.Hf;
        cpu.Reg.Pf = Alu.IsEvenParity((byte)(((value + cpu.Reg.L) & 7) ^ cpu.Reg.B));
        cpu.Reg.Nf = value.IsBitSet(7);

        cpu.InternalWait(5);
        if (!repeat || cpu.Reg.B == 0)
            return;
        cpu.Reg.PC -= 2;
        cpu.InternalWait(5);
    }

    private static void DoOutd(Cpu cpu, bool repeat)
    {
        var value = cpu.Read8(cpu.Reg.HL);
        cpu.Reg.B = cpu.Alu.DecAndSetFlags(cpu.Reg.B);
        cpu.Reg.HL--;
        cpu.Bus.WritePort(cpu.Reg.BC, value);

        cpu.Reg.Hf = value + cpu.Reg.L > 0xFF;
        cpu.Reg.Cf = cpu.Reg.Hf;
        cpu.Reg.Pf = Alu.IsEvenParity((byte)(((value + cpu.Reg.L) & 7) ^ cpu.Reg.B));
        cpu.Reg.Nf = value.IsBitSet(7);

        cpu.InternalWait(5);
        if (!repeat || cpu.Reg.B == 0)
            return;
        cpu.Reg.PC -= 2;
        cpu.InternalWait(5);
    }

    private static void DoRld(Cpu cpu)
    {
        var value = cpu.Read8(cpu.Reg.HL);
        var newValue = ((value & 0x0F) << 4) | (cpu.Reg.A & 0x0F);
        var newA = (cpu.Reg.A & 0xF0) | ((value & 0xF0) >> 4);
        cpu.Reg.A = (byte)newA;
        cpu.Write8(cpu.Reg.HL, (byte)newValue);

        cpu.Reg.Sf = (cpu.Reg.A & 0x80) != 0;
        cpu.Reg.Zf = cpu.Reg.A == 0;
        cpu.Reg.Hf = false;
        cpu.Reg.Nf = false;
        cpu.Reg.Pf = Alu.IsEvenParity(cpu.Reg.A);
        cpu.Reg.SetFlags53FromA();
        cpu.InternalWait(4);
    }

    private static void DoRrd(Cpu cpu)
    {
        var value = cpu.Read8(cpu.Reg.HL);
        var newValue = ((cpu.Reg.A & 0x0F) << 4) | ((value & 0xF0) >> 4);
        var newA = (cpu.Reg.A & 0xF0) | (value & 0x0F);
        cpu.Reg.A = (byte)newA;
        cpu.Write8(cpu.Reg.HL, (byte)newValue);

        cpu.Reg.Sf = (cpu.Reg.A & 0x80) != 0;
        cpu.Reg.Zf = cpu.Reg.A == 0;
        cpu.Reg.Hf = false;
        cpu.Reg.Nf = false;
        cpu.Reg.Pf = Alu.IsEvenParity(cpu.Reg.A);
        cpu.Reg.SetFlags53FromA();
        cpu.InternalWait(4);
    }
}
