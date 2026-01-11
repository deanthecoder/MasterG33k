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
using System.Reflection;
using System.Runtime.InteropServices;
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
        m_cpu = new Cpu(new Memory());
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
        Settings.MruFiles = Mru.AsString();
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
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
}
