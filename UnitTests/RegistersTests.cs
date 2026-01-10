// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using CSharp.Core.UnitTesting;
using DTC.Z80;

namespace UnitTests;

public class RegistersTests : TestsBase
{
    [Test]
    public void CheckDefaultValues()
    {
        var reg = new Registers();

        Assert.That(reg.Main.A, Is.Zero);
        Assert.That(reg.Main.F, Is.Zero);
        Assert.That(reg.Main.B, Is.Zero);
        Assert.That(reg.Main.C, Is.Zero);
        Assert.That(reg.Main.D, Is.Zero);
        Assert.That(reg.Main.E, Is.Zero);
        Assert.That(reg.Main.H, Is.Zero);
        Assert.That(reg.Main.L, Is.Zero);
        Assert.That(reg.Alt.A, Is.Zero);
        Assert.That(reg.Alt.F, Is.Zero);
        Assert.That(reg.Alt.B, Is.Zero);
        Assert.That(reg.Alt.C, Is.Zero);
        Assert.That(reg.Alt.D, Is.Zero);
        Assert.That(reg.Alt.E, Is.Zero);
        Assert.That(reg.Alt.H, Is.Zero);
        Assert.That(reg.Alt.L, Is.Zero);
        Assert.That(reg.IX, Is.Zero);
        Assert.That(reg.IY, Is.Zero);
        Assert.That(reg.SP, Is.Zero);
        Assert.That(reg.PC, Is.Zero);
        Assert.That(reg.I, Is.Zero);
        Assert.That(reg.R, Is.Zero);

        Assert.That(reg.Sf, Is.False);
        Assert.That(reg.Zf, Is.False);
        Assert.That(reg.Hf, Is.False);
        Assert.That(reg.Pf, Is.False);
        Assert.That(reg.Nf, Is.False);
        Assert.That(reg.Cf, Is.False);
        Assert.That(reg.Flag3, Is.False);
        Assert.That(reg.Flag5, Is.False);
    }

    [Test]
    public void CheckSettingSignFlag()
    {
        var reg = new Registers { Sf = true };
        Assert.That(reg.Sf, Is.True);
        Assert.That(reg.Main.F, Is.EqualTo(0b1000_0000));
    }

    [Test]
    public void CheckSettingZeroFlag()
    {
        var reg = new Registers { Zf = true };
        Assert.That(reg.Zf, Is.True);
        Assert.That(reg.Main.F, Is.EqualTo(0b0100_0000));
    }

    [Test]
    public void CheckSettingFlag5()
    {
        var reg = new Registers { Flag5 = true };
        Assert.That(reg.Flag5, Is.True);
        Assert.That(reg.Main.F, Is.EqualTo(0b0010_0000));
    }

    [Test]
    public void CheckSettingHalfCarryFlag()
    {
        var reg = new Registers { Hf = true };
        Assert.That(reg.Hf, Is.True);
        Assert.That(reg.Main.F, Is.EqualTo(0b0001_0000));
    }

    [Test]
    public void CheckSettingFlag3()
    {
        var reg = new Registers { Flag3 = true };
        Assert.That(reg.Flag3, Is.True);
        Assert.That(reg.Main.F, Is.EqualTo(0b0000_1000));
    }

    [Test]
    public void CheckSettingParityOverflowFlag()
    {
        var reg = new Registers { Pf = true };
        Assert.That(reg.Pf, Is.True);
        Assert.That(reg.Main.F, Is.EqualTo(0b0000_0100));
    }

    [Test]
    public void CheckSettingNegativeFlag()
    {
        var reg = new Registers { Nf = true };
        Assert.That(reg.Nf, Is.True);
        Assert.That(reg.Main.F, Is.EqualTo(0b0000_0010));
    }

    [Test]
    public void CheckSettingCarryFlag()
    {
        var reg = new Registers { Cf = true };
        Assert.That(reg.Cf, Is.True);
        Assert.That(reg.Main.F, Is.EqualTo(0b0000_0001));
    }

    [Test]
    public void CheckRegisterPairs()
    {
        var reg = new Registers();
        reg.Main.B = 0x12;
        reg.Main.C = 0x34;
        reg.Main.D = 0x56;
        reg.Main.E = 0x78;
        reg.Main.H = 0x9A;
        reg.Main.L = 0xBC;
        reg.Main.A = 0xDE;
        reg.Main.F = 0xF0;

        Assert.That(reg.Main.BC, Is.EqualTo(0x1234));
        Assert.That(reg.Main.DE, Is.EqualTo(0x5678));
        Assert.That(reg.Main.HL, Is.EqualTo(0x9ABC));
        Assert.That(reg.Main.AF, Is.EqualTo(0xDEF0));
    }

    [Test]
    public void CheckAlternateRegisterPairs()
    {
        var reg = new Registers();
        reg.Alt.B = 0x11;
        reg.Alt.C = 0x22;
        reg.Alt.D = 0x33;
        reg.Alt.E = 0x44;
        reg.Alt.H = 0x55;
        reg.Alt.L = 0x66;
        reg.Alt.A = 0x77;
        reg.Alt.F = 0x88;

        Assert.That(reg.Alt.BC, Is.EqualTo(0x1122));
        Assert.That(reg.Alt.DE, Is.EqualTo(0x3344));
        Assert.That(reg.Alt.HL, Is.EqualTo(0x5566));
        Assert.That(reg.Alt.AF, Is.EqualTo(0x7788));
    }

    [Test]
    public void CheckIndexRegisterHighLow()
    {
        var reg = new Registers { IX = 0x1234, IY = 0xABCD };

        Assert.That(reg.IXH, Is.EqualTo(0x12));
        Assert.That(reg.IXL, Is.EqualTo(0x34));
        Assert.That(reg.IYH, Is.EqualTo(0xAB));
        Assert.That(reg.IYL, Is.EqualTo(0xCD));

        reg.IXH = 0xFE;
        reg.IXL = 0xDC;
        reg.IYH = 0xBA;
        reg.IYL = 0x98;

        Assert.That(reg.IX, Is.EqualTo(0xFEDC));
        Assert.That(reg.IY, Is.EqualTo(0xBA98));
    }
}
