// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using System.Runtime.InteropServices;
using DTC.Emulation;
using DTC.Emulation.Snapshot;
using DTC.Z80.HostDevices;

namespace DTC.Z80.Devices;

/// <summary>
/// SN76489 PSG (SMS/GG).
/// </summary>
public sealed class SmsPsg : IAudioSource
{
    private const int FixedPointShift = 8;
    private const int NoAntiAlias = int.MinValue;
    private const int NoiseShiftReset = 0x8000;
    private const int NoiseFeedbackMask = 0x9;
    private const int DefaultSampleRateHz = 44100;
    private const int ToneChannelCount = 3;
    private const int ChannelCount = 4;
    private const int NoiseRegisterIndex = 6;
    private const int VolumeRegisterOffset = 1;

    // Approximate log volume table; higher indices are quieter.
    private static readonly int[] VolumeTable = GeneratePsgVolume();

    private readonly int[] m_registers = new int[8];
    private readonly int[] m_phaseCounter = new int[ChannelCount];
    private readonly int[] m_polarity = new int[ChannelCount];
    private readonly int[] m_antiAliasPosition = new int[ToneChannelCount];
    private readonly int[] m_channelOutput = new int[ChannelCount];
    private readonly bool[] m_channelEnabled = [true, true, true, true];
    private readonly SoundDevice m_audioSink;
    private int m_cpuClockHz;
    private readonly int m_sampleRate;
    private double m_ticksPerSample;
    private int m_clockIncrementScaled;
    private int m_clockRemainderScaled;
    private int m_latchedRegister;
    private int m_noisePeriod;
    private int m_noiseShiftRegister;
    private double m_ticksUntilNextSample;

    public SmsPsg(SoundDevice audioSink, int cpuClockHz, int sampleRate = DefaultSampleRateHz)
    {
        m_audioSink = audioSink ?? throw new ArgumentNullException(nameof(audioSink));
        m_cpuClockHz = cpuClockHz;
        m_sampleRate = sampleRate;
        UpdateTiming();
        Reset();
    }

    public int SampleRateHz => m_sampleRate;

    int IAudioSource.ChannelCount => ChannelCount;

    int IAudioSource.SampleRateHz => m_sampleRate;

    public void Reset()
    {
        UpdateTiming();

        m_latchedRegister = 0;
        m_clockRemainderScaled = 0;
        m_noiseShiftRegister = NoiseShiftReset;
        m_noisePeriod = 0x10;
        m_ticksUntilNextSample = m_ticksPerSample;

        Array.Clear(m_registers, 0, m_registers.Length);
        for (var channel = 0; channel < ChannelCount; channel++)
        {
            InitializeRegistersForChannel(channel);
            ResetChannelState(channel);
        }
    }

    public void SetCpuClockHz(int cpuClockHz)
    {
        if (m_cpuClockHz == cpuClockHz || cpuClockHz <= 0)
            return;

        m_cpuClockHz = cpuClockHz;
        UpdateTiming();
        if (m_ticksUntilNextSample > m_ticksPerSample)
            m_ticksUntilNextSample = m_ticksPerSample;
    }

    public void SetChannelEnabled(int channel, bool isEnabled)
    {
        if (channel is < 1 or > 4)
            return;

        m_channelEnabled[channel - 1] = isEnabled;
    }

    public void Write(byte value)
    {
        if (IsLatchByte(value))
            LatchRegister(value);
        else
            WriteLatchedRegister(value);

        ApplyRegisterSideEffects();
    }

    public void AdvanceT(long tStates)
    {
        if (tStates <= 0)
            return;

        m_ticksUntilNextSample -= tStates;
        while (m_ticksUntilNextSample <= 0.0)
        {
            GenerateSample();
            m_ticksUntilNextSample += m_ticksPerSample;
        }
    }

    private static bool IsLatchByte(byte value) => (value & 0x80) != 0;

    private static bool IsToneRegister(int registerIndex) => registerIndex is 0 or 2 or 4;

    private static int ToneRegisterIndex(int channel) => channel << 1;

    private static int VolumeRegisterIndex(int channel) => (channel << 1) + VolumeRegisterOffset;

    private void UpdateTiming()
    {
        m_ticksPerSample = (double)m_cpuClockHz / m_sampleRate;
        m_clockIncrementScaled = (m_cpuClockHz << FixedPointShift) / 16 / m_sampleRate;
    }

    private void InitializeRegistersForChannel(int channel)
    {
        var toneIndex = ToneRegisterIndex(channel);
        m_registers[toneIndex] = 1;
        m_registers[toneIndex + VolumeRegisterOffset] = 0x0F;
    }

    private void ResetChannelState(int channel)
    {
        m_phaseCounter[channel] = 0;
        m_polarity[channel] = 1;
        if (channel < ToneChannelCount)
            m_antiAliasPosition[channel] = NoAntiAlias;
    }

    private void LatchRegister(byte value)
    {
        m_latchedRegister = (value >> 4) & 7;
        m_registers[m_latchedRegister] = (m_registers[m_latchedRegister] & 0x3F0) | (value & 0x0F);
    }

    private void WriteLatchedRegister(byte value)
    {
        if (IsToneRegister(m_latchedRegister))
            m_registers[m_latchedRegister] = (m_registers[m_latchedRegister] & 0x0F) | ((value & 0x3F) << 4);
        else
            m_registers[m_latchedRegister] = value & 0x0F;
    }

    private void ApplyRegisterSideEffects()
    {
        switch (m_latchedRegister)
        {
            case 0:
            case 2:
            case 4:
                if (m_registers[m_latchedRegister] == 0)
                    m_registers[m_latchedRegister] = 1;
                break;
            case NoiseRegisterIndex:
                UpdateNoiseControl();
                break;
        }
    }

    private void UpdateNoiseControl()
    {
        m_noisePeriod = 0x10 << (m_registers[NoiseRegisterIndex] & 3);
        m_noiseShiftRegister = NoiseShiftReset;
    }

    private void GenerateSample()
    {
        RenderToneChannels();
        RenderNoiseChannel();
        OutputSample();

        AdvanceClock(out var clockCycles, out var clockCyclesScaled);
        ApplyClockToCounters(clockCycles);
        UpdateToneCounters(clockCycles, clockCyclesScaled);
        UpdateNoiseCounter(clockCycles);
    }

    private void RenderToneChannels()
    {
        for (var channel = 0; channel < ToneChannelCount; channel++)
        {
            if (!m_channelEnabled[channel])
            {
                m_channelOutput[channel] = 0;
                continue;
            }

            var toneRegister = m_registers[ToneRegisterIndex(channel)];
            var volume = GetChannelVolume(channel);
            if (IsToneAboveNyquist(toneRegister))
            {
                m_channelOutput[channel] = GetToneDcAverage(toneRegister, volume);
                continue;
            }

            if (m_antiAliasPosition[channel] != NoAntiAlias)
                m_channelOutput[channel] = (volume * m_antiAliasPosition[channel]) >> FixedPointShift;
            else
                m_channelOutput[channel] = volume * m_polarity[channel];
        }
    }

    private void RenderNoiseChannel()
    {
        if (m_channelEnabled[3])
            m_channelOutput[3] = GetChannelVolume(3) * ((m_noiseShiftRegister & 1) << 1);
        else
            m_channelOutput[3] = 0;
    }

    private void OutputSample()
    {
        var output = m_channelOutput[0] + m_channelOutput[1] + m_channelOutput[2] + m_channelOutput[3];
        output = Math.Clamp(output, -0x80, 0x7F);
        var normalized = Math.Clamp(output / 128.0, -1.0, 1.0);
        m_audioSink.AddSample(normalized, normalized);
    }

    private void AdvanceClock(out int clockCycles, out int clockCyclesScaled)
    {
        // Fixed-point accumulator for PSG clock cycles per sample.
        m_clockRemainderScaled += m_clockIncrementScaled;
        clockCycles = m_clockRemainderScaled >> FixedPointShift;
        clockCyclesScaled = clockCycles << FixedPointShift;
        m_clockRemainderScaled -= clockCyclesScaled;
    }

    private void ApplyClockToCounters(int clockCycles)
    {
        m_phaseCounter[0] -= clockCycles;
        m_phaseCounter[1] -= clockCycles;
        m_phaseCounter[2] -= clockCycles;

        if (m_noisePeriod == 0x80)
            m_phaseCounter[3] = m_phaseCounter[2];
        else
            m_phaseCounter[3] -= clockCycles;
    }

    private void UpdateToneCounters(int clockCycles, int clockCyclesScaled)
    {
        for (var channel = 0; channel < ToneChannelCount; channel++)
        {
            var counter = m_phaseCounter[channel];
            if (counter <= 0)
            {
                var tone = m_registers[ToneRegisterIndex(channel)];

                // Track sub-sample position for anti-aliasing when in audible range.
                if (tone > 6)
                {
                    var numerator = (clockCyclesScaled - m_clockRemainderScaled + (2 << FixedPointShift) * counter);
                    var position = (int)(((long)numerator << FixedPointShift) * m_polarity[channel]
                                         / (clockCyclesScaled + m_clockRemainderScaled));
                    m_antiAliasPosition[channel] = position;
                    m_polarity[channel] = -m_polarity[channel];
                }
                else
                {
                    m_polarity[channel] = 1;
                    m_antiAliasPosition[channel] = NoAntiAlias;
                }

                m_phaseCounter[channel] += tone * (clockCycles / tone + 1);
            }
            else
            {
                m_antiAliasPosition[channel] = NoAntiAlias;
            }
        }
    }

    private void UpdateNoiseCounter(int clockCycles)
    {
        if (m_phaseCounter[3] > 0)
            return;
        m_polarity[3] = -m_polarity[3];

        if (m_noisePeriod != 0x80)
            m_phaseCounter[3] += m_noisePeriod * (clockCycles / m_noisePeriod + 1);

        if (m_polarity[3] == 1)
            StepNoiseShiftRegister();
    }

    private void StepNoiseShiftRegister()
    {
        int feedback;
        if ((m_registers[NoiseRegisterIndex] & 0x04) != 0)
        {
            var pattern = m_noiseShiftRegister & NoiseFeedbackMask;
            feedback = pattern != 0 && (pattern ^ NoiseFeedbackMask) != 0 ? 1 : 0;
        }
        else
        {
            feedback = m_noiseShiftRegister & 1;
        }

        m_noiseShiftRegister = (m_noiseShiftRegister >> 1) | (feedback << 15);
    }

    private int GetChannelVolume(int channel) => VolumeTable[m_registers[VolumeRegisterIndex(channel)]];

    private bool IsToneAboveNyquist(int toneRegister)
    {
        if (toneRegister <= 0)
            return true;

        const double divider = 32.0;
        var frequency = m_cpuClockHz / (divider * toneRegister);

        // SN76489 tone frequency is cpuClock / (32 * tone). If at/above Nyquist, treat as DC.
        return frequency >= m_sampleRate * 0.5;
    }

    private static int GetToneDcAverage(int toneRegister, int volume) =>
        toneRegister <= 6 ? volume : 0;

    private static int[] GeneratePsgVolume()
    {
        const int steps = 16;
        const double max = 25.0;
        const double falloff = 0.80;

        var result = new int[steps];
        var value = max;

        for (var i = 0; i < steps; ++i)
        {
            result[i] = (int)Math.Round(value);
            value *= falloff;
        }

        result[^1] = 0;

        return result;
    }

    internal int GetStateSize() =>
        sizeof(int) * (m_registers.Length + m_phaseCounter.Length + m_polarity.Length + m_antiAliasPosition.Length + m_channelOutput.Length) +
        sizeof(int) * 4 + // m_latchedRegister, m_noisePeriod, m_noiseShiftRegister, m_cpuClockHz.
        sizeof(int) * 2 + // m_clockIncrementScaled, m_clockRemainderScaled.
        sizeof(double) * 2; // m_ticksUntilNextSample, m_ticksPerSample.

    internal void SaveState(ref StateWriter writer)
    {
        writer.WriteBytes(MemoryMarshal.AsBytes(m_registers.AsSpan()));
        writer.WriteBytes(MemoryMarshal.AsBytes(m_phaseCounter.AsSpan()));
        writer.WriteBytes(MemoryMarshal.AsBytes(m_polarity.AsSpan()));
        writer.WriteBytes(MemoryMarshal.AsBytes(m_antiAliasPosition.AsSpan()));
        writer.WriteBytes(MemoryMarshal.AsBytes(m_channelOutput.AsSpan()));

        writer.WriteInt32(m_latchedRegister);
        writer.WriteInt32(m_noisePeriod);
        writer.WriteInt32(m_noiseShiftRegister);
        writer.WriteInt32(m_cpuClockHz);
        writer.WriteInt32(m_clockIncrementScaled);
        writer.WriteInt32(m_clockRemainderScaled);
        writer.WriteDouble(m_ticksUntilNextSample);
        writer.WriteDouble(m_ticksPerSample);
    }

    internal void LoadState(ref StateReader reader)
    {
        reader.ReadBytes(MemoryMarshal.AsBytes(m_registers.AsSpan()));
        reader.ReadBytes(MemoryMarshal.AsBytes(m_phaseCounter.AsSpan()));
        reader.ReadBytes(MemoryMarshal.AsBytes(m_polarity.AsSpan()));
        reader.ReadBytes(MemoryMarshal.AsBytes(m_antiAliasPosition.AsSpan()));
        reader.ReadBytes(MemoryMarshal.AsBytes(m_channelOutput.AsSpan()));

        m_latchedRegister = reader.ReadInt32();
        m_noisePeriod = reader.ReadInt32();
        m_noiseShiftRegister = reader.ReadInt32();
        m_cpuClockHz = reader.ReadInt32();
        m_clockIncrementScaled = reader.ReadInt32();
        m_clockRemainderScaled = reader.ReadInt32();
        m_ticksUntilNextSample = reader.ReadDouble();
        m_ticksPerSample = reader.ReadDouble();
    }
}

