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
public sealed class VdpRenderTests
{
    [Test]
    public void CheckBackgroundColorIndexZeroUsesPaletteEntry()
    {
        var vdp = new SmsVdp();
        vdp.Reset();

        // Mode 4 with display enabled; backdrop index 1.
        WriteRegister(vdp, 0, 0b0000_0100);
        WriteRegister(vdp, 1, 0b0100_0000);
        WriteRegister(vdp, 7, 0x01);

        // Palette 0 entry 0 = red (value 0x03 -> R=255).
        WriteCram(vdp, 0, 0x03);

        // Backdrop colour (sprite palette index 1) = blue (value 0x30 -> B=255).
        WriteCram(vdp, 16 + 1, 0x30);

        // Name table entry 0 -> tile index 0, palette 0, no flags.
        WriteVram(vdp, 0x3800, 0x00);
        WriteVram(vdp, 0x3801, 0x00);

        var frame = RenderFrame(vdp);

        Assert.That(frame[0], Is.EqualTo(255));
        Assert.That(frame[1], Is.Zero);
        Assert.That(frame[2], Is.Zero);
        Assert.That(frame[3], Is.EqualTo(255));
    }

    [Test]
    public void GivenDisplayDisabledCheckBackdropAndNoSprites()
    {
        var vdp = new SmsVdp();
        vdp.Reset();

        // Mode 4 with display disabled; backdrop index 2; sprite patterns at 0x0000.
        WriteRegister(vdp, 0, 0b0000_0100);
        WriteRegister(vdp, 1, 0b0000_0000);
        WriteRegister(vdp, 7, 0x02);
        WriteRegister(vdp, 6, 0x00);

        // Backdrop colour (sprite palette index 2) = green (value 0x0C -> G=255).
        WriteCram(vdp, 16 + 2, 0x0C);

        // Make tile 0, row 0, left-most pixel non-zero (color index 1).
        WriteVram(vdp, 0x0000, 0x80);
        WriteVram(vdp, 0x0001, 0x00);
        WriteVram(vdp, 0x0002, 0x00);
        WriteVram(vdp, 0x0003, 0x00);

        // Name table entry 0 -> tile index 0, palette 0, no flags.
        WriteVram(vdp, 0x3800, 0x00);
        WriteVram(vdp, 0x3801, 0x00);

        // Sprite 0 at (0,0), tile index 0.
        WriteVram(vdp, 0x3F00, 0x00);
        WriteVram(vdp, 0x3F80, 0x00);
        WriteVram(vdp, 0x3F81, 0x00);

        var frame = RenderFrame(vdp);

        Assert.That(frame[0], Is.Zero);
        Assert.That(frame[1], Is.EqualTo(255));
        Assert.That(frame[2], Is.Zero);
        Assert.That(frame[3], Is.EqualTo(255));
    }

    private static void WriteRegister(SmsVdp vdp, byte index, byte value)
    {
        vdp.WriteControl(value);
        vdp.WriteControl((byte)(0b1000_0000 | (index & 0x0F)));
    }

    private static void WriteVram(SmsVdp vdp, int address, byte value)
    {
        WriteControlWord(vdp, address, 0b01);
        vdp.WriteData(value);
    }

    private static void WriteCram(SmsVdp vdp, int address, byte value)
    {
        WriteControlWord(vdp, address, 0b11);
        vdp.WriteData(value);
    }

    private static void WriteControlWord(SmsVdp vdp, int address, int code)
    {
        var low = (byte)(address & 0xFF);
        var high = (byte)(((address >> 8) & 0x3F) | (code << 6));
        vdp.WriteControl(low);
        vdp.WriteControl(high);
    }

    private static byte[] RenderFrame(SmsVdp vdp)
    {
        byte[] frame = null;

        vdp.FrameRendered += Handler;
        vdp.AdvanceCycles(192 * 228);
        vdp.FrameRendered -= Handler;

        Assert.That(frame, Is.Not.Null);
        Assert.That(frame, Has.Length.EqualTo(SmsVdp.FrameWidth * SmsVdp.FrameHeight * 4));
        return frame;

        void Handler(object sender, byte[] buffer) => frame = buffer.ToArray();
    }
}
