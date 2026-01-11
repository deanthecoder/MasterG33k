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
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CSharp.Core.UI;
using DialogHostAvalonia;
using Material.Icons;
using MasterG33k.ViewModels;

namespace MasterG33k.Views;

public class App : Application
{
    public App()
    {
        DataContext = new AppViewModel();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.MainWindow = mainWindow;

            desktop.MainWindow.Closed += (_, _) =>
            {
                viewModel.Dispose();
                Settings.Instance.Dispose();
            };
            mainWindow.Opened += (_, _) => EnsureBiosAvailable(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void EnsureBiosAvailable(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var biosFolder = Path.Combine(AppContext.BaseDirectory, "BIOS");
        var hasBios = Directory.Exists(biosFolder) &&
                      Directory.EnumerateFiles(biosFolder, "*.sms").Any();
        if (hasBios)
            return;

        DialogHost.Show(new MessageDialog
            {
                Message = "No Master System BIOS ROM found.",
                Detail = $"Copy your Sega Master System BIOS ROM (.sms) into:\n{biosFolder}",
                Icon = MaterialIconKind.AlertCircleOutline
            },
            (DialogClosingEventHandler)((_, _) => desktop.Shutdown()));
    }
}
