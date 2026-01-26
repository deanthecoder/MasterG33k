// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using Avalonia.Media;
using DTC.Core;
using DTC.Core.Image;
using DTC.Core.ViewModels;
using DTC.Emulation.Audio;
using DTC.Emulation.Recording;
using DTC.Emulation.Snapshot;

namespace DTC.Emulation;

/// <summary>
/// Hosts common emulator UI behaviors like pause, recording, screenshots, and rollback.
/// </summary>
public sealed class EmulatorViewModel : ViewModelBase, IDisposable
{
    private readonly IMachine m_machine;
    private readonly MachineRunner m_runner;
    private readonly IAudioOutputDevice m_audioDevice;
    private readonly LcdScreen m_screen;
    private readonly Func<double> m_videoFrameRateProvider;
    private readonly Func<string> m_romTitleProvider;
    private readonly DisplayRecorder m_recorder;
    private volatile byte[] m_lastFrameBuffer;
    private uint m_lastFrameChecksum;
    private long m_lastFrameTicks;
    private bool m_hasFrameChecksum;

    public EmulatorViewModel(
        IMachine machine,
        MachineRunner runner,
        IAudioOutputDevice audioDevice,
        LcdScreen screen,
        Func<double> videoFrameRateProvider,
        Func<string> romTitleProvider,
        Func<double> cpuHzProvider)
    {
        m_machine = machine ?? throw new ArgumentNullException(nameof(machine));
        m_runner = runner ?? throw new ArgumentNullException(nameof(runner));
        m_audioDevice = audioDevice ?? throw new ArgumentNullException(nameof(audioDevice));
        m_screen = screen ?? throw new ArgumentNullException(nameof(screen));
        m_videoFrameRateProvider = videoFrameRateProvider ?? throw new ArgumentNullException(nameof(videoFrameRateProvider));
        m_romTitleProvider = romTitleProvider ?? throw new ArgumentNullException(nameof(romTitleProvider));
        if (cpuHzProvider == null)
            throw new ArgumentNullException(nameof(cpuHzProvider));

        Display = m_screen.Display;
        SnapshotHistory = new SnapshotHistory(m_runner, (ulong)cpuHzProvider());
        m_recorder = new DisplayRecorder();
        m_recorder.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(IsRecordingIndicatorOn));
        };

        m_machine.Video.FrameRendered += OnFrameRendered;
        m_runner.PausedFrameRefreshRequested += (_, _) => RefreshPausedFrame();
        m_runner.StateLoaded += (_, _) => RefreshDisplayFromVideo();
    }

    public IImage Display { get; }

    public SnapshotHistory SnapshotHistory { get; }

    public IAudioOutputDevice AudioDevice => m_audioDevice;

    public bool IsRecording => m_recorder.IsRecording;

    public bool IsRecordingIndicatorOn
    {
        get => m_recorder.IsIndicatorOn;
    }

    public bool HasFrameChecksum => m_hasFrameChecksum;

    public uint LastFrameChecksum => m_lastFrameChecksum;

    public long LastFrameTicks => m_lastFrameTicks;

    public long CpuTicks => m_machine.CpuTicks;

    public event EventHandler DisplayUpdated;

    public void Start()
    {
        if (m_runner.IsRunning)
            return;

        m_audioDevice.Start();
        m_runner.Start();
    }

    public void Stop()
    {
        if (!m_runner.IsRunning)
            return;

        m_runner.Stop();
    }

    public void Reset() => m_runner.Reset();

    public bool TogglePause()
    {
        var isPaused = m_runner.TogglePause();
        m_screen.FrameBuffer.IsPaused = isPaused;

        var frameBufferCopy = m_lastFrameBuffer;
        if (frameBufferCopy != null)
        {
            m_screen.Update(frameBufferCopy);
            DisplayUpdated?.Invoke(this, EventArgs.Empty);
        }

        return isPaused;
    }

    public void SetScreenEffectEnabled(bool isEnabled) =>
        m_screen.FrameBuffer.IsCrt = isEnabled;

    public void SaveScreenshot(FileInfo file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        var frameBuffer = m_lastFrameBuffer;
        if (frameBuffer == null || frameBuffer.Length == 0)
        {
            Logger.Instance.Warn("No frame available for screenshot.");
            return;
        }

        var expectedSize = m_machine.Video.FrameWidth * m_machine.Video.FrameHeight * 4;
        if (frameBuffer.Length != expectedSize)
        {
            Logger.Instance.Warn($"Screenshot aborted; expected {expectedSize} bytes but got {frameBuffer.Length}.");
            return;
        }

        var frameCopy = new byte[frameBuffer.Length];
        Buffer.BlockCopy(frameBuffer, 0, frameCopy, 0, frameBuffer.Length);
        TgaWriter.Write(file, frameCopy, m_machine.Video.FrameWidth, m_machine.Video.FrameHeight, 4);
    }

    public void ToggleRecording()
    {
        if (IsRecording)
            StopRecording();
        else
            StartRecording();
    }

    public void StartRecording()
    {
        m_recorder.Start(m_screen.Display, m_videoFrameRateProvider(), m_audioDevice, m_romTitleProvider);
    }

    public void StopRecording()
    {
        m_recorder.Stop();
    }

    public void Dispose()
    {
        m_recorder.Dispose();
    }

    private void OnFrameRendered(object sender, byte[] frameBuffer)
    {
        if (frameBuffer == null || frameBuffer.Length == 0)
            return;

        var bufferCopy = new byte[frameBuffer.Length];
        Buffer.BlockCopy(frameBuffer, 0, bufferCopy, 0, frameBuffer.Length);
        m_lastFrameBuffer = bufferCopy;
        m_lastFrameChecksum = ComputeFrameChecksum(frameBuffer);
        m_lastFrameTicks = m_machine.CpuTicks;
        m_hasFrameChecksum = true;
        m_screen.Update(frameBuffer);
        m_recorder.CaptureFrame();
        SnapshotHistory?.OnFrameRendered((ulong)m_machine.CpuTicks);
        DisplayUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshPausedFrame()
    {
        var frameBuffer = m_lastFrameBuffer;
        if (frameBuffer == null)
            return;

        m_screen.Update(frameBuffer);
        DisplayUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshDisplayFromVideo()
    {
        var frameBuffer = new byte[m_machine.Video.FrameWidth * m_machine.Video.FrameHeight * 4];
        m_machine.Video.CopyFrameBuffer(frameBuffer);
        m_lastFrameBuffer = frameBuffer;
        m_lastFrameChecksum = ComputeFrameChecksum(frameBuffer);
        m_lastFrameTicks = m_machine.CpuTicks;
        m_hasFrameChecksum = true;
        m_screen.Update(frameBuffer);
        DisplayUpdated?.Invoke(this, EventArgs.Empty);
    }

    private static uint ComputeFrameChecksum(byte[] frameBuffer)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var b in frameBuffer)
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }

}
