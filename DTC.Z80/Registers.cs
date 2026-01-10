// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace DTC.Z80;

/// <summary>
/// Minimal Z80 register set for early tests.
/// </summary>
public sealed class Registers
{
    public byte A { get; set; }
    public byte F { get; set; }
    public byte B { get; set; }
    public byte C { get; set; }
    public byte D { get; set; }
    public byte E { get; set; }
    public byte H { get; set; }
    public byte L { get; set; }
    public ushort SP { get; set; }
    public ushort PC { get; set; }

    public ushort AF
    {
        get => (ushort)((A << 8) | F);
        set
        {
            A = (byte)(value >> 8);
            F = (byte)(value & 0xF0);
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

    public bool Zf
    {
        get => (F & 0x80) != 0;
        set => F = value ? (byte)(F | 0x80) : (byte)(F & ~0x80);
    }

    public bool Nf
    {
        get => (F & 0x40) != 0;
        set => F = value ? (byte)(F | 0x40) : (byte)(F & ~0x40);
    }

    public bool Hf
    {
        get => (F & 0x20) != 0;
        set => F = value ? (byte)(F | 0x20) : (byte)(F & ~0x20);
    }

    public bool Cf
    {
        get => (F & 0x10) != 0;
        set => F = value ? (byte)(F | 0x10) : (byte)(F & ~0x10);
    }

    public void Clear()
    {
        A = F = B = C = D = E = H = L = 0;
        SP = 0;
        PC = 0;
    }
}
