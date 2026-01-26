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
using Avalonia;
using Avalonia.Media;
using DTC.Core;
using DTC.Core.Commands;
using DTC.Core.Extensions;
using DTC.Core.UI;
using DTC.Core.ViewModels;
using DTC.Emulation;
using DTC.Emulation.Audio;
using DTC.Emulation.Rom;
using DTC.Emulation.Snapshot;
using DTC.Z80.Devices;

namespace MasterG33k.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int AudioSampleRateHz = 44100;
    private string m_windowTitle;
    private bool m_isCpuHistoryTracked;
    private readonly SmsMachine m_machine;
    private readonly MachineRunner m_machineRunner;
    private readonly EmulatorViewModel m_emulator;
    private readonly AudioChannelSettings m_audioChannels;
    private bool m_hasLoggedVideoStandard;
    private bool m_lastLoggedIsPal;
    private string m_loadedRomPath;
    private static readonly string[] RomExtensions = [".sms"];

    public MruFiles Mru { get; }
    public IImage Display { get; }
    public AboutInfo AboutInfo { get; } = AboutInfoProvider.Info;
    public SnapshotHistory SnapshotHistory { get; }

    public Settings Settings => Settings.Instance;

    public string WindowTitle
    {
        get => m_windowTitle ?? "MasterG33k";
        private set => SetField(ref m_windowTitle, value);
    }

    private string m_currentRomTitle = "MasterG33k";

    public bool IsRecording => m_emulator.IsRecording;

    public bool IsRecordingIndicatorOn => m_emulator.IsRecordingIndicatorOn;

    public event EventHandler DisplayUpdated;

    public bool IsDebugBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    public bool IsSoundChannel1Enabled
    {
        get => m_audioChannels.IsEnabled(1);
        private set
        {
            if (m_audioChannels.IsEnabled(1) == value)
                return;
            m_audioChannels.SetChannelEnabled(1, value);
            OnPropertyChanged();
        }
    }

    public bool IsSoundChannel2Enabled
    {
        get => m_audioChannels.IsEnabled(2);
        private set
        {
            if (m_audioChannels.IsEnabled(2) == value)
                return;
            m_audioChannels.SetChannelEnabled(2, value);
            OnPropertyChanged();
        }
    }

    public bool IsSoundChannel3Enabled
    {
        get => m_audioChannels.IsEnabled(3);
        private set
        {
            if (m_audioChannels.IsEnabled(3) == value)
                return;
            m_audioChannels.SetChannelEnabled(3, value);
            OnPropertyChanged();
        }
    }

    public bool IsSoundChannel4Enabled
    {
        get => m_audioChannels.IsEnabled(4);
        private set
        {
            if (m_audioChannels.IsEnabled(4) == value)
                return;
            m_audioChannels.SetChannelEnabled(4, value);
            OnPropertyChanged();
        }
    }

    public bool IsPalEnabled => Settings.IsPalEnabled;

    public bool IsNtscEnabled => !Settings.IsPalEnabled;

    public bool IsCpuHistoryTracked
    {
        get => m_isCpuHistoryTracked;
        private set
        {
            if (!SetField(ref m_isCpuHistoryTracked, value))
                return;
            Settings.IsCpuHistoryTracked = value;
#if DEBUG
            m_machine.Cpu.InstructionLogger.IsEnabled = value;
#endif
        }
    }

    public MainWindowViewModel()
    {
        Mru = new MruFiles().InitFromString(Settings.MruFiles);
        Mru.OpenRequested += (_, file) => LoadRomFromFile(file, addToMru: false);

        var screen = new LcdScreen(SmsVdp.FrameWidth, SmsVdp.FrameHeight);
        var audioSink = new SoundDevice(AudioSampleRateHz);

        var descriptor = CreateMachineDescriptor();
        m_machine = new SmsMachine(descriptor, audioSink);
        m_machine.UpdateDescriptor(descriptor);
        m_machine.Joypad.PausePressed += (_, _) => m_emulator?.TogglePause();

        m_machineRunner = new MachineRunner(m_machine, GetEffectiveCpuHz, e =>
        {
            Logger.Instance.Error($"Stopping CPU loop due to exception: {e.Message}");
        });
        m_emulator = new EmulatorViewModel(
            m_machine,
            m_machineRunner,
            audioSink,
            screen,
            GetVideoFrameRate,
            () => m_currentRomTitle,
            GetEffectiveCpuHz);
        m_emulator.DisplayUpdated += (_, _) => DisplayUpdated?.Invoke(this, EventArgs.Empty);
        m_emulator.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
        Display = m_emulator.Display;
        SnapshotHistory = m_emulator.SnapshotHistory;
        m_emulator.SetScreenEffectEnabled(Settings.IsCrtEmulationEnabled);
        m_audioChannels = new AudioChannelSettings(m_machine.Audio);
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        IsCpuHistoryTracked = Settings.IsCpuHistoryTracked;
        ApplyVideoStandardSetting();
        ApplySoundEnabledSetting();
        ApplyHardwareLowPassFilterSetting();
        ApplyLayerVisibility();
        ApplySoundChannelSettings();
#if DEBUG
        m_machine.Cpu.InstructionLogger.IsEnabled = IsCpuHistoryTracked;
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

    public void SetVideoStandardNtsc()
    {
        if (!Settings.IsPalEnabled)
            return;

        Settings.IsPalEnabled = false;
        ApplyVideoStandardSetting();
    }

    public void SetVideoStandardPal()
    {
        if (Settings.IsPalEnabled)
            return;

        Settings.IsPalEnabled = true;
        ApplyVideoStandardSetting();
    }

    public void ToggleBackgroundVisibility()
    {
        Settings.IsBackgroundVisible = !Settings.IsBackgroundVisible;
    }

    public void ToggleSpriteVisibility()
    {
        Settings.AreSpritesVisible = !Settings.AreSpritesVisible;
    }

    public void ToggleHardwareLowPassFilter()
    {
        Settings.IsHardwareLowPassFilterEnabled = !Settings.IsHardwareLowPassFilterEnabled;
        ApplyHardwareLowPassFilterSetting();
    }

    public void ToggleSoundChannel1()
    {
        IsSoundChannel1Enabled = !IsSoundChannel1Enabled;
    }

    public void ToggleSoundChannel2()
    {
        IsSoundChannel2Enabled = !IsSoundChannel2Enabled;
    }

    public void ToggleSoundChannel3()
    {
        IsSoundChannel3Enabled = !IsSoundChannel3Enabled;
    }

    public void ToggleSoundChannel4()
    {
        IsSoundChannel4Enabled = !IsSoundChannel4Enabled;
    }

    public void ToggleRecording()
    {
        m_emulator.ToggleRecording();
    }

    public void StartRecording()
    {
        m_emulator.StartRecording();
    }

    public void StopRecording()
    {
        m_emulator.StopRecording();
    }

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
        var prefix = RomNameHelper.GetSafeFileBaseName(m_currentRomTitle, "MasterG33k");
        var defaultName = $"{prefix}.tga";
        var command = new FileSaveCommand("Save Screenshot", "TGA Files", ["*.tga"], defaultName);
        command.FileSelected += (_, info) =>
        {
            m_emulator.SaveScreenshot(info);
        };
        command.Execute(null);
    }

    private static void LogRomInfo(FileInfo romFile, byte[] romData)
    {
        var name = romFile.LeafName();
        var headerOffset = FindSmsHeaderOffset(romData);
        var manufacturer = headerOffset >= 0 ? "SEGA" : "Unknown";
        var sizeKb = romData.Length / 1024.0;
        Logger.Instance.Info($"ROM loaded: {name} ({manufacturer}, {sizeKb:0.#} KB)");
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
        m_emulator.Reset();
        if (!string.IsNullOrEmpty(m_loadedRomPath))
            SnapshotHistory?.ResetForRom(m_loadedRomPath);
        Logger.Instance.Info("CPU reset.");
    }

    public void DumpCpuHistory() =>
        m_machine.Cpu.InstructionLogger.DumpToConsole();

    public void ReportCpuClockTicks() =>
        Console.WriteLine($"CPU clock ticks: {m_machine.CpuTicks}");

    public void ReportFrameChecksum()
    {
        if (!m_emulator.HasFrameChecksum)
        {
            Console.WriteLine($"Frame checksum: n/a (no frame rendered yet). CPU ticks: {m_emulator.CpuTicks}");
            return;
        }

        Console.WriteLine($"Frame checksum: 0x{m_emulator.LastFrameChecksum:X8} @ CPU ticks {m_emulator.LastFrameTicks} (current {m_emulator.CpuTicks}).");
    }

    public void TrackCpuHistory() => IsCpuHistoryTracked = !IsCpuHistoryTracked;

    public void SetInputActive(bool isActive) =>
        m_machine.SetInputActive(isActive);

    private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Settings.IsSoundEnabled):
                ApplySoundEnabledSetting();
                return;
            case nameof(Settings.IsHardwareLowPassFilterEnabled):
                ApplyHardwareLowPassFilterSetting();
                return;
            case nameof(Settings.IsPalEnabled):
                ApplyVideoStandardSetting();
                OnPropertyChanged(nameof(IsPalEnabled));
                OnPropertyChanged(nameof(IsNtscEnabled));
                return;
            case nameof(Settings.IsCrtEmulationEnabled):
                m_emulator.SetScreenEffectEnabled(Settings.IsCrtEmulationEnabled);
                return;
            case nameof(Settings.IsBackgroundVisible):
            case nameof(Settings.AreSpritesVisible):
                ApplyLayerVisibility();
                return;
            case nameof(Settings.IsCpuHistoryTracked):
                IsCpuHistoryTracked = Settings.IsCpuHistoryTracked;
                break;
        }
    }

    private void ApplyLayerVisibility()
    {
        m_machine.SetLayerVisibility(Settings.IsBackgroundVisible, Settings.AreSpritesVisible);
    }

    private void ApplySoundEnabledSetting() =>
        m_emulator.AudioDevice.SetEnabled(Settings.IsSoundEnabled);

    private void ApplyHardwareLowPassFilterSetting() =>
        m_emulator.AudioDevice.SetLowPassFilterEnabled(Settings.IsHardwareLowPassFilterEnabled);

    private void ApplyVideoStandardSetting()
    {
        var isPal = Settings.IsPalEnabled;
        m_machine.SetVideoStandard(isPal);
        UpdateMachineDescriptor();
        m_machineRunner.ResyncClock();
        SnapshotHistory?.SetTicksPerSample((ulong)GetEffectiveCpuHz());

        if (m_hasLoggedVideoStandard && m_lastLoggedIsPal == isPal)
            return;
        m_hasLoggedVideoStandard = true;
        m_lastLoggedIsPal = isPal;
        var label = isPal ? "PAL (50Hz)" : "NTSC (60Hz)";
        Logger.Instance.Info($"Video standard: {label}");
    }

    private void ApplySoundChannelSettings()
    {
        m_audioChannels.ApplyAll();
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

        var (romName, romData) = RomLoader.ReadRomData(romFile, RomExtensions);
        if (romData == null || romData.Length == 0)
        {
            Logger.Instance.Warn($"Unable to load ROM '{romFile.FullName}': No valid ROM data found.");
            return false;
        }

        m_emulator.Stop();

        m_machine.LoadRom(romData, romName);

        if (addToMru)
            Mru.Add(romFile);
        Settings.LastRomPath = romFile.FullName;
        m_loadedRomPath = romFile.FullName;

        m_currentRomTitle = RomNameHelper.GetDisplayName(romName) ?? "MasterG33k";
        WindowTitle = RomNameHelper.BuildWindowTitle("MasterG33k", m_currentRomTitle);
        SnapshotHistory?.ResetForRom(m_loadedRomPath);
        m_emulator.Start();
        LogRomInfo(romFile, romData);
        return true;
    }

    private void UpdateMachineDescriptor()
    {
        var cpuHz = GetEffectiveCpuHz();
        var videoHz = cpuHz / (SmsVdp.CyclesPerScanline * m_machine.Vdp.TotalScanlines);
        var descriptor = new MachineDescriptor
        {
            Name = "MasterG33k",
            CpuHz = cpuHz,
            VideoHz = videoHz,
            AudioSampleRateHz = AudioSampleRateHz,
            FrameWidth = SmsVdp.FrameWidth,
            FrameHeight = SmsVdp.FrameHeight
        };
        m_machine.UpdateDescriptor(descriptor);
    }

    private MachineDescriptor CreateMachineDescriptor() => new()
    {
        Name = "MasterG33k",
        CpuHz = GetEffectiveCpuHz(),
        VideoHz = 0,
        AudioSampleRateHz = AudioSampleRateHz,
        FrameWidth = SmsVdp.FrameWidth,
        FrameHeight = SmsVdp.FrameHeight
    };

    public void Dispose()
    {
        m_emulator.Dispose();
        m_emulator.Stop();
        m_machineRunner.Dispose();
        m_machine.Joypad.Dispose();
        m_emulator.AudioDevice.Dispose();
        Settings.MruFiles = Mru.AsString();
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    // Master System CPU clock; we always render 256x192.
    private double GetEffectiveCpuHz() =>
        Settings.IsPalEnabled ? 3_546_895 : 3_579_545;

    private double GetVideoFrameRate() =>
        GetEffectiveCpuHz() / (SmsVdp.CyclesPerScanline * m_machine.Vdp.TotalScanlines);

}
