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
using CSharp.Core;
using CSharp.Core.Commands;
using CSharp.Core.Extensions;
using CSharp.Core.UI;
using CSharp.Core.ViewModels;
using DTC.Z80;
using DTC.Z80.Devices;

namespace MasterG33k.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
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
    private Thread m_cpuThread;
    private bool m_shutdownRequested;
    private volatile bool m_pauseNmiRequested;
    private readonly ClockSync m_clockSync;
    private readonly LcdScreen m_screen;
    private long m_lastCpuTStates;
    private uint m_lastFrameChecksum;
    private long m_lastFrameTicks;
    private bool m_hasFrameChecksum;

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
        m_vdp = new SmsVdp();
        m_vdp.FrameRendered += OnFrameRendered;
        m_joypad = new SmsJoypad();
        m_memoryController = new SmsMemoryController();
        var portDevice = new SmsPortDevice(m_vdp, m_joypad, m_memoryController);
        m_joypad.PausePressed += (_, _) =>
        {
            // Map Pause to NMI on the CPU without blocking the UI thread.
            m_pauseNmiRequested = true;
        };
        m_cpu = new Cpu(new Bus(new Memory(), portDevice));
        m_cpu.Bus.Attach(new SmsRamMirrorDevice(m_cpu.MainMemory));
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

    public void CloseCommand() => Application.Current.GetMainWindow().Close();

    public void SaveScreenshot()
    {
        var prefix = SanitizeFileName(m_currentRomTitle);
        var defaultName = $"{prefix}.tga";
        var command = new FileSaveCommand("Save Screenshot", "TGA Files", ["*.tga"], defaultName);
        command.FileSelected += (_, info) => m_vdp.DumpFrame(info);
        command.Execute(null);
    }

    public void LoadBios(FileInfo biosFile)
    {
        if (biosFile == null)
            throw new ArgumentNullException(nameof(biosFile));
        if (!biosFile.Exists)
        {
            Logger.Instance.Warn($"BIOS file '{biosFile.FullName}' was not found.");
            return;
        }

        var biosData = biosFile.ReadAllBytes();
        if (biosData.Length > 0x4000)
        {
            SmsRomChecksum.TryPatchChecksum(biosData, patchBothFields: true);
            var romDevice = new SmsRomDevice(biosData);
            m_cpu.Bus.Attach(new SmsMapperDevice(m_memoryController, m_cpu.MainMemory));
            m_memoryController.SetCartridge(romDevice, forceEnabled: false);
            m_memoryController.SetBiosRom(romDevice);
            m_memoryController.Reset();
            LogRomInfo(biosFile, biosData, romDevice.BankCount);
            StartCpuIfNeeded();
            return;
        }

        m_memoryController.SetBios(biosData);
        m_memoryController.Reset();
        LogRomInfo(biosFile, biosData, bankCount: 1);
        StartCpuIfNeeded();
    }

    public void TryLoadLastRom()
    {
        if (string.IsNullOrWhiteSpace(Settings.LastRomPath))
            return;

        var romFile = new FileInfo(Settings.LastRomPath);
        if (!romFile.Exists)
            return;

        LoadRomFromFile(romFile, addToMru: true);
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

    public void ExportTileMap()
    {
        var prefix = SanitizeFileName(m_currentRomTitle);
        var defaultName = $"{prefix}-tilemap.tga";
        var command = new FileSaveCommand("Export Tile Map", "TGA Files", ["*.tga"], defaultName);
        command.FileSelected += (_, info) => m_vdp.DumpSpriteTileMap(info);
        command.Execute(null);
    }

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

    internal void LoadRomFile(FileInfo romFile, bool addToMru = true) =>
        LoadRomFromFile(romFile, addToMru);

    internal void LoadRomFromFile(FileInfo romFile, bool addToMru)
    {
        if (romFile == null)
            return;
        if (!romFile.Exists)
        {
            Logger.Instance.Warn($"Unable to load ROM '{romFile.FullName}': File not found.");
            return;
        }

        var romData = ReadRomData(romFile, out var romName);
        if (romData == null || romData.Length == 0)
        {
            Logger.Instance.Warn($"Unable to load ROM '{romFile.FullName}': No valid ROM data found.");
            return;
        }

        StopCpu();

        var romDevice = new SmsRomDevice(romData);
        m_cpu.Bus.Attach(new SmsMapperDevice(m_memoryController, m_cpu.MainMemory));
        m_memoryController.SetCartridge(romDevice, forceEnabled: false);

        lock (m_cpuStepLock)
        {
            Array.Clear(m_cpu.MainMemory.Data, 0, m_cpu.MainMemory.Data.Length);
            m_cpu.Reset();
            m_vdp.Reset();
            m_memoryController.Reset();
            m_clockSync.Reset();
            m_lastCpuTStates = 0;
        }

        if (addToMru)
            Mru.Add(romFile);
        Settings.LastRomPath = romFile.FullName;

        m_currentRomTitle = romName;
        WindowTitle = $"MasterG33k - {m_currentRomTitle}";
        StartCpuIfNeeded();
        LogRomInfo(romFile, romData, romDevice.BankCount);
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
                m_clockSync.SyncWithRealTime();
                lock (m_cpuStepLock)
                {
                    if (m_pauseNmiRequested)
                    {
                        m_pauseNmiRequested = false;
                        m_cpu.RequestNmi();
                    }
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

    private void OnFrameRendered(object sender, byte[] frameBuffer)
    {
        if (frameBuffer == null || frameBuffer.Length == 0)
            return;

        m_lastFrameChecksum = ComputeFrameChecksum(frameBuffer);
        m_lastFrameTicks = m_cpu.TStatesSinceCpuStart;
        m_hasFrameChecksum = true;
        m_screen.Update(frameBuffer);
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
