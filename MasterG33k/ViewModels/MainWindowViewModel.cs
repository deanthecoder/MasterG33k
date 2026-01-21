// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using DTC.Core;
using DTC.Core.Image;
using DTC.Core.Commands;
using DTC.Core.Extensions;
using DTC.Core.UI;
using DTC.Core.ViewModels;
using DTC.Z80;
using DTC.Z80.Devices;

namespace MasterG33k.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int PauseRefreshIntervalMs = 33;
    private string m_windowTitle;
    private bool m_isSoundChannel1Enabled = true;
    private bool m_isSoundChannel2Enabled = true;
    private bool m_isSoundChannel3Enabled = true;
    private bool m_isSoundChannel4Enabled = true;
    private bool m_isCpuHistoryTracked;
    private readonly Cpu m_cpu;
    private readonly SmsVdp m_vdp;
    private readonly SmsJoypad m_joypad;
    private readonly SmsMemoryController m_memoryController;
    private readonly Lock m_cpuStepLock = new();
    private readonly ManualResetEventSlim m_cpuPauseEvent = new(initialState: true);
    private Thread m_cpuThread;
    private bool m_shutdownRequested;
    private volatile bool m_isCpuPaused;
    private readonly ClockSync m_clockSync;
    private readonly LcdScreen m_screen;
    private volatile bool m_isPaused;
    private long m_lastCpuTStates;
    private uint m_lastFrameChecksum;
    private long m_lastFrameTicks;
    private bool m_hasFrameChecksum;
    private volatile byte[] m_lastFrameBuffer;

    public MruFiles Mru { get; }
    public IImage Display { get; }
    public AboutInfo AboutInfo { get; } = AboutInfoProvider.Info;

    public Settings Settings => Settings.Instance;

    public string WindowTitle
    {
        get => m_windowTitle ?? "MasterG33k";
        private set => SetField(ref m_windowTitle, value);
    }

    private string m_currentRomTitle = "MasterG33k";

    public bool IsRecording => false;

    public bool IsRecordingIndicatorOn => false;

    public event EventHandler DisplayUpdated;

    public bool IsDebugBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    public bool IsSoundChannel1Enabled
    {
        get => m_isSoundChannel1Enabled;
        private set => SetField(ref m_isSoundChannel1Enabled, value);
    }

    public bool IsSoundChannel2Enabled
    {
        get => m_isSoundChannel2Enabled;
        private set => SetField(ref m_isSoundChannel2Enabled, value);
    }

    public bool IsSoundChannel3Enabled
    {
        get => m_isSoundChannel3Enabled;
        private set => SetField(ref m_isSoundChannel3Enabled, value);
    }

    public bool IsSoundChannel4Enabled
    {
        get => m_isSoundChannel4Enabled;
        private set => SetField(ref m_isSoundChannel4Enabled, value);
    }

    public bool IsCpuHistoryTracked
    {
        get => m_isCpuHistoryTracked;
        private set
        {
            if (!SetField(ref m_isCpuHistoryTracked, value))
                return;
            Settings.IsCpuHistoryTracked = value;
#if DEBUG
            m_cpu.InstructionLogger.IsEnabled = value;
#endif
        }
    }

    public MainWindowViewModel()
    {
        Mru = new MruFiles().InitFromString(Settings.MruFiles);
        Mru.OpenRequested += (_, file) => LoadRomFromFile(file, addToMru: false);

        m_screen = new LcdScreen(SmsVdp.FrameWidth, SmsVdp.FrameHeight);
        Display = m_screen.Display;
        m_screen.FrameBuffer.IsCrt = Settings.IsCrtEmulationEnabled;
        m_vdp = new SmsVdp();
        m_vdp.FrameRendered += OnFrameRendered;
        m_joypad = new SmsJoypad();
        m_memoryController = new SmsMemoryController();
        var mainMemory = new Memory();
        var portDevice = new SmsPortDevice(m_vdp, m_joypad, m_memoryController);
        m_joypad.PausePressed += (_, _) =>
        {
            ToggleCpuPause();
        };
        m_cpu = new Cpu(new Bus(mainMemory, portDevice));
        m_cpu.Bus.Attach(new SmsSystemRamDevice(m_cpu.MainMemory, m_memoryController));
        m_cpu.Bus.Attach(new SmsSystemRamMirrorDevice(m_cpu.MainMemory, m_memoryController));
        m_cpu.Bus.Attach(m_memoryController);
        m_clockSync = new ClockSync(GetEffectiveCpuHz, () => m_cpu.TStatesSinceCpuStart, () => m_cpu.Reset());
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        IsCpuHistoryTracked = Settings.IsCpuHistoryTracked;
        ApplyLayerVisibility();
#if DEBUG
        m_cpu.InstructionLogger.IsEnabled = IsCpuHistoryTracked;
#endif
    }

    public void ToggleAmbientBlur()
    {
        Settings.IsAmbientBlurred = !Settings.IsAmbientBlurred;
    }

    public void ToggleCrtEmulation()
    {
        Settings.IsCrtEmulationEnabled = !Settings.IsCrtEmulationEnabled;
    }

    public void ToggleBackgroundVisibility()
    {
        Settings.IsBackgroundVisible = !Settings.IsBackgroundVisible;
    }

    public void ToggleSpriteVisibility()
    {
        Settings.AreSpritesVisible = !Settings.AreSpritesVisible;
    }

    public void ToggleSoundChannel1() => IsSoundChannel1Enabled = !IsSoundChannel1Enabled;

    public void ToggleSoundChannel2() => IsSoundChannel2Enabled = !IsSoundChannel2Enabled;

    public void ToggleSoundChannel3() => IsSoundChannel3Enabled = !IsSoundChannel3Enabled;

    public void ToggleSoundChannel4() => IsSoundChannel4Enabled = !IsSoundChannel4Enabled;

    public void ToggleRecording() => Logger.Instance.Info("Recording is not implemented yet.");

    public void StartRecording() => Logger.Instance.Info("Recording is not implemented yet.");

    public void StopRecording() => Logger.Instance.Info("Recording is not implemented yet.");

    public void LoadGameRom()
    {
        var command = new FileOpenCommand("Open ROM", "Master System ROMs", ["*.sms", "*.zip"]);
        command.FileSelected += (_, info) => LoadRomFromFile(info, addToMru: true);
        command.Execute(null);
    }

    public void LoadLastRomOrPrompt()
    {
        if (!string.IsNullOrEmpty(Settings.LastRomPath))
        {
            var romFile = new FileInfo(Settings.LastRomPath);
            if (LoadRomFromFile(romFile, addToMru: true))
                return;
        }

        LoadGameRom();
    }

    public void CloseCommand() => Application.Current.GetMainWindow().Close();

    public void SaveScreenshot()
    {
        var prefix = SanitizeFileName(m_currentRomTitle);
        var defaultName = $"{prefix}.tga";
        var command = new FileSaveCommand("Save Screenshot", "TGA Files", ["*.tga"], defaultName);
        command.FileSelected += (_, info) =>
        {
            var frameBuffer = m_lastFrameBuffer;
            if (frameBuffer == null || frameBuffer.Length == 0)
            {
                Logger.Instance.Warn("No frame available for screenshot.");
                return;
            }

            var expectedSize = SmsVdp.FrameWidth * SmsVdp.FrameHeight * 4;
            if (frameBuffer.Length != expectedSize)
            {
                Logger.Instance.Warn($"Screenshot aborted; expected {expectedSize} bytes but got {frameBuffer.Length}.");
                return;
            }

            var frameCopy = new byte[frameBuffer.Length];
            Buffer.BlockCopy(frameBuffer, 0, frameCopy, 0, frameBuffer.Length);
            TgaWriter.Write(info, frameCopy, SmsVdp.FrameWidth, SmsVdp.FrameHeight, 4);
        };
        command.Execute(null);
    }

    private static void LogRomInfo(FileInfo romFile, byte[] romData, int bankCount)
    {
        var name = romFile.LeafName();
        var headerOffset = FindSmsHeaderOffset(romData);
        var manufacturer = headerOffset >= 0 ? "SEGA" : "Unknown";
        var headerInfo = headerOffset >= 0 ? $"Header @ 0x{headerOffset:X4}" : "Header not found";
        var sizeKb = romData.Length / 1024.0;
        Logger.Instance.Info($"ROM loaded: {name} ({sizeKb:0.#} KB, Banks={bankCount}, Maker={manufacturer}, {headerInfo})");
    }

    private static int FindSmsHeaderOffset(byte[] romData)
    {
        if (romData == null || romData.Length < 8)
            return -1;

        var signature = "TMR SEGA"u8.ToArray();
        var max = romData.Length - signature.Length;
        for (var i = 0; i <= max; i++)
        {
            var match = !signature.Where((t, j) => romData[i + j] != t).Any();
            if (match)
                return i;
        }

        return -1;
    }

    public void SaveSnapshot() => Logger.Instance.Info("Snapshots are not implemented yet.");

    public void OpenLog()
    {
        var logFile = Assembly.GetEntryAssembly()
            .GetAppSettingsPath()
            .GetFile("log.txt");
        Process.Start(new ProcessStartInfo(logFile.FullName)
        {
            UseShellExecute = true
        });
    }

    public void OpenProjectPage() => new Uri("https://github.com/deanthecoder/MasterG33k").Open();
    
    public void ResetDevice()
    {
        lock (m_cpuStepLock)
        {
            m_cpu.Reset();
            m_vdp.Reset();
            m_memoryController.Reset();
            m_clockSync.Reset();
            m_lastCpuTStates = 0;
        }
        SetPaused(false);
        Logger.Instance.Info("CPU reset.");
    }

    public void DumpCpuHistory() =>
        m_cpu.InstructionLogger.DumpToConsole();

    public void ReportCpuClockTicks() =>
        Console.WriteLine($"CPU clock ticks: {m_cpu.TStatesSinceCpuStart}");

    public void ReportFrameChecksum()
    {
        if (!m_hasFrameChecksum)
        {
            Console.WriteLine($"Frame checksum: n/a (no frame rendered yet). CPU ticks: {m_cpu.TStatesSinceCpuStart}");
            return;
        }

        Console.WriteLine($"Frame checksum: 0x{m_lastFrameChecksum:X8} @ CPU ticks {m_lastFrameTicks} (current {m_cpu.TStatesSinceCpuStart}).");
    }

    public void TrackCpuHistory() => IsCpuHistoryTracked = !IsCpuHistoryTracked;

    public void SetInputActive(bool isActive) =>
        m_joypad.SetInputEnabled(isActive);

    private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.IsSoundEnabled))
            return;
        if (e.PropertyName == nameof(Settings.IsCrtEmulationEnabled))
        {
            m_screen.FrameBuffer.IsCrt = Settings.IsCrtEmulationEnabled;
            return;
        }
        if (e.PropertyName == nameof(Settings.IsBackgroundVisible) ||
            e.PropertyName == nameof(Settings.AreSpritesVisible))
        {
            ApplyLayerVisibility();
            return;
        }
        if (e.PropertyName == nameof(Settings.IsCpuHistoryTracked))
        {
            IsCpuHistoryTracked = Settings.IsCpuHistoryTracked;
        }
    }

    private void ApplyLayerVisibility()
    {
        m_vdp.IsBackgroundVisible = Settings.IsBackgroundVisible;
        m_vdp.AreSpritesVisible = Settings.AreSpritesVisible;
    }

    internal bool LoadRomFromFile(FileInfo romFile, bool addToMru)
    {
        if (romFile == null)
            return false;
        if (!romFile.Exists)
        {
            Logger.Instance.Warn($"Unable to load ROM '{romFile.FullName}': File not found.");
            return false;
        }

        var romData = ReadRomData(romFile, out var romName);
        if (romData == null || romData.Length == 0)
        {
            Logger.Instance.Warn($"Unable to load ROM '{romFile.FullName}': No valid ROM data found.");
            return false;
        }

        StopCpu();

        var romDevice = new SmsRomDevice(romData);
        var mapper = new SmsMapperDevice(m_memoryController, m_cpu.MainMemory);
        m_cpu.Bus.Attach(mapper);
        m_cpu.Bus.Attach(new SmsMapperMirrorDevice(mapper));
        m_memoryController.SetBios(null);
        m_memoryController.SetCartridge(romDevice, forceEnabled: false);
        InitializePostRomState(romDevice);

        if (addToMru)
            Mru.Add(romFile);
        Settings.LastRomPath = romFile.FullName;

        m_currentRomTitle = romName;
        WindowTitle = $"MasterG33k - {m_currentRomTitle}";
        StartCpuIfNeeded();
        LogRomInfo(romFile, romData, romDevice.BankCount);
        return true;
    }

    private void InitializePostRomState(SmsRomDevice romDevice)
    {
        if (romDevice == null)
            throw new ArgumentNullException(nameof(romDevice));

        lock (m_cpuStepLock)
        {
            Array.Clear(m_cpu.MainMemory.Data, 0, m_cpu.MainMemory.Data.Length);
            m_cpu.Reset();
            m_vdp.ApplyPostBiosState();
            m_memoryController.Reset();

            // Post-BIOS slot state: BIOS disabled, cartridge/RAM/IO enabled, expansion/card disabled.
            m_memoryController.WriteControl(0xA8);

            // BIOS stores the last port $3E value at $C000; some titles read it back on boot.
            m_cpu.MainMemory.Data[0xC000] = 0xA8;
            m_cpu.Bus.Write8(0xFFFC, romDevice.Control);
            m_cpu.Bus.Write8(0xFFFD, romDevice.Bank0);
            m_cpu.Bus.Write8(0xFFFE, romDevice.Bank1);
            m_cpu.Bus.Write8(0xFFFF, romDevice.Bank2);

            m_cpu.Reg.PC = 0x0000;
            m_cpu.Reg.SP = 0xDFF0;
            m_cpu.Reg.AF = 0x0000;
            m_cpu.Reg.BC = 0x0000;
            m_cpu.Reg.DE = 0x0000;
            m_cpu.Reg.HL = 0x0000;
            m_cpu.Reg.IX = 0x0000;
            m_cpu.Reg.IY = 0x0000;
            m_cpu.Reg.IM = 1;
            m_cpu.Reg.IFF1 = false;
            m_cpu.Reg.IFF2 = false;

            m_clockSync.Reset();
            m_lastCpuTStates = 0;
        }

        SetPaused(false);
    }

    private void SetPaused(bool isPaused)
    {
        if (m_isPaused == isPaused)
            return;

        m_isPaused = isPaused;
        m_screen.FrameBuffer.IsPaused = isPaused;
    }

    public void Dispose()
    {
        StopCpu();
        m_joypad.Dispose();
        m_screen.Dispose();
        Settings.MruFiles = Mru.AsString();
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void StartCpuIfNeeded()
    {
        if (m_cpuThread != null)
            return;

        m_shutdownRequested = false;
        m_clockSync.Reset();
        m_lastCpuTStates = m_cpu.TStatesSinceCpuStart;
        m_cpuThread = new Thread(RunCpuLoop)
        {
            Name = "MasterG33k CPU",
            IsBackground = true
        };
        m_cpuThread.Start();
    }

    private void StopCpu()
    {
        if (m_cpuThread == null)
            return;

        m_shutdownRequested = true;
        m_cpuPauseEvent.Set();
        if (!m_cpuThread.Join(TimeSpan.FromSeconds(2)))
            m_cpuThread.Interrupt();
        m_cpuThread = null;
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
                    RefreshPausedFrame();
                    continue;
                }

                m_clockSync.SyncWithRealTime();
                lock (m_cpuStepLock)
                {
                    m_cpu.Step();
                    var current = m_cpu.TStatesSinceCpuStart;
                    var delta = current - m_lastCpuTStates;
                    if (delta > 0)
                        m_vdp.AdvanceCycles(delta);
                    m_lastCpuTStates = current;
                    if (m_vdp.TryConsumeInterrupt())
                        m_cpu.RequestInterrupt();
                }
            }
        }
        catch (ThreadInterruptedException)
        {
            // Expected during shutdown.
        }
        catch (Exception e)
        {
            Logger.Instance.Error($"Stopping CPU loop due to exception: {e.Message}");
        }
    }

    private void ToggleCpuPause()
    {
        bool isPaused;
        byte[] frameBufferCopy;

        lock (m_cpuStepLock)
        {
            m_isCpuPaused = !m_isCpuPaused;
            isPaused = m_isCpuPaused;
            if (m_isCpuPaused)
            {
                m_cpuPauseEvent.Reset();
            }
            else
            {
                m_clockSync.Resync();
                m_cpuPauseEvent.Set();
            }

            frameBufferCopy = m_lastFrameBuffer;
        }

        SetPaused(isPaused);
        if (frameBufferCopy != null)
        {
            m_screen.Update(frameBufferCopy);
            DisplayUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnFrameRendered(object sender, byte[] frameBuffer)
    {
        if (frameBuffer == null || frameBuffer.Length == 0)
            return;

        var bufferCopy = new byte[frameBuffer.Length];
        Buffer.BlockCopy(frameBuffer, 0, bufferCopy, 0, frameBuffer.Length);
        m_lastFrameBuffer = bufferCopy;
        m_lastFrameChecksum = ComputeFrameChecksum(frameBuffer);
        m_lastFrameTicks = m_cpu.TStatesSinceCpuStart;
        m_hasFrameChecksum = true;
        m_screen.Update(frameBuffer);
        DisplayUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshPausedFrame()
    {
        if (!m_isPaused)
            return;

        var frameBuffer = m_lastFrameBuffer;
        if (frameBuffer == null)
            return;

        lock (m_cpuStepLock)
        {
            if (!m_isPaused)
                return;

            m_screen.Update(frameBuffer);
        }

        DisplayUpdated?.Invoke(this, EventArgs.Empty);
    }

    // Master System NTSC CPU clock; we always render 256x192.
    private static double GetEffectiveCpuHz() =>
        3_579_545;

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

    private static string SanitizeFileName(string input) =>
        string.IsNullOrWhiteSpace(input) ? "MasterG33k" : input.ToSafeFileName();

    private static byte[] ReadRomData(FileInfo romFile, out string romName)
    {
        romName = romFile?.Name;
        if (romFile == null)
            return null;

        if (!romFile.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return romFile.ReadAllBytes();

        using var archive = ZipFile.OpenRead(romFile.FullName);
        foreach (var entry in archive.Entries)
        {
            if (!entry.Name.EndsWith(".sms", StringComparison.OrdinalIgnoreCase))
                continue;

            var buffer = new byte[(int)entry.Length];
            using var stream = entry.Open();
            stream.ReadExactly(buffer.AsSpan());

            romName = entry.Name;
            return buffer;
        }

        return null;
    }
}
