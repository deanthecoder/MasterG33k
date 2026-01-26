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
using SharpHook.Native;

using DTC.Emulation;

namespace UnitTests;

[TestFixture]
public sealed class JoypadAndPortDeviceTests
{
    [Test]
    public void CheckPauseFiresOnlyOnPress()
    {
        using var joypad = new SmsJoypad(startHook: false);
        var count = 0;
        joypad.PausePressed += (_, _) => count++;

        joypad.InjectKey(KeyCode.VcP, isPressed: true);
        joypad.InjectKey(KeyCode.VcP, isPressed: false);

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void CheckJoypadTracksButtonStates()
    {
        using var joypad = new SmsJoypad(startHook: false);

        joypad.InjectKey(KeyCode.VcUp, isPressed: true);
        joypad.InjectKey(KeyCode.VcDown, isPressed: true);
        joypad.InjectKey(KeyCode.VcLeft, isPressed: true);
        joypad.InjectKey(KeyCode.VcRight, isPressed: true);
        joypad.InjectKey(KeyCode.VcZ, isPressed: true);
        joypad.InjectKey(KeyCode.VcX, isPressed: true);

        var state = joypad.GetPressedButtons();
        Assert.That(state, Is.EqualTo(
            SmsJoypad.SmsJoypadButtons.Up |
            SmsJoypad.SmsJoypadButtons.Down |
            SmsJoypad.SmsJoypadButtons.Left |
            SmsJoypad.SmsJoypadButtons.Right |
            SmsJoypad.SmsJoypadButtons.Button1 |
            SmsJoypad.SmsJoypadButtons.Button2));

        joypad.InjectKey(KeyCode.VcUp, isPressed: false);
        joypad.InjectKey(KeyCode.VcZ, isPressed: false);

        state = joypad.GetPressedButtons();
        Assert.That(state.HasFlag(SmsJoypad.SmsJoypadButtons.Up), Is.False);
        Assert.That(state.HasFlag(SmsJoypad.SmsJoypadButtons.Button1), Is.False);
        Assert.That(state.HasFlag(SmsJoypad.SmsJoypadButtons.Down), Is.True);
        Assert.That(state.HasFlag(SmsJoypad.SmsJoypadButtons.Button2), Is.True);
    }

    [Test]
    public void GivenAllButtonsPressedCheckPortDeviceReturnsExpectedJoypadBytes()
    {
        using var joypad = new SmsJoypad(startHook: false);
        joypad.InjectKey(KeyCode.VcUp, isPressed: true);
        joypad.InjectKey(KeyCode.VcDown, isPressed: true);
        joypad.InjectKey(KeyCode.VcLeft, isPressed: true);
        joypad.InjectKey(KeyCode.VcRight, isPressed: true);
        joypad.InjectKey(KeyCode.VcZ, isPressed: true);
        joypad.InjectKey(KeyCode.VcX, isPressed: true);

        var portDevice = new SmsPortDevice(new SmsVdp(), joypad);

        Assert.That(portDevice.Read8(0xDC), Is.EqualTo(0xC0));
        Assert.That(portDevice.Read8(0xC0), Is.EqualTo(0xC0));
        Assert.That(portDevice.Read8(0xDD), Is.EqualTo(0xFF));
        Assert.That(portDevice.Read8(0xC1), Is.EqualTo(0xFF));
    }

    [Test]
    public void GivenUnknownPortsCheckPortDeviceFallsBack()
    {
        var fallback = new TestPortDevice();
        var portDevice = new SmsPortDevice(new SmsVdp(), fallback: fallback);

        Assert.That(portDevice.Read8(0xAA), Is.EqualTo(0x5A));

        portDevice.Write8(0xAA, 0x12);

        Assert.That(fallback.LastWritePort, Is.EqualTo(0xAA));
        Assert.That(fallback.LastWriteValue, Is.EqualTo(0x12));
    }

    [Test]
    public void CheckPortDeviceIgnoresJoypadWrites()
    {
        var fallback = new TestPortDevice();
        var portDevice = new SmsPortDevice(new SmsVdp(), fallback: fallback);

        portDevice.Write8(0xC0, 0x01);
        portDevice.Write8(0xC1, 0x02);
        portDevice.Write8(0xDC, 0x03);
        portDevice.Write8(0xDD, 0x04);

        Assert.That(fallback.WriteCount, Is.Zero);
    }

    [Test]
    public void GivenAudioControlPortReadCheckReportsNoFmHardware()
    {
        var portDevice = new SmsPortDevice(new SmsVdp());

        Assert.That(portDevice.Read8(0xF2), Is.EqualTo(0x02));
    }

    [Test]
    public void GivenAudioControlPortWriteCheckNoFmSignaturePersists()
    {
        var portDevice = new SmsPortDevice(new SmsVdp());

        portDevice.Write8(0xF2, 0x00);
        Assert.That(portDevice.Read8(0xF2), Is.EqualTo(0x02));

        portDevice.Write8(0xF2, 0x03);
        Assert.That(portDevice.Read8(0xF2), Is.EqualTo(0x02));
    }

    private sealed class TestPortDevice : IPortDevice
    {
        public int WriteCount { get; private set; }
        public ushort LastWritePort { get; private set; }
        public byte LastWriteValue { get; private set; }

        public byte Read8(ushort portAddress) => 0x5A;

        public void Write8(ushort portAddress, byte value)
        {
            WriteCount++;
            LastWritePort = portAddress;
            LastWriteValue = value;
        }
    }
}
