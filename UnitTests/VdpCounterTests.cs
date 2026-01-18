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
using DTC.Z80.Devices;

namespace UnitTests;

[TestFixture]
public sealed class VdpCounterTests
{
    [Test]
    public void CheckReadHCounterReturnsScanlineProgress()
    {
        var vdp = new SmsVdp();
        vdp.Reset();

        Assert.That(vdp.ReadHCounter(), Is.Zero);

        vdp.AdvanceCycles(114);

        Assert.That(vdp.ReadHCounter(), Is.Zero);

        vdp.LatchHCounter();

        Assert.That(vdp.ReadHCounter(), Is.EqualTo(128));
    }

    [Test]
    public void GivenLineInterruptEnabledCheckReloadsAndSignalsAtCounterZero()
    {
        var vdp = new SmsVdp();
        vdp.Reset();

        WriteRegister(vdp, 0, 0b0001_0000);
        WriteRegister(vdp, 10, 2);

        vdp.AdvanceCycles(228);
        Assert.That(vdp.TryConsumeInterrupt(), Is.False);

        vdp.AdvanceCycles(228);
        Assert.That(vdp.TryConsumeInterrupt(), Is.True);

        vdp.AdvanceCycles(228);
        vdp.AdvanceCycles(228);
        Assert.That(vdp.TryConsumeInterrupt(), Is.True);
    }

    private static void WriteRegister(SmsVdp vdp, byte index, byte value)
    {
        vdp.WriteControl(value);
        vdp.WriteControl((byte)(0b1000_0000 | (index & 0x0F)));
    }
}
