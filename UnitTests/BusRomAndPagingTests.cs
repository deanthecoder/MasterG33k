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

using DTC.Z80;
using DTC.Z80.Devices;

namespace UnitTests;

[TestFixture]
public sealed class BusRomAndPagingTests
{
    [Test]
    public void WritesToRomAreIgnored()
    {
        var memory = new Memory();
        var bus = new Bus(memory);
        var memoryController = new SmsMemoryController();
        var romData = new byte[0x4000];
        romData[0x0000] = 0x12;
        var rom = new SmsRomDevice(romData);
        memoryController.SetCartridge(rom);
        memoryController.Reset();

        bus.Attach(memoryController);

        Assert.That(bus.Read8(0x0000), Is.EqualTo(0x12));
        bus.Write8(0x0000, 0x34);
        Assert.That(bus.Read8(0x0000), Is.EqualTo(0x12));
    }

    [Test]
    public void WritesToMapperRegistersPageRomBanks()
    {
        var memory = new Memory();
        var bus = new Bus(memory);
        var memoryController = new SmsMemoryController();
        var romData = new byte[0x8000];
        romData[0x0400] = 0x11;
        romData[0x4000 + 0x0400] = 0x22;
        var rom = new SmsRomDevice(romData);
        memoryController.SetCartridge(rom);
        memoryController.Reset();

        bus.Attach(memoryController);
        bus.Attach(new SmsMapperDevice(memoryController, memory));

        Assert.That(bus.Read8(0x0400), Is.EqualTo(0x11));
        bus.Write8(0xFFFD, 0x01);
        Assert.That(bus.Read8(0x0400), Is.EqualTo(0x22));
    }
}
