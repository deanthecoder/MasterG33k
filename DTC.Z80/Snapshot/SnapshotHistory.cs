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
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DTC.Core.ViewModels;
using DTC.Z80.Devices;

namespace DTC.Z80.Snapshot;

/// <summary>
/// Periodically snapshots machine state to support time travel.
/// </summary>
public sealed class SnapshotHistory : ViewModelBase
{
    private const int MaxSamples = 30;
    private readonly ISnapshotHost m_host;
    private readonly MachineState[] m_states = new MachineState[MaxSamples];
    private readonly byte[][] m_frameBuffers = new byte[MaxSamples][];
    private readonly int m_frameWidth;
    private readonly int m_frameHeight;
    private int m_count;
    private int m_startIndex;
    private int m_indexToRestore;
    private ulong m_ticksPerSample;
    private ulong m_ticksToNextSample;
    private ulong m_lastCpuTicks;
    private string m_romPath;
    private int m_stateSize;
    private int m_pauseCount;
    private byte[] m_clearBuffer;

    public SnapshotHistory(ISnapshotHost host, ulong ticksPerSample)
    {
        m_host = host ?? throw new ArgumentNullException(nameof(host));
        m_frameWidth = SmsVdp.FrameWidth;
        m_frameHeight = SmsVdp.FrameHeight;
        SetTicksPerSample(ticksPerSample);
        var size = new PixelSize(m_frameWidth, m_frameHeight);
        ScreenPreview = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888);
    }

    public WriteableBitmap ScreenPreview { get; }

    public int LastSampleIndex => m_count - 1;

    public bool CanRestore => LastSampleIndex >= 0 && IndexToRestore < LastSampleIndex;

    public bool HasSnapshots => m_count > 0;

    private bool IsSnapshottingPaused => Volatile.Read(ref m_pauseCount) > 0;

    public int IndexToRestore
    {
        get => m_indexToRestore;
        set
        {
            if (!SetField(ref m_indexToRestore, value))
                return;
            UpdatePreview();
            OnPropertyChanged(nameof(CanRestore));
        }
    }

    public void SetTicksPerSample(ulong ticksPerSample)
    {
        if (ticksPerSample == 0)
            return;

        m_ticksPerSample = ticksPerSample;
        if (m_ticksToNextSample == 0 || m_ticksToNextSample > m_ticksPerSample)
            m_ticksToNextSample = m_ticksPerSample;
    }

    public void ResetForRom(string romPath)
    {
        m_romPath = romPath;
        EnsureBuffersAllocated();
        Clear();
    }

    private void Clear()
    {
        m_count = 0;
        m_startIndex = 0;
        m_lastCpuTicks = 0;
        m_ticksToNextSample = m_ticksPerSample;
        IndexToRestore = 0;
        ClearPreview();
        OnPropertyChanged(nameof(LastSampleIndex));
        OnPropertyChanged(nameof(HasSnapshots));
        OnPropertyChanged(nameof(CanRestore));
    }

    public void OnFrameRendered(ulong currentCpuTicks)
    {
        if (IsSnapshottingPaused)
            return;

        if (!m_host.IsRunning || !m_host.HasLoadedCartridge)
            return;

        if (m_lastCpuTicks == 0)
        {
            m_lastCpuTicks = currentCpuTicks;
            return;
        }

        var delta = currentCpuTicks - m_lastCpuTicks;
        if (delta == 0)
            return;
        m_lastCpuTicks = currentCpuTicks;

        if (delta < m_ticksToNextSample)
        {
            m_ticksToNextSample -= delta;
            return;
        }

        while (delta >= m_ticksToNextSample)
        {
            delta -= m_ticksToNextSample;
            m_ticksToNextSample = m_ticksPerSample;
            CaptureSnapshot();
        }

        if (delta > 0)
            m_ticksToNextSample -= delta;
    }

    public void Rollback()
    {
        if (!CanRestore)
            return;

        var state = GetSnapshot(IndexToRestore);
        if (state == null)
            return;

        m_host.LoadState(state);
        TrimTo(IndexToRestore + 1);
        IndexToRestore = LastSampleIndex;
        m_ticksToNextSample = m_ticksPerSample;
        m_lastCpuTicks = m_host.CpuClockTicks;
    }

    public MachineState CaptureSnapshotNow()
    {
        CaptureSnapshot();
        return GetSnapshot(LastSampleIndex);
    }

    public void PauseSnapshotting() =>
        Interlocked.Increment(ref m_pauseCount);

    public void ResumeSnapshotting()
    {
        var newValue = Interlocked.Decrement(ref m_pauseCount);
        if (newValue < 0)
            Interlocked.Exchange(ref m_pauseCount, 0);
    }

    private void CaptureSnapshot()
    {
        EnsureBuffersAllocated();

        var index = GetWriteIndex();
        var state = m_states[index];
        var frameBuffer = m_frameBuffers[index];
        if (state == null || frameBuffer == null)
            return;

        m_host.CaptureState(state, frameBuffer);
        state.RomPath = m_romPath;

        if (m_count == MaxSamples)
            m_startIndex = (m_startIndex + 1) % MaxSamples;
        else
            m_count++;

        OnPropertyChanged(nameof(LastSampleIndex));
        OnPropertyChanged(nameof(HasSnapshots));

        IndexToRestore = LastSampleIndex;
        UpdatePreview();
        OnPropertyChanged(nameof(CanRestore));
    }

    private void TrimTo(int count)
    {
        m_count = Math.Clamp(count, 0, m_count);
        OnPropertyChanged(nameof(LastSampleIndex));
        OnPropertyChanged(nameof(HasSnapshots));
        OnPropertyChanged(nameof(CanRestore));
    }

    private MachineState GetSnapshot(int index)
    {
        if ((uint)index >= (uint)m_count)
            return null;
        var physicalIndex = (m_startIndex + index) % MaxSamples;
        return m_states[physicalIndex];
    }

    private byte[] GetPreviewBuffer(int index)
    {
        if ((uint)index >= (uint)m_count)
            return null;
        var physicalIndex = (m_startIndex + index) % MaxSamples;
        return m_frameBuffers[physicalIndex];
    }

    private int GetWriteIndex()
    {
        if (m_count < MaxSamples)
            return (m_startIndex + m_count) % MaxSamples;
        return m_startIndex;
    }

    private void EnsureBuffersAllocated()
    {
        var stateSize = m_host.GetStateSize();
        if (stateSize <= 0)
            return;
        if (m_stateSize == stateSize && m_states[0] != null)
            return;

        m_stateSize = stateSize;
        var frameBufferSize = m_frameWidth * m_frameHeight * 4;
        for (var i = 0; i < MaxSamples; i++)
        {
            if (m_states[i] == null || m_states[i].Size != stateSize)
                m_states[i] = new MachineState(stateSize);

            if (m_frameBuffers[i] == null || m_frameBuffers[i].Length != frameBufferSize)
                m_frameBuffers[i] = new byte[frameBufferSize];
        }

        if (m_clearBuffer == null || m_clearBuffer.Length != frameBufferSize)
            m_clearBuffer = new byte[frameBufferSize];
    }

    private void UpdatePreview()
    {
        var buffer = GetPreviewBuffer(IndexToRestore);
        if (buffer == null)
        {
            ClearPreview();
            return;
        }

        using var locked = ScreenPreview.Lock();
        var destStride = locked.RowBytes;
        var srcStride = m_frameWidth * 4;

        for (var y = 0; y < m_frameHeight; y++)
        {
            var srcOffset = y * srcStride;
            var destOffset = y * destStride;
            Marshal.Copy(buffer, srcOffset, IntPtr.Add(locked.Address, destOffset), srcStride);
        }

        OnPropertyChanged(nameof(ScreenPreview));
    }

    private void ClearPreview()
    {
        if (m_clearBuffer == null)
            return;

        using var locked = ScreenPreview.Lock();
        var totalBytes = locked.RowBytes * m_frameHeight;
        Marshal.Copy(m_clearBuffer, 0, locked.Address, Math.Min(totalBytes, m_clearBuffer.Length));

        if (m_clearBuffer.Length < totalBytes)
        {
            var remaining = totalBytes - m_clearBuffer.Length;
            var offset = m_clearBuffer.Length;
            while (remaining > 0)
            {
                var count = Math.Min(m_clearBuffer.Length, remaining);
                Marshal.Copy(m_clearBuffer, 0, IntPtr.Add(locked.Address, offset), count);
                remaining -= count;
                offset += count;
            }
        }

        OnPropertyChanged(nameof(ScreenPreview));
    }
}
