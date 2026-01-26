// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation.Snapshot;

namespace DTC.Emulation;

/// <summary>
/// Runs a machine on its own CPU thread with real-time synchronization and snapshot support.
/// </summary>
public sealed class MachineRunner : ISnapshotHost, IDisposable
{
    private const int PauseRefreshIntervalMs = 33;
    private readonly IMachine m_machine;
    private readonly ClockSync m_clockSync;
    private readonly Lock m_cpuStepLock = new();
    private readonly ManualResetEventSlim m_cpuPauseEvent = new(initialState: true);
    private readonly Action<Exception> m_onError;
    private Thread m_cpuThread;
    private bool m_shutdownRequested;
    private volatile bool m_isCpuPaused;
    private long m_lastCpuTicks;

    public MachineRunner(IMachine machine, Func<double> cpuHzProvider, Action<Exception> onError = null)
    {
        m_machine = machine ?? throw new ArgumentNullException(nameof(machine));
        if (cpuHzProvider == null)
            throw new ArgumentNullException(nameof(cpuHzProvider));
        m_clockSync = new ClockSync(cpuHzProvider, () => m_machine.CpuTicks, () => m_machine.Reset());
        m_onError = onError;
    }

    public IMachine Machine => m_machine;

    public bool IsRunning => m_cpuThread != null;

    public bool IsPaused => m_isCpuPaused;

    public event EventHandler PausedFrameRefreshRequested;
    public event EventHandler StateLoaded;

    public void Start()
    {
        if (m_cpuThread != null)
            return;

        m_shutdownRequested = false;
        m_clockSync.Reset();
        m_lastCpuTicks = m_machine.CpuTicks;
        m_cpuThread = new Thread(RunCpuLoop)
        {
            Name = $"{m_machine.Name} CPU",
            IsBackground = true
        };
        m_cpuThread.Start();
    }

    public void Stop()
    {
        if (m_cpuThread == null)
            return;

        m_shutdownRequested = true;
        m_cpuPauseEvent.Set();
        if (!m_cpuThread.Join(TimeSpan.FromSeconds(2)))
            m_cpuThread.Interrupt();
        m_cpuThread = null;
    }

    public void Reset()
    {
        lock (m_cpuStepLock)
        {
            m_machine.Reset();
            m_clockSync.Reset();
            m_lastCpuTicks = 0;
        }
    }

    public void ResyncClock() => m_clockSync.Resync();

    public bool TogglePause()
    {
        lock (m_cpuStepLock)
        {
            m_isCpuPaused = !m_isCpuPaused;
            if (m_isCpuPaused)
            {
                m_cpuPauseEvent.Reset();
            }
            else
            {
                m_clockSync.Resync();
                m_cpuPauseEvent.Set();
            }

            return m_isCpuPaused;
        }
    }

    public void Dispose()
    {
        Stop();
        m_cpuPauseEvent.Dispose();
    }

    private void RunCpuLoop()
    {
        try
        {
            while (!m_shutdownRequested)
            {
                if (!m_cpuPauseEvent.IsSet)
                {
                    m_cpuPauseEvent.Wait(TimeSpan.FromMilliseconds(PauseRefreshIntervalMs));
                    PausedFrameRefreshRequested?.Invoke(this, EventArgs.Empty);
                    continue;
                }

                m_clockSync.SyncWithRealTime();
                lock (m_cpuStepLock)
                {
                    m_machine.StepCpu();
                    var current = m_machine.CpuTicks;
                    var delta = current - m_lastCpuTicks;
                    if (delta > 0)
                        m_machine.AdvanceDevices(delta);
                    m_lastCpuTicks = current;
                    if (m_machine.TryConsumeInterrupt())
                        m_machine.RequestInterrupt();
                }
            }
        }
        catch (ThreadInterruptedException)
        {
            // Expected during shutdown.
        }
        catch (Exception e)
        {
            if (m_onError != null)
                m_onError(e);
        }
    }

    bool ISnapshotHost.IsRunning => IsRunning;

    bool ISnapshotHost.HasLoadedCartridge => m_machine.HasLoadedCartridge;

    ulong ISnapshotHost.CpuClockTicks => (ulong)m_machine.CpuTicks;

    int ISnapshotHost.FrameWidth => m_machine.Video.FrameWidth;

    int ISnapshotHost.FrameHeight => m_machine.Video.FrameHeight;

    int ISnapshotHost.GetStateSize() => m_machine.Snapshotter.GetStateSize();

    void ISnapshotHost.CaptureState(MachineState state, Span<byte> frameBuffer) =>
        m_machine.Snapshotter.Save(state, frameBuffer);

    void ISnapshotHost.LoadState(MachineState state) =>
        LoadState(state);

    public void LoadState(MachineState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        lock (m_cpuStepLock)
        {
            m_machine.Snapshotter.Load(state);
            m_lastCpuTicks = m_machine.CpuTicks;
            m_clockSync.Resync();
        }

        StateLoaded?.Invoke(this, EventArgs.Empty);
    }
}
