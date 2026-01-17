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
using System.Diagnostics;
using System.Text;
using CSharp.Core.Extensions;
using CSharp.Core.UnitTesting;
using DTC.Z80;
using DTC.Z80.Devices;

namespace UnitTests;

[TestFixture]
public sealed class ZexallSmsTests : TestsBase
{
    private const double SmsCpuHz = 3_579_545;
    private const int EmulatedSeconds = 30_000;

    [Test, Explicit]
    public void RunTests()
    {
        var romFile = ProjectDir.Parent.GetDir("external").GetFile("zexdoc.sms");
        Assert.That(romFile, Does.Exist);
        var romData = romFile.ReadAllBytes();

        var vdp = new SmsVdp();
        var memoryController = new SmsMemoryController();
        var romDevice = new SmsRomDevice(romData);
        memoryController.SetCartridge(romDevice, forceEnabled: false);
        memoryController.SetBios(null);
        memoryController.Reset();

        var debugConsole = new DebugConsolePortDevice();
        var portDevice = new SmsPortDevice(vdp, joypad: null, memoryController, fallback: debugConsole);
        var bus = new Bus(new Memory(), portDevice);
        bus.Attach(memoryController);
        bus.Attach(new SmsMapperDevice(memoryController, bus.MainMemory));

        var cpu = new Cpu(bus);
        Array.Clear(cpu.MainMemory.Data, 0, cpu.MainMemory.Data.Length);
        cpu.Reset();
        vdp.Reset();

        const long maxTStates = (long)(SmsCpuHz * EmulatedSeconds);
        var lastTStates = cpu.TStatesSinceCpuStart;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (cpu.TStatesSinceCpuStart < maxTStates)
            {
                cpu.Step();
                var current = cpu.TStatesSinceCpuStart;
                var delta = current - lastTStates;
                if (delta > 0)
                    vdp.AdvanceCycles(delta);
                lastTStates = current;
                if (vdp.TryConsumeInterrupt())
                    cpu.RequestInterrupt();
            }
        }
        finally
        {
            stopwatch.Stop();
            LogSpeedMultiplier(cpu.TStatesSinceCpuStart, stopwatch.Elapsed);
            debugConsole.FlushToConsole("ZEXALL debug output");
        }

        Assert.That(debugConsole.OutputLength, Is.GreaterThan(0),
            "No debug console output captured. Ensure the emulator captures ZEXALL debug output ports.");
    }

    private sealed class DebugConsolePortDevice : IPortDevice
    {
        private readonly IPortDevice m_fallback;
        private readonly StringBuilder m_output = new();
        private readonly HashSet<byte> m_ports = new();

        public DebugConsolePortDevice(IPortDevice fallback = null)
        {
            m_fallback = fallback ?? DefaultPortDevice.Instance;
        }

        public int OutputLength => m_output.Length;

        public byte Read8(ushort portAddress) => m_fallback.Read8(portAddress);

        public void Write8(ushort portAddress, byte value)
        {
            m_fallback.Write8(portAddress, value);
            var port = (byte)portAddress;
            m_ports.Add(port);

            if (value == 0x0A || value == 0x0D)
            {
                m_output.Append('\n');
                return;
            }

            if (IsPrintableAscii(value))
                m_output.Append((char)value);
        }

        public void FlushToConsole(string title)
        {
            var output = m_output.ToString().TrimEnd();
            if (output.Length > 0)
            {
                Console.WriteLine($"{title}:");
                Console.WriteLine(output);
                return;
            }

            var portsList = m_ports.Count == 0
                ? "<none>"
                : string.Join(", ", m_ports.OrderBy(p => p).Select(p => $"0x{p:X2}"));
            Console.WriteLine($"{title}: <no printable ASCII output captured> Ports seen: {portsList}");
        }

        private static bool IsPrintableAscii(byte value) => value >= 0x20 && value <= 0x7E;
    }

    private static void LogSpeedMultiplier(long tStates, TimeSpan elapsed)
    {
        var emulatedSeconds = tStates / SmsCpuHz;
        var realSeconds = elapsed.TotalSeconds;
        if (realSeconds <= 0)
        {
            Console.WriteLine("Average speed: <elapsed time too small to measure>");
            return;
        }

        var multiplier = emulatedSeconds / realSeconds;
        Console.WriteLine($"Average speed: {multiplier:0.00}x real time (emulated {emulatedSeconds:0.00}s in {realSeconds:0.00}s)");
    }
}
