// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using DTC.Z80.HostDevices;

namespace DTC.Z80.Devices;

/// <summary>
/// Sound chip: SN76489 PSG (SMS/GG).
/// </summary>
public sealed class SmsPsg
{
    private const int Scale = 8;
    private const int NoAntialias = int.MinValue;
    private const int ShiftReset = 0x8000;
    private const int FeedbackPattern = 0x9;
    private const int DefaultSampleRateHz = 44100;

    // Tests with an SMS and a TV card found the highest three volume levels to be clipped.
    private static readonly int[] PsgVolume =
    [
        25, 20, 16, 13, 10, 8, 6, 5, 4, 3, 3, 2, 2, 1, 1, 0
    ];

    private readonly int[] m_reg = new int[8];
    private readonly int[] m_freqCounter = new int[4];
    private readonly int[] m_freqPolarity = new int[4];
    private readonly int[] m_freqPos = new int[3];
    private readonly int[] m_outputChannel = new int[4];
    private readonly bool[] m_channelEnabled = [true, true, true, true];
    private readonly SoundDevice m_audioSink;
    private int m_cpuClockHz;
    private readonly int m_sampleRate;
    private double m_ticksPerSample;
    private int m_clock;
    private int m_clockFrac;
    private int m_regLatch;
    private int m_noiseFreq;
    private int m_noiseShiftReg;
    private double m_ticksUntilSample;

    public SmsPsg(SoundDevice audioSink, int cpuClockHz, int sampleRate = DefaultSampleRateHz)
    {
        m_audioSink = audioSink ?? throw new ArgumentNullException(nameof(audioSink));
        m_cpuClockHz = cpuClockHz;
        m_sampleRate = sampleRate;
        m_ticksPerSample = (double)m_cpuClockHz / m_sampleRate;
        Reset();
    }

    public void Reset()
    {
        m_clock = (m_cpuClockHz << Scale) / 16 / m_sampleRate;
        m_regLatch = 0;
        m_clockFrac = 0;
        m_noiseShiftReg = ShiftReset;
        m_noiseFreq = 0x10;
        m_ticksUntilSample = m_ticksPerSample;

        Array.Clear(m_reg, 0, m_reg.Length);
        for (var i = 0; i < 4; i++)
        {
            // Set tone frequency (non-zero).
            m_reg[i << 1] = 1;

            // Set volume off.
            m_reg[(i << 1) + 1] = 0x0F;
            m_freqCounter[i] = 0;
            m_freqPolarity[i] = 1;
            if (i != 3)
                m_freqPos[i] = NoAntialias;
        }
    }

    public void SetChannelEnabled(int channel, bool isEnabled)
    {
        if (channel is < 1 or > 4)
            return;

        m_channelEnabled[channel - 1] = isEnabled;
    }

    public void SetCpuClockHz(int cpuClockHz)
    {
        if (m_cpuClockHz == cpuClockHz || cpuClockHz <= 0)
            return;

        m_cpuClockHz = cpuClockHz;
        m_ticksPerSample = (double)m_cpuClockHz / m_sampleRate;
        m_clock = (m_cpuClockHz << Scale) / 16 / m_sampleRate;
        if (m_ticksUntilSample > m_ticksPerSample)
            m_ticksUntilSample = m_ticksPerSample;
    }

    public void Write(byte value)
    {
        // If bit 7 is 1 then the byte is a LATCH/DATA byte.
        if ((value & 0x80) != 0)
        {
            // Bits 6 and 5 ("cc") give the channel to be latched.
            // Bit 4 ("t") determines whether to latch volume (1) or tone/noise (0).
            m_regLatch = (value >> 4) & 7;

            // Zero lower 4 bits of register and mask new value.
            m_reg[m_regLatch] = (m_reg[m_regLatch] & 0x3F0) | (value & 0x0F);
        }
        else
        {
            // If the currently latched register is a tone register then the low 6
            // bits of the byte are placed into the high 6 bits of the latched register.
            if (m_regLatch == 0 || m_regLatch == 2 || m_regLatch == 4)
                m_reg[m_regLatch] = (m_reg[m_regLatch] & 0x0F) | ((value & 0x3F) << 4);
            else
                m_reg[m_regLatch] = value & 0x0F;
        }

        switch (m_regLatch)
        {
            case 0:
            case 2:
            case 4:
                if (m_reg[m_regLatch] == 0)
                    m_reg[m_regLatch] = 1;
                break;
            case 6:
                m_noiseFreq = 0x10 << (m_reg[6] & 3);
                m_noiseShiftReg = ShiftReset;
                break;
        }
    }

    public void AdvanceT(long tStates)
    {
        if (tStates <= 0)
            return;

        m_ticksUntilSample -= tStates;
        while (m_ticksUntilSample <= 0.0)
        {
            GenerateSample();
            m_ticksUntilSample += m_ticksPerSample;
        }
    }

    private void GenerateSample()
    {
        for (var i = 0; i < 3; i++)
        {
            if (!m_channelEnabled[i])
            {
                m_outputChannel[i] = 0;
                continue;
            }

            if (IsToneAboveNyquist(m_reg[i << 1]))
            {
                m_outputChannel[i] = GetToneDcAverage(i, m_reg[i << 1]);
                continue;
            }

            if (m_freqPos[i] != NoAntialias)
                m_outputChannel[i] = (PsgVolume[m_reg[(i << 1) + 1]] * m_freqPos[i]) >> Scale;
            else
                m_outputChannel[i] = PsgVolume[m_reg[(i << 1) + 1]] * m_freqPolarity[i];
        }

        if (m_channelEnabled[3])
            m_outputChannel[3] = PsgVolume[m_reg[7]] * ((m_noiseShiftReg & 1) << 1);
        else
            m_outputChannel[3] = 0;

        var output = m_outputChannel[0] + m_outputChannel[1] + m_outputChannel[2] + m_outputChannel[3];
        output = Math.Clamp(output, -0x80, 0x7F);
        var normalized = Math.Clamp(output / 128.0, -1.0, 1.0);
        m_audioSink.AddSample(normalized, normalized);

        m_clockFrac += m_clock;
        var clockCycles = m_clockFrac >> Scale;
        var clockCyclesScaled = clockCycles << Scale;
        m_clockFrac -= clockCyclesScaled;

        m_freqCounter[0] -= clockCycles;
        m_freqCounter[1] -= clockCycles;
        m_freqCounter[2] -= clockCycles;

        if (m_noiseFreq == 0x80)
            m_freqCounter[3] = m_freqCounter[2];
        else
            m_freqCounter[3] -= clockCycles;

        for (var i = 0; i < 3; i++)
        {
            var counter = m_freqCounter[i];
            if (counter <= 0)
            {
                var tone = m_reg[i << 1];
                if (tone > 6)
                {
                    var numerator = clockCyclesScaled - m_clockFrac + (2 << Scale) * counter;
                    var position = (int)(((long)numerator << Scale) * m_freqPolarity[i] / (clockCyclesScaled + m_clockFrac));
                    m_freqPos[i] = position;
                    m_freqPolarity[i] = -m_freqPolarity[i];
                }
                else
                {
                    m_freqPolarity[i] = 1;
                    m_freqPos[i] = NoAntialias;
                }

                m_freqCounter[i] += tone * (clockCycles / tone + 1);
            }
            else
            {
                m_freqPos[i] = NoAntialias;
            }
        }

        if (m_freqCounter[3] > 0)
            return;
        m_freqPolarity[3] = -m_freqPolarity[3];

        if (m_noiseFreq != 0x80)
            m_freqCounter[3] += m_noiseFreq * (clockCycles / m_noiseFreq + 1);

        if (m_freqPolarity[3] != 1)
            return;
        int feedback;
        if ((m_reg[6] & 0x04) != 0)
        {
            var pattern = m_noiseShiftReg & FeedbackPattern;
            feedback = pattern != 0 && (pattern ^ FeedbackPattern) != 0 ? 1 : 0;
        }
        else
        {
            feedback = m_noiseShiftReg & 1;
        }

        m_noiseShiftReg = (m_noiseShiftReg >> 1) | (feedback << 15);
    }

    private bool IsToneAboveNyquist(int toneRegister)
    {
        if (toneRegister <= 0)
            return true;

        const double divider = 32.0;
        var frequency = m_cpuClockHz / (divider * toneRegister);
        return frequency >= m_sampleRate * 0.5;
    }

    private int GetToneDcAverage(int channel, int toneRegister)
    {
        // Tone <= 6 is treated as a constant high output on real hardware.
        if (toneRegister <= 6)
            return PsgVolume[m_reg[(channel << 1) + 1]];

        // SN76489 tone is 50% duty, so the DC average is zero.
        return 0;
    }
}
