// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using DTC.Core.Settings;

namespace MasterG33k.ViewModels;

/// <summary>
/// Application settings.
/// </summary>
public class Settings : UserSettingsBase
{
    public static Settings Instance { get; } = new Settings();

    protected override void ApplyDefaults()
    {
        IsSoundEnabled = true;
        IsAmbientBlurred = false;
        IsBackgroundVisible = true;
        AreSpritesVisible = true;
        IsCpuHistoryTracked = false;
        MruFiles = string.Empty;
        LastRomPath = string.Empty;
    }

    public bool IsSoundEnabled
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool IsAmbientBlurred
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool IsBackgroundVisible
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool AreSpritesVisible
    {
        get => Get<bool>();
        set => Set(value);
    }


    public bool IsCpuHistoryTracked
    {
        get => Get<bool>();
        set => Set(value);
    }

    public string MruFiles
    {
        get => Get<string>();
        set => Set(value);
    }

    public string LastRomPath
    {
        get => Get<string>();
        set => Set(value);
    }
}
