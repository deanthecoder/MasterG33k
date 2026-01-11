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
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CSharp.Core;
using CSharp.Core.Extensions;
using CSharp.Core.UI;
using CSharp.Core.ViewModels;
using DTC.Z80;

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
    private readonly object m_cpuStepLock = new();
    private Thread m_cpuThread;
    private bool m_shutdownRequested;
    private readonly ClockSync m_clockSync;

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
        Mru.OpenRequested += (_, file) => LoadRomFile(file, addToMru: false);

        Display = CreateBlackDisplay();
        m_cpu = new Cpu(new Bus(new Memory()));
        m_clockSync = new ClockSync(GetEffectiveCpuHz, () => m_cpu.TStatesSinceCpuStart, () => m_cpu.Reset());
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        IsCpuHistoryTracked = Settings.IsCpuHistoryTracked;
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

    public void LoadGameRom() => Logger.Instance.Info("ROM loading is not implemented yet.");

    public void CloseCommand() => Application.Current.GetMainWindow().Close();

    public void SaveScreenshot() => Logger.Instance.Info("Screenshot export is not implemented yet.");

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
            var romDevice = new SmsRomDevice(biosData);
            m_cpu.Bus.Attach(romDevice);
            m_cpu.Bus.Attach(new SmsMapperDevice(romDevice));
            LogRomInfo(biosFile, biosData, romDevice.BankCount);
            StartCpuIfNeeded();
            return;
        }

        m_cpu.Bus.Attach(new BiosRomDevice(biosData));
        LogRomInfo(biosFile, biosData, bankCount: 1);
        StartCpuIfNeeded();
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

    public void ExportTileMap() => Logger.Instance.Info("Tile map export is not implemented yet.");

    public void ResetDevice() => Logger.Instance.Info("Reset is not implemented yet.");

    public void DumpCpuHistory() =>
        m_cpu.InstructionLogger.DumpToConsole();

    public void ReportCpuClockTicks() => Logger.Instance.Info("CPU clock ticks are not implemented yet.");

    public void TrackCpuHistory() => IsCpuHistoryTracked = !IsCpuHistoryTracked;

    private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.IsSoundEnabled))
            return;
        if (e.PropertyName == nameof(Settings.IsCpuHistoryTracked))
        {
            IsCpuHistoryTracked = Settings.IsCpuHistoryTracked;
        }
    }

    internal void LoadRomFile(FileInfo romFile, bool addToMru = true)
    {
        if (romFile == null)
            return;
        if (!romFile.Exists)
        {
            Logger.Instance.Warn($"Unable to load ROM '{romFile.FullName}': File not found.");
            return;
        }

        if (addToMru)
            Mru.Add(romFile);

        m_currentRomTitle = romFile.Name;
        WindowTitle = $"MasterG33k - {m_currentRomTitle}";
    }

    public void Dispose()
    {
        StopCpu();
        Settings.MruFiles = Mru.AsString();
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void StartCpuIfNeeded()
    {
        if (m_cpuThread != null)
            return;

        m_shutdownRequested = false;
        m_clockSync.Reset();
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
                    m_cpu.Step();
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

    private static IImage CreateBlackDisplay()
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(256, 192),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var fb = bitmap.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        for (var i = 3; i < bytes.Length; i += 4)
            bytes[i] = 255;
        Marshal.Copy(bytes, 0, fb.Address, bytes.Length);

        return bitmap;
    }

    private static double GetEffectiveCpuHz() =>
        // Master System NTSC CPU clock; we always render 256x192.
        3_579_545;
}
