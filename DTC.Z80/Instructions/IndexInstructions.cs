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
using DTC.Core.Extensions;

namespace DTC.Z80.Instructions;

public static class IndexInstructions
{
    public static void Execute(Cpu cpu, bool useIX, byte opcode)
    {
        if (opcode == 0xCB)
        {
            var displacement = (sbyte)cpu.Fetch8();
            var cbOpcode = cpu.Fetch8();
            ExecuteIndexedCb(cpu, useIX, displacement, cbOpcode);
            return;
        }

        if (opcode == 0xED)
        {
            Instructions.Table[opcode].Execute(cpu);
            return;
        }

        if (TryExecuteIndexed(cpu, useIX, opcode))
            return;

        ExecuteWithIndexSwap(cpu, useIX, () => Instructions.Table[opcode].Execute(cpu));
    }

    private static void ExecuteWithIndexSwap(Cpu cpu, bool useIX, Action action)
    {
        if (useIX)
        {
            (cpu.Reg.IX, cpu.Reg.HL) = (cpu.Reg.HL, cpu.Reg.IX);
            action();
            (cpu.Reg.IX, cpu.Reg.HL) = (cpu.Reg.HL, cpu.Reg.IX);
            return;
        }

        (cpu.Reg.IY, cpu.Reg.HL) = (cpu.Reg.HL, cpu.Reg.IY);
        action();
        (cpu.Reg.IY, cpu.Reg.HL) = (cpu.Reg.HL, cpu.Reg.IY);
    }

    private static bool TryExecuteIndexed(Cpu cpu, bool useIX, byte opcode)
    {
        if (opcode == 0x34)
        {
            ExecuteIndexedUnary(cpu, useIX, cpu.Alu.IncAndSetFlags, wait: 6);
            return true;
        }

        if (opcode == 0x35)
        {
            ExecuteIndexedUnary(cpu, useIX, cpu.Alu.DecAndSetFlags, wait: 6);
            return true;
        }

        if (opcode == 0x36)
        {
            var addr = GetIndexedAddress(cpu, useIX);
            var value = cpu.Fetch8();
            cpu.Write8(addr, value);
            cpu.InternalWait(2);
            return true;
        }

        if (TryGetIndexedLoadRegister(opcode, out var loadRegister))
        {
            LoadFromIndexed(cpu, useIX, loadRegister);
            return true;
        }

        if (TryGetIndexedStoreRegister(opcode, out var storeRegister))
        {
            StoreToIndexed(cpu, useIX, storeRegister);
            return true;
        }

        return TryExecuteIndexedAlu(cpu, useIX, opcode);
    }

    private static ushort GetIndex(Cpu cpu, bool useIX) => useIX ? cpu.Reg.IX : cpu.Reg.IY;

    private static ushort GetIndexedAddress(Cpu cpu, bool useIX)
    {
        var displacement = (sbyte)cpu.Fetch8();
        return (ushort)(GetIndex(cpu, useIX) + displacement);
    }

    private static void ExecuteIndexedUnary(Cpu cpu, bool useIX, Func<byte, byte> operation, int wait)
    {
        var addr = GetIndexedAddress(cpu, useIX);
        var value = cpu.Read8(addr);
        value = operation(value);
        cpu.Write8(addr, value);
        cpu.InternalWait(wait);
    }

    private static bool TryGetIndexedLoadRegister(byte opcode, out int register)
    {
        if ((opcode & 0xC7) == 0x46 && opcode != 0x76)
        {
            register = (opcode >> 3) & 7;
            return true;
        }

        register = 0;
        return false;
    }

    private static bool TryGetIndexedStoreRegister(byte opcode, out int register)
    {
        if ((opcode & 0xF8) == 0x70 && opcode != 0x76)
        {
            register = opcode & 7;
            return true;
        }

        register = 0;
        return false;
    }

    private static void LoadFromIndexed(Cpu cpu, bool useIX, int register)
    {
        var addr = GetIndexedAddress(cpu, useIX);
        var value = cpu.Read8(addr);
        SetBaseRegisterValue(cpu, register, value);
        cpu.InternalWait(5);
    }

    private static void StoreToIndexed(Cpu cpu, bool useIX, int register)
    {
        var addr = GetIndexedAddress(cpu, useIX);
        var value = GetBaseRegisterValue(cpu, register);
        cpu.Write8(addr, value);
        cpu.InternalWait(5);
    }

    private static bool TryExecuteIndexedAlu(Cpu cpu, bool useIX, byte opcode)
    {
        switch (opcode)
        {
            case 0x86:
                ExecuteIndexedAlu(cpu, useIX, (a, b) => cpu.Alu.AddAndSetFlags(a, b, addCf: false));
                return true;
            case 0x8E:
                ExecuteIndexedAlu(cpu, useIX, (a, b) => cpu.Alu.AddAndSetFlags(a, b, addCf: true));
                return true;
            case 0x96:
                ExecuteIndexedAlu(cpu, useIX, (a, b) => cpu.Alu.SubtractAndSetFlags(a, b, subCf: false));
                return true;
            case 0x9E:
                ExecuteIndexedAlu(cpu, useIX, (a, b) => cpu.Alu.SubtractAndSetFlags(a, b, subCf: true));
                return true;
            case 0xA6:
                ExecuteIndexedLogic(cpu, useIX, cpu.Alu.And);
                return true;
            case 0xAE:
                ExecuteIndexedLogic(cpu, useIX, cpu.Alu.Xor);
                return true;
            case 0xB6:
                ExecuteIndexedLogic(cpu, useIX, cpu.Alu.Or);
                return true;
            case 0xBE:
                ExecuteIndexedCompare(cpu, useIX);
                return true;
            default:
                return false;
        }
    }

    private static void ExecuteIndexedAlu(Cpu cpu, bool useIX, Func<byte, byte, byte> operation)
    {
        var addr = GetIndexedAddress(cpu, useIX);
        var value = cpu.Read8(addr);
        cpu.Reg.A = operation(cpu.Reg.A, value);
        cpu.InternalWait(5);
    }

    private static void ExecuteIndexedLogic(Cpu cpu, bool useIX, Action<byte> operation)
    {
        var addr = GetIndexedAddress(cpu, useIX);
        var value = cpu.Read8(addr);
        operation(value);
        cpu.InternalWait(5);
    }

    private static void ExecuteIndexedCompare(Cpu cpu, bool useIX)
    {
        var addr = GetIndexedAddress(cpu, useIX);
        var value = cpu.Read8(addr);
        cpu.Alu.SubtractAndSetFlags(cpu.Reg.A, value, subCf: false);
        cpu.Reg.Flag5 = value.IsBitSet(5);
        cpu.Reg.Flag3 = value.IsBitSet(3);
        cpu.InternalWait(5);
    }

    private static byte GetBaseRegisterValue(Cpu cpu, int code) => code switch
    {
        0 => cpu.Reg.B,
        1 => cpu.Reg.C,
        2 => cpu.Reg.D,
        3 => cpu.Reg.E,
        4 => cpu.Reg.H,
        5 => cpu.Reg.L,
        7 => cpu.Reg.A,
        _ => 0
    };

    private static void SetBaseRegisterValue(Cpu cpu, int code, byte value)
    {
        switch (code)
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

    private static void ExecuteIndexedCb(Cpu cpu, bool useIX, sbyte displacement, byte opcode)
    {
        var addr = (ushort)(GetIndex(cpu, useIX) + displacement);

        if (opcode < 0x40)
        {
            var group = opcode / 8;
            var regIndex = opcode % 8;
            var value = ApplyIndexedShiftRotate(cpu, addr, group);
            WriteIndexedCbResult(cpu, addr, regIndex, value);
            return;
        }

        if (opcode < 0x80)
        {
            var bit = (opcode - 0x40) / 8;
            var value = cpu.Read8(addr);
            var isSet = value.IsBitSet((byte)bit);
            cpu.Reg.Zf = !isSet;
            cpu.Reg.Hf = true;
            cpu.Reg.Nf = false;
            cpu.Reg.Pf = cpu.Reg.Zf;
            cpu.Reg.Sf = bit == 7 && isSet;
            SetFlags53FromAddress(cpu, addr);
            cpu.InternalWait(3);
            return;
        }

        if (opcode < 0xC0)
        {
            var bit = (opcode - 0x80) / 8;
            var regIndex = opcode % 8;
            var value = cpu.Read8(addr);
            value = value.ResetBit((byte)bit);
            WriteIndexedCbResult(cpu, addr, regIndex, value);
            return;
        }

        {
            var bit = (opcode - 0xC0) / 8;
            var regIndex = opcode % 8;
            var value = cpu.Read8(addr);
            value = value.SetBit((byte)bit);
            WriteIndexedCbResult(cpu, addr, regIndex, value);
        }
    }

    private static byte ApplyIndexedShiftRotate(Cpu cpu, ushort addr, int group)
    {
        var value = cpu.Read8(addr);
        return group switch
        {
            0 => cpu.Alu.RotateLeftCircular(value),
            1 => cpu.Alu.RotateRightCircular(value),
            2 => cpu.Alu.RotateLeft(value),
            3 => cpu.Alu.RotateRight(value),
            4 => cpu.Alu.ShiftLeft(value),
            5 => cpu.Alu.ShiftRightArithmetic(value),
            6 => cpu.Alu.ShiftLeftLogical(value),
            7 => cpu.Alu.ShiftRightLogical(value),
            _ => throw new ArgumentOutOfRangeException(nameof(group))
        };
    }

    private static void WriteIndexedCbResult(Cpu cpu, ushort addr, int register, byte value)
    {
        cpu.Write8(addr, value);
        if (register != 6)
            SetBaseRegisterValue(cpu, register, value);
        cpu.InternalWait(3);
    }

    private static void SetFlags53FromAddress(Cpu cpu, ushort addr)
    {
        var highByte = (byte)(addr >> 8);
        cpu.Reg.Flag5 = highByte.IsBitSet(5);
        cpu.Reg.Flag3 = highByte.IsBitSet(3);
    }
}
