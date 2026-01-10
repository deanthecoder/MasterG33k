// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using CSharp.Core.Extensions;

namespace DTC.Z80;

/// <summary>
/// Z80 register set. Intended as a minimal, accurate scaffold.
/// </summary>
public sealed class Registers
{
    private const byte SignBit = 7;
    private const byte ZeroBit = 6;
    private const byte Flag5Bit = 5;
    private const byte HalfCarryBit = 4;
    private const byte Flag3Bit = 3;
    private const byte ParityOverflowBit = 2;
    private const byte NegativeBit = 1;
    private const byte CarryBit = 0;

    public struct RegisterSet
    {
        public byte A { get; set; }
        public byte F { get; set; }
        public byte B { get; set; }
        public byte C { get; set; }
        public byte D { get; set; }
        public byte E { get; set; }
        public byte H { get; set; }
        public byte L { get; set; }

        public ushort AF
        {
            get => (ushort)((A << 8) | F);
            set
            {
                A = (byte)(value >> 8);
                F = (byte)(value & 0xFF);
            }
        }

        public ushort BC
        {
            get => (ushort)((B << 8) | C);
            set
            {
                B = (byte)(value >> 8);
                C = (byte)(value & 0xFF);
            }
        }

        public ushort DE
        {
            get => (ushort)((D << 8) | E);
            set
            {
                D = (byte)(value >> 8);
                E = (byte)(value & 0xFF);
            }
        }

        public ushort HL
        {
            get => (ushort)((H << 8) | L);
            set
            {
                H = (byte)(value >> 8);
                L = (byte)(value & 0xFF);
            }
        }
    }

    private RegisterSet m_main;
    private RegisterSet m_alt;

    public ref RegisterSet Main => ref m_main;

    public ref RegisterSet Alt => ref m_alt;

    public ushort IX { get; set; }
    public ushort IY { get; set; }
    public ushort SP { get; set; }
    public ushort PC { get; set; }

    public byte I { get; set; }
    public byte R { get; set; }

    public byte IXH
    {
        get => (byte)(IX >> 8);
        set => IX = (ushort)((value << 8) | (IX & 0x00FF));
    }

    public byte IXL
    {
        get => (byte)(IX & 0xFF);
        set => IX = (ushort)((IX & 0xFF00) | value);
    }

    public byte IYH
    {
        get => (byte)(IY >> 8);
        set => IY = (ushort)((value << 8) | (IY & 0x00FF));
    }

    public byte IYL
    {
        get => (byte)(IY & 0xFF);
        set => IY = (ushort)((IY & 0xFF00) | value);
    }

    public bool Sf
    {
        get => (m_main.F & 0x80) != 0;
        set => m_main.F = SetFlag(m_main.F, SignBit, value);
    }

    public bool Zf
    {
        get => (m_main.F & 0x40) != 0;
        set => m_main.F = SetFlag(m_main.F, ZeroBit, value);
    }

    public bool Flag5
    {
        get => (m_main.F & 0x20) != 0;
        set => m_main.F = SetFlag(m_main.F, Flag5Bit, value);
    }

    public bool Hf
    {
        get => (m_main.F & 0x10) != 0;
        set => m_main.F = SetFlag(m_main.F, HalfCarryBit, value);
    }

    public bool Flag3
    {
        get => (m_main.F & 0x08) != 0;
        set => m_main.F = SetFlag(m_main.F, Flag3Bit, value);
    }

    public bool Pf
    {
        get => (m_main.F & 0x04) != 0;
        set => m_main.F = SetFlag(m_main.F, ParityOverflowBit, value);
    }

    public bool Nf
    {
        get => (m_main.F & 0x02) != 0;
        set => m_main.F = SetFlag(m_main.F, NegativeBit, value);
    }

    public bool Cf
    {
        get => (m_main.F & 0x01) != 0;
        set => m_main.F = SetFlag(m_main.F, CarryBit, value);
    }

    private static byte SetFlag(byte flags, byte bit, bool value) =>
        value ? flags.SetBit(bit) : flags.ResetBit(bit);

    public void Clear()
    {
        m_main = new RegisterSet();
        m_alt = new RegisterSet();
        IX = IY = SP = PC = 0;
        I = R = 0;
    }
}
