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
using DTC.Emulation.Devices;
using DTC.Z80.Devices;

namespace UnitTests;

[TestFixture]
public sealed class MemoryMapperTests
{
    [Test]
    public void CheckMapperRegistersMirrorToRam()
    {
        var memory = new Memory();
        var controller = new SmsMemoryController();
        var mapper = new SmsMapperDevice(controller, memory);

        mapper.Write8(0xFFFC, 0x5A);

        Assert.That(memory.Read8(0xDFFC), Is.EqualTo(0x5A));
        Assert.That(mapper.Read8(0xFFFC), Is.EqualTo(0x5A));

        memory.Write8(0xDFFD, 0x3C);
        Assert.That(mapper.Read8(0xFFFD), Is.EqualTo(0x3C));
    }

    [Test]
    public void GivenRamMirrorWriteCheckMapperRegistersUnchanged()
    {
        var cartData = CreateBankedRom(0x8000, 0x11, 0x22);

        var controller = new SmsMemoryController();
        var cartRom = new SmsRomDevice(cartData);
        controller.SetCartridge(cartRom, forceEnabled: true);
        controller.Reset();

        var memory = new Memory();
        var mapper = new SmsMapperDevice(controller, memory);

        Assert.That(cartRom.Bank1, Is.EqualTo(1));

        memory.Write8(0xDFFE, 0x00);
        Assert.That(cartRom.Bank1, Is.EqualTo(1));
        Assert.That(mapper.Read8(0xFFFE), Is.EqualTo(0x00));

        mapper.Write8(0xFFFE, 0x00);
        Assert.That(cartRom.Bank1, Is.EqualTo(0));
    }

    [Test]
    public void GivenBiosEnabledCheckMapperTargetsBios()
    {
        var biosData = CreateBankedRom(0x8000, 0x10, 0x33);
        var cartData = CreateBankedRom(0x8000, 0x11, 0x22);

        var controller = new SmsMemoryController();
        var cartRom = new SmsRomDevice(cartData);
        var biosRom = new SmsRomDevice(biosData);
        controller.SetBiosRom(biosRom);
        controller.SetCartridge(cartRom, forceEnabled: true);
        controller.Reset();

        var mapper = new SmsMapperDevice(controller, new Memory());

        Assert.That(cartRom.Bank1, Is.EqualTo(1));
        Assert.That(biosRom.Bank1, Is.EqualTo(1));

        mapper.Write8(0xFFFE, 0x00);

        Assert.That(cartRom.Bank1, Is.EqualTo(1));
        Assert.That(biosRom.Bank1, Is.EqualTo(0));
    }

    [Test]
    public void GivenBiosOnlyCheckMapperTargetsBios()
    {
        var biosData = CreateBankedRom(0x8000, 0x12, 0x34);

        var controller = new SmsMemoryController();
        var biosRom = new SmsRomDevice(biosData);
        controller.SetBiosRom(biosRom);
        controller.Reset();

        var mapper = new SmsMapperDevice(controller, new Memory());

        Assert.That(biosRom.Bank1, Is.EqualTo(1));

        mapper.Write8(0xFFFE, 0x00);

        Assert.That(biosRom.Bank1, Is.EqualTo(0));
    }
    
    private static byte[] CreateBankedRom(int size, byte bank0, byte bank1)
    {
        var data = new byte[size];
        Array.Fill(data, bank0, 0, 0x4000);
        if (size >= 0x8000)
            Array.Fill(data, bank1, 0x4000, 0x4000);
        return data;
    }
}
