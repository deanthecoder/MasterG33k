// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any non-commercial
// purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using System;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CSharp.Core.UI;

namespace MasterG33k.ViewModels;

internal static class AboutInfoProvider
{
    public static AboutInfo Info => new AboutInfo
    {
        Title = "MasterG33k",
        Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown",
        Copyright = "Copyright © 2026 Dean Edis (DeanTheCoder).",
        WebsiteUrl = "https://github.com/deanthecoder",
        Icon = LoadIcon()
    };

    private static IImage LoadIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://MasterG33k/Assets/app.ico"));
        return new Bitmap(stream);
    }
}
