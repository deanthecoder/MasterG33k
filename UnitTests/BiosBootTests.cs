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
using DTC.Core.Extensions;
using DTC.Core.UnitTesting;
using DTC.Z80;
using DTC.Z80.Devices;

namespace UnitTests;

[TestFixture]
public sealed class BiosBootTests : TestsBase
{
    private const double SmsCpuHz = 3_579_545;
    private const long SegaScreenTicks = 10_139_144;
    private const long SoftwareErrorTicks = 38_692_953;
    private const uint SegaScreenChecksum = 0x68DB2604;
    private const uint SoftwareErrorChecksum = 0x438F5B9C;
    private const long SoftwareErrorWindowStart = 30_000_000;

    [Test]
    public void GivenBiosAvailableCheckBootsToSegaScreen()
    {
        var biosFile = ResolveBiosFile();
        Assume.That(biosFile, Does.Exist, "Set MASTERG33K_BIOS_PATH or copy a BIOS .sms into MasterG33k/BIOS.");

        var biosData = biosFile.ReadAllBytes();
        var romInfo = RomHeaderInfo.FromRom(biosData);
        var harness = new BiosHarness(biosData, SoftwareErrorWindowStart);
        var frame = harness.RunUntilTicks(SegaScreenTicks);

        Assert.That(frame.HasValue, Is.True, "No frame rendered before target ticks.");
        Assert.That(frame.Value.Checksum, Is.EqualTo(SegaScreenChecksum),
            $"Unexpected SEGA checksum at {frame.Value.Ticks} ticks. {romInfo.ToSummary()} {harness.GetDebugSummary()}");
    }

    [Test]
    public void GivenBiosAvailableCheckDoesNotShowSoftwareError()
    {
        var biosFile = ResolveBiosFile();
        Assume.That(biosFile, Does.Exist, "Set MASTERG33K_BIOS_PATH or copy a BIOS .sms into MasterG33k/BIOS.");

        var biosData = biosFile.ReadAllBytes();
        var romInfo = RomHeaderInfo.FromRom(biosData);
        var harness = new BiosHarness(biosData, SoftwareErrorWindowStart);
        var frame = harness.RunUntilTicks(SoftwareErrorTicks);

        Assert.That(frame.HasValue, Is.True, "No frame rendered before target ticks.");
        Assert.That(harness.SawSoftwareError, Is.False, $"Software Error frame detected. {romInfo.ToSummary()} {harness.GetDebugSummary()}");
        Assert.That(frame.Value.Checksum, Is.Not.EqualTo(SoftwareErrorChecksum),
            $"Last frame matches Software Error checksum at {frame.Value.Ticks} ticks. {romInfo.ToSummary()} {harness.GetDebugSummary()}");
    }

    private static FileInfo ResolveBiosFile()
    {
        var biosDir = ProjectDir.Parent.GetDir("MasterG33k").GetDir("BIOS");
        return biosDir.Exists ? biosDir.EnumerateFiles("*.sms").FirstOrDefault() : null;
    }

    private sealed class BiosHarness
    {
        private readonly Cpu m_cpu;
        private readonly SmsVdp m_vdp;
        private readonly SmsMemoryController m_memoryController;
        private readonly long m_errorWindowStart;
        private uint m_lastChecksum;
        private long m_lastTicks;
        private bool m_hasFrame;
        private bool m_sawError;

        public BiosHarness(byte[] biosData, long errorWindowStart)
        {
            m_errorWindowStart = errorWindowStart;
            m_vdp = new SmsVdp();
            m_memoryController = new SmsMemoryController();

            var portDevice = new SmsPortDevice(m_vdp, joypad: null, m_memoryController);
            var bus = new Bus(new Memory(), portDevice);
            bus.Attach(m_memoryController);
            bus.Attach(new SmsMapperDevice(m_memoryController, bus.MainMemory));

            if (biosData.Length > 0x4000)
            {
                SmsRomChecksum.TryPatchChecksum(biosData, patchBothFields: true);
                var romDevice = new SmsRomDevice(biosData);
                m_memoryController.SetCartridge(romDevice, forceEnabled: false);
                m_memoryController.SetBiosRom(romDevice);
            }
            else
            {
                m_memoryController.SetBios(biosData);
            }

            m_memoryController.Reset();
            m_cpu = new Cpu(bus);
            Array.Clear(m_cpu.MainMemory.Data, 0, m_cpu.MainMemory.Data.Length);
            m_cpu.Reset();
            m_vdp.Reset();

            m_vdp.FrameRendered += OnFrameRendered;
        }

        public bool SawSoftwareError => m_sawError;

        public FrameSample? RunUntilTicks(long targetTicks)
        {
            var lastTStates = m_cpu.TStatesSinceCpuStart;
            const long maxTStates = (long)(SmsCpuHz * 60);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                while (m_cpu.TStatesSinceCpuStart < targetTicks && m_cpu.TStatesSinceCpuStart < maxTStates)
                {
                    m_cpu.Step();
                    var current = m_cpu.TStatesSinceCpuStart;
                    var delta = current - lastTStates;
                    if (delta > 0)
                        m_vdp.AdvanceCycles(delta);
                    lastTStates = current;
                    if (m_vdp.TryConsumeInterrupt())
                        m_cpu.RequestInterrupt();
                }
            }
            finally
            {
                stopwatch.Stop();
            }

            if (!m_hasFrame)
                return null;

            return new FrameSample(m_lastChecksum, m_lastTicks);
        }

        public string GetDebugSummary()
        {
            var bank0 = m_cpu.MainMemory.Read8(0xFFFD);
            var bank1 = m_cpu.MainMemory.Read8(0xFFFE);
            var bank2 = m_cpu.MainMemory.Read8(0xFFFF);
            return $"MemCtrl=0x{m_memoryController.Control:X2} BIOS={(m_memoryController.IsBiosEnabled ? "on" : "off")} CART={(m_memoryController.IsCartridgeEnabled ? "on" : "off")} BankRegs=[{bank0:X2},{bank1:X2},{bank2:X2}]";
        }

        private void OnFrameRendered(object sender, byte[] frameBuffer)
        {
            if (frameBuffer == null || frameBuffer.Length == 0)
                return;

            var checksum = ComputeFrameChecksum(frameBuffer);
            var ticks = m_cpu.TStatesSinceCpuStart;
            m_lastChecksum = checksum;
            m_lastTicks = ticks;
            m_hasFrame = true;
            if (ticks >= m_errorWindowStart && checksum == SoftwareErrorChecksum)
                m_sawError = true;
        }
    }

    private readonly record struct FrameSample(uint Checksum, long Ticks);

    private readonly record struct RomHeaderInfo(int HeaderOffset, ushort HeaderChecksumA, ushort HeaderChecksumB, byte RegionNibble, byte SizeNibble, ushort ComputedChecksum)
    {
        public bool HasHeader => HeaderOffset >= 0;

        public static RomHeaderInfo FromRom(byte[] romData)
        {
            if (romData == null || romData.Length < 0x2000)
                return new RomHeaderInfo(-1, 0, 0, 0, 0, 0);

            var signature = "TMR SEGA"u8.ToArray();
            var offsets = new[] { 0x7FF0, 0x3FF0, 0x1FF0 };
            foreach (var offset in offsets)
            {
                if (offset + 16 > romData.Length)
                    continue;
                var match = true;
                for (var i = 0; i < signature.Length; i++)
                {
                    if (romData[offset + i] != signature[i])
                    {
                        match = false;
                        break;
                    }
                }

                if (!match)
                    continue;

                var checksumA = (ushort)(romData[offset + 0x08] | (romData[offset + 0x09] << 8));
                var checksumB = (ushort)(romData[offset + 0x0A] | (romData[offset + 0x0B] << 8));
                var regionAndSize = romData[offset + 0x0F];
                var region = (byte)((regionAndSize >> 4) & 0x0F);
                var size = (byte)(regionAndSize & 0x0F);
                var computed = ComputeBiosChecksum(romData, size);
                return new RomHeaderInfo(offset, checksumA, checksumB, region, size, computed);
            }

            return new RomHeaderInfo(-1, 0, 0, 0, 0, 0);
        }

        public string ToSummary()
        {
            if (!HasHeader)
                return "Header: not found at 0x7FF0/0x3FF0/0x1FF0.";
            return $"Header @0x{HeaderOffset:X4}, region=0x{RegionNibble:X1}, size=0x{SizeNibble:X1}, checksumA=0x{HeaderChecksumA:X4}, checksumB=0x{HeaderChecksumB:X4}, computed=0x{ComputedChecksum:X4}.";
        }
    }

    private static ushort ComputeBiosChecksum(byte[] romData, byte sizeNibble)
    {
        var ranges = sizeNibble switch
        {
            0xA => new[] { (0x0000, 0x1FEF) },
            0xB => new[] { (0x0000, 0x3FEF) },
            0xC => new[] { (0x0000, 0x7FEF) },
            0xD => new[] { (0x0000, 0xBFEF) },
            0xE => new[] { (0x0000, 0x7FEF), (0x8000, 0x0FFFF) },
            0xF => new[] { (0x0000, 0x7FEF), (0x8000, 0x1FFFF) },
            0x0 => new[] { (0x0000, 0x7FEF), (0x8000, 0x3FFFF) },
            0x1 => new[] { (0x0000, 0x7FEF), (0x8000, 0x7FFFF) },
            0x2 => new[] { (0x0000, 0x7FEF), (0x8000, 0xFFFFF) },
            _ => Array.Empty<(int start, int end)>()
        };

        var sum = 0;
        foreach (var (start, end) in ranges)
        {
            var cappedEnd = Math.Min(end, romData.Length - 1);
            for (var i = start; i <= cappedEnd; i++)
                sum = (sum + romData[i]) & 0xFFFF;
        }

        return (ushort)sum;
    }

    private static uint ComputeFrameChecksum(byte[] frameBuffer)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        for (var i = 0; i < frameBuffer.Length; i++)
        {
            hash ^= frameBuffer[i];
            hash *= prime;
        }

        return hash;
    }
}
