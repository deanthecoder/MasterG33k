// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any non-commercial
// purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using DTC.Core.Extensions;

namespace DTC.Z80.Instructions;

public static class CbInstructions
{
    public static readonly Instruction[] Table = Build();

    private static Instruction[] Build()
    {
        var table = new Instruction[256];
        for (var opcode = 0; opcode < 0x100; opcode++)
        {
            if (opcode < 0x40)
            {
                var group = opcode / 8;
                var regIndex = opcode % 8;
                var mnemonic = GetShiftRotateMnemonic(group);
                table[opcode] = new Instruction(
                    $"{mnemonic} {GetRegisterName(regIndex)}",
                    cpu =>
                    {
                        var value = GetRegisterValue(cpu, regIndex, out var isMemory);
                        value = group switch
                        {
                            0 => cpu.Alu.RotateLeftCircular(value),
                            1 => cpu.Alu.RotateRightCircular(value),
                            2 => cpu.Alu.RotateLeft(value),
                            3 => cpu.Alu.RotateRight(value),
                            4 => cpu.Alu.ShiftLeft(value),
                            5 => cpu.Alu.ShiftRightArithmetic(value),
                            6 => cpu.Alu.ShiftLeftLogical(value),
                            7 => cpu.Alu.ShiftRightLogical(value),
                            _ => value
                        };
                        SetRegisterValue(cpu, regIndex, value, isMemory);
                        if (isMemory)
                            cpu.InternalWait(1);
                    });
                continue;
            }

            if (opcode < 0x80)
            {
                var bit = (opcode - 0x40) / 8;
                var regIndex = opcode % 8;
                table[opcode] = new Instruction(
                    $"BIT {bit},{GetRegisterName(regIndex)}",
                    cpu =>
                    {
                        var value = GetRegisterValue(cpu, regIndex, out var isMemory);
                        var isSet = value.IsBitSet((byte)bit);
                        cpu.Reg.Zf = !isSet;
                        cpu.Reg.Hf = true;
                        cpu.Reg.Nf = false;
                        cpu.Reg.Pf = cpu.Reg.Zf;
                        cpu.Reg.Sf = bit == 7 && isSet;
                        cpu.Reg.Flag5 = value.IsBitSet(5);
                        cpu.Reg.Flag3 = value.IsBitSet(3);
                        if (isMemory)
                            cpu.InternalWait(1);
                    });
                continue;
            }

            if (opcode < 0xC0)
            {
                var bit = (opcode - 0x80) / 8;
                var regIndex = opcode % 8;
                table[opcode] = new Instruction(
                    $"RES {bit},{GetRegisterName(regIndex)}",
                    cpu =>
                    {
                        var value = GetRegisterValue(cpu, regIndex, out var isMemory);
                        value = value.ResetBit((byte)bit);
                        SetRegisterValue(cpu, regIndex, value, isMemory);
                        if (isMemory)
                            cpu.InternalWait(1);
                    });
                continue;
            }

            {
                var bit = (opcode - 0xC0) / 8;
                var regIndex = opcode % 8;
                table[opcode] = new Instruction(
                    $"SET {bit},{GetRegisterName(regIndex)}",
                    cpu =>
                    {
                        var value = GetRegisterValue(cpu, regIndex, out var isMemory);
                        value = value.SetBit((byte)bit);
                        SetRegisterValue(cpu, regIndex, value, isMemory);
                        if (isMemory)
                            cpu.InternalWait(1);
                    });
            }
        }

        return table;
    }

    private static string GetShiftRotateMnemonic(int group) => group switch
    {
        0 => "RLC",
        1 => "RRC",
        2 => "RL",
        3 => "RR",
        4 => "SLA",
        5 => "SRA",
        6 => "SLL",
        7 => "SRL",
        _ => "?"
    };

    private static string GetRegisterName(int index) => index switch
    {
        0 => "B",
        1 => "C",
        2 => "D",
        3 => "E",
        4 => "H",
        5 => "L",
        6 => "(HL)",
        7 => "A",
        _ => "?"
    };

    private static byte GetRegisterValue(Cpu cpu, int index, out bool isMemory)
    {
        isMemory = false;
        return index switch
        {
            0 => cpu.Reg.B,
            1 => cpu.Reg.C,
            2 => cpu.Reg.D,
            3 => cpu.Reg.E,
            4 => cpu.Reg.H,
            5 => cpu.Reg.L,
            6 => ReadHl(cpu, out isMemory),
            7 => cpu.Reg.A,
            _ => 0
        };
    }

    private static byte ReadHl(Cpu cpu, out bool isMemory)
    {
        isMemory = true;
        return cpu.Read8(cpu.Reg.HL);
    }

    private static void SetRegisterValue(Cpu cpu, int index, byte value, bool isMemory)
    {
        if (isMemory)
        {
            cpu.Write8(cpu.Reg.HL, value);
            return;
        }

        switch (index)
        {
            case 0:
                cpu.Reg.B = value;
                break;
            case 1:
                cpu.Reg.C = value;
                break;
            case 2:
                cpu.Reg.D = value;
                break;
            case 3:
                cpu.Reg.E = value;
                break;
            case 4:
                cpu.Reg.H = value;
                break;
            case 5:
                cpu.Reg.L = value;
                break;
            case 7:
                cpu.Reg.A = value;
                break;
        }
    }
}
