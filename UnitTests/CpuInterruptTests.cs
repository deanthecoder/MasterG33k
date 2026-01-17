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
public sealed class CpuInterruptTests
{
    [Test]
    public void GivenIm2EnabledCheckInterruptFetchesVectorFromIRegister()
    {
        var bus = new Bus(new Memory());
        var cpu = new Cpu(bus);
        cpu.Reset();

        cpu.Reg.SP = 0xDFF0;
        cpu.Reg.PC = 0x0000;
        cpu.Reg.I = 0x12;
        cpu.Reg.IM = 2;
        cpu.Reg.IFF1 = true;
        cpu.Reg.IFF2 = true;

        // NOP at 0x0000, then interrupt should vector to 0x1234.
        cpu.MainMemory.Data[0x0000] = 0x00;
        var vectorAddr = (ushort)((cpu.Reg.I << 8) | 0xFF);
        cpu.MainMemory.Data[vectorAddr] = 0x34;
        cpu.MainMemory.Data[vectorAddr + 1] = 0x12;

        cpu.RequestInterrupt();
        cpu.Step();

        Assert.That(cpu.Reg.PC, Is.EqualTo(0x1234));
        Assert.That(cpu.Reg.IFF1, Is.False);
        Assert.That(cpu.Reg.IFF2, Is.False);
        Assert.That(cpu.Reg.SP, Is.EqualTo(0xDFF0 - 2));
        Assert.That(cpu.MainMemory.Data[cpu.Reg.SP], Is.EqualTo(0x01));
        Assert.That(cpu.MainMemory.Data[cpu.Reg.SP + 1], Is.EqualTo(0x00));
        Assert.That(cpu.Reg.R, Is.EqualTo(2));
    }

    [Test]
    public void GivenPendingNmiAndIrqCheckNmiOverridesMaskableInterrupts()
    {
        var bus = new Bus(new Memory());
        var cpu = new Cpu(bus);
        cpu.Reset();

        cpu.Reg.SP = 0xDFF0;
        cpu.Reg.PC = 0x0000;
        cpu.Reg.I = 0x7F;
        cpu.Reg.IM = 2;
        cpu.Reg.IFF1 = true;
        cpu.Reg.IFF2 = true;

        // NOP at 0x0000 so PC advances to 0x0001 before NMI.
        cpu.MainMemory.Data[0x0000] = 0x00;

        cpu.RequestInterrupt();
        cpu.RequestNmi();
        cpu.Step();

        Assert.That(cpu.Reg.PC, Is.EqualTo(0x0066));
        Assert.That(cpu.Reg.IFF1, Is.False);
        Assert.That(cpu.Reg.IFF2, Is.True);
        Assert.That(cpu.Reg.SP, Is.EqualTo(0xDFF0 - 2));
        Assert.That(cpu.MainMemory.Data[cpu.Reg.SP], Is.EqualTo(0x01));
        Assert.That(cpu.MainMemory.Data[cpu.Reg.SP + 1], Is.EqualTo(0x00));
        Assert.That(cpu.Reg.R, Is.EqualTo(2));
    }
}
