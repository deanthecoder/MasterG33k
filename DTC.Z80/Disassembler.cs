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
using DTC.Z80.Instructions;

namespace DTC.Z80;

/// <summary>
/// Provides helpers for turning opcode data back into human-readable mnemonics.
/// </summary>
public static class Disassembler
{
    public static string GetInstructionWithOperands(Bus memory, ushort address)
    {
        if (memory == null)
            throw new ArgumentNullException(nameof(memory));

        var opcode = memory.Read8(address);
        return opcode switch
        {
            0xCB => GetCbInstruction(memory, address),
            0xED => GetEdInstruction(memory, address),
            0xDD => GetIndexInstruction(memory, address, useIX: true),
            0xFD => GetIndexInstruction(memory, address, useIX: false),
            _ => ResolveImmediateOperands(GetBaseMnemonic(opcode), memory, address, opcodeBytes: 1)
        };
    }

    private static string GetBaseMnemonic(byte opcode) =>
        Instructions.Instructions.Table[opcode]?.Mnemonic ?? $"DB ${opcode:X2}";

    private static string GetCbInstruction(Bus memory, ushort address)
    {
        var cbOpcode = memory.Read8((ushort)(address + 1));
        return CbInstructions.Table[cbOpcode]?.Mnemonic ?? $"CB ${cbOpcode:X2}";
    }

    private static string GetEdInstruction(Bus memory, ushort address)
    {
        var edOpcode = memory.Read8((ushort)(address + 1));
        var mnemonic = EdInstructions.Table[edOpcode]?.Mnemonic ?? $"ED ${edOpcode:X2}";
        return ResolveImmediateOperands(mnemonic, memory, address, opcodeBytes: 2);
    }

    private static string GetIndexInstruction(Bus memory, ushort address, bool useIX)
    {
        var opcode = memory.Read8((ushort)(address + 1));
        if (opcode == 0xCB)
        {
            var displacement = unchecked((sbyte)memory.Read8((ushort)(address + 2)));
            var cbOpcode = memory.Read8((ushort)(address + 3));
            return FormatIndexedCbMnemonic(cbOpcode, useIX, displacement);
        }

        if (opcode == 0xED)
            return $"{(useIX ? "DD" : "FD")} {GetEdInstruction(memory, (ushort)(address + 1))}";

        var mnemonic = GetBaseMnemonic(opcode);
        if (mnemonic == "JP (HL)")
            return ReplaceIndexRegisters(mnemonic, useIX);

        if (mnemonic.Contains("(HL)", StringComparison.Ordinal))
        {
            var displacement = unchecked((sbyte)memory.Read8((ushort)(address + 2)));
            var indexed = mnemonic.Replace("(HL)", FormatIndexOperand(useIX, displacement), StringComparison.Ordinal);
            return ResolveImmediateOperands(indexed, memory, address, opcodeBytes: 3);
        }

        return ResolveImmediateOperands(ReplaceIndexRegisters(mnemonic, useIX), memory, address, opcodeBytes: 2);
    }

    private static string FormatIndexedCbMnemonic(byte opcode, bool useIX, sbyte displacement)
    {
        var indexOperand = FormatIndexOperand(useIX, displacement);
        var regIndex = opcode & 0x07;

        if (opcode < 0x40)
        {
            var operation = GetCbRotateMnemonic(opcode / 8);
            var register = GetRegisterName(regIndex);
            return regIndex == 6
                ? $"{operation} {indexOperand}"
                : $"{operation} {indexOperand},{register}";
        }

        if (opcode < 0x80)
        {
            var bit = (opcode - 0x40) / 8;
            return $"BIT {bit},{indexOperand}";
        }

        if (opcode < 0xC0)
        {
            var bit = (opcode - 0x80) / 8;
            var register = GetRegisterName(regIndex);
            return regIndex == 6
                ? $"RES {bit},{indexOperand}"
                : $"RES {bit},{indexOperand},{register}";
        }

        {
            var bit = (opcode - 0xC0) / 8;
            var register = GetRegisterName(regIndex);
            return regIndex == 6
                ? $"SET {bit},{indexOperand}"
                : $"SET {bit},{indexOperand},{register}";
        }
    }

    private static string ResolveImmediateOperands(string mnemonic, Bus memory, ushort address, int opcodeBytes)
    {
        var immediateLength = GetImmediateLength(mnemonic);
        if (immediateLength == 0)
            return mnemonic;

        var immediateAddress = (ushort)(address + opcodeBytes);
        var value = immediateLength == 1
            ? memory.Read8(immediateAddress)
            : memory.Read16(immediateAddress);

        var hex = immediateLength == 1 ? $"${value:X2}" : $"${value:X4}";
        var resolved = ReplaceImmediatePlaceholders(mnemonic, hex);

        if (!IsRelativeMnemonic(mnemonic))
            return resolved;

        var displacement = unchecked((sbyte)memory.Read8(immediateAddress));
        var target = (ushort)(address + opcodeBytes + immediateLength + displacement);
        return $"{resolved} -> ${target:X4}";
    }

    private static int GetImmediateLength(string mnemonic)
    {
        if (mnemonic.Contains("a16", StringComparison.Ordinal))
            return 2;
        if (mnemonic.Contains("e8", StringComparison.Ordinal))
            return 1;
        if (mnemonic.Contains("nn", StringComparison.Ordinal))
            return Is8BitImmediate(mnemonic) ? 1 : 2;
        return mnemonic.Contains('n', StringComparison.Ordinal) ? 1 : 0;
    }

    private static bool IsRelativeMnemonic(string mnemonic) =>
        mnemonic.StartsWith("JR", StringComparison.Ordinal) ||
        mnemonic.StartsWith("DJNZ", StringComparison.Ordinal);

    private static string ReplaceImmediatePlaceholders(string mnemonic, string hex) =>
        mnemonic
            .Replace("a16", hex, StringComparison.Ordinal)
            .Replace("nn", hex, StringComparison.Ordinal)
            .Replace("e8", hex, StringComparison.Ordinal)
            .Replace("n", hex, StringComparison.Ordinal);

    private static bool Is8BitImmediate(string mnemonic)
    {
        if (mnemonic.Contains("(nn)", StringComparison.Ordinal))
            return false;

        if (mnemonic.StartsWith("ADC A,", StringComparison.Ordinal) ||
            mnemonic.StartsWith("ADD A,", StringComparison.Ordinal) ||
            mnemonic.StartsWith("AND A,", StringComparison.Ordinal) ||
            mnemonic.StartsWith("CP A,", StringComparison.Ordinal) ||
            mnemonic.StartsWith("OR A,", StringComparison.Ordinal) ||
            mnemonic.StartsWith("SBC A,", StringComparison.Ordinal) ||
            mnemonic.StartsWith("SUB A,", StringComparison.Ordinal) ||
            mnemonic.StartsWith("XOR A,", StringComparison.Ordinal))
            return true;

        if (!mnemonic.StartsWith("LD ", StringComparison.Ordinal))
            return false;

        var operands = mnemonic[3..];
        var commaIndex = operands.IndexOf(',');
        if (commaIndex < 0)
            return false;

        var destination = operands[..commaIndex].Trim();
        return destination is "A" or "B" or "C" or "D" or "E" or "H" or "L" or "(HL)";
    }

    private static string ReplaceIndexRegisters(string mnemonic, bool useIX)
    {
        var spaceIndex = mnemonic.IndexOf(' ');
        if (spaceIndex < 0)
            return mnemonic;

        var operation = mnemonic[..spaceIndex];
        var operands = mnemonic[(spaceIndex + 1)..];
        var index = useIX ? "IX" : "IY";

        var parts = operands.Split(',', StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i] switch
            {
                "HL" => index,
                "H" => $"{index}H",
                "L" => $"{index}L",
                _ => parts[i]
            };
        }

        return $"{operation} {string.Join(",", parts)}";
    }

    private static string FormatIndexOperand(bool useIX, sbyte displacement)
    {
        var index = useIX ? "IX" : "IY";
        if (displacement == 0)
            return $"({index})";

        var sign = displacement < 0 ? "-" : "+";
        var magnitude = (byte)(displacement < 0 ? -displacement : displacement);
        return $"({index}{sign}${magnitude:X2})";
    }

    private static string GetRegisterName(int register) =>
        register switch
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

    private static string GetCbRotateMnemonic(int group) =>
        group switch
        {
            0 => "RLC",
            1 => "RRC",
            2 => "RL",
            3 => "RR",
            4 => "SLA",
            5 => "SRA",
            6 => "SLL",
            7 => "SRL",
            _ => "???"
        };
}
