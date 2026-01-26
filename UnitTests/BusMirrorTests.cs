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
using DTC.Emulation;
using DTC.Emulation.Devices;
using DTC.Z80.Devices;

namespace UnitTests;

[TestFixture]
public sealed class BusMirrorTests
{
    [Test]
    public void CheckReadsFromMirrorReturnRamContent()
    {
        var memory = new Memory();
        var bus = new Bus(memory);
        bus.Attach(new SmsRamMirrorDevice(bus.MainMemory));

        memory.Data[0xC000] = 0x22;
        memory.Data[0xE000] = 0x11;

        Assert.That(bus.Read8(0xE000), Is.EqualTo(0x22));
    }

    [Test]
    public void CheckWritesToMirrorUpdateBaseRam()
    {
        var bus = new Bus(new Memory());
        bus.Attach(new SmsRamMirrorDevice(bus.MainMemory));

        bus.Write8(0xE123, 0x34);

        Assert.That(bus.Read8(0xC123), Is.EqualTo(0x34));
    }

    [Test]
    public void CheckMirrorUpperBoundMapsToRamEnd()
    {
        var bus = new Bus(new Memory());
        bus.Attach(new SmsRamMirrorDevice(bus.MainMemory));

        bus.Write8(0xFFFB, 0xAB);

        Assert.That(bus.Read8(0xDFFB), Is.EqualTo(0xAB));
    }
}
