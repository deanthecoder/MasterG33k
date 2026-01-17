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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using CSharp.Core;
using MasterG33k.ViewModels;

namespace MasterG33k.Views;

public partial class MainWindow : Window
{
    private bool m_isLoaded;
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Activated += (_, _) => ViewModel?.SetInputActive(true);
        Deactivated += (_, _) => ViewModel?.SetInputActive(false);

        Logger.Instance.Info("Application starting.");
    }

    private void OnAboutDialogClicked(object sender, PointerPressedEventArgs e) =>
        Host.CloseDialogCommand.Execute(sender);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (m_isLoaded)
            return;
        m_isLoaded = true;

        Logger.Instance.Info("Window loaded.");

        var action = new Action(() =>
        {
            AmbientDisplay.InvalidateVisual();
            MainDisplay.InvalidateVisual();
        });
        ViewModel.DisplayUpdated += (_, _) =>
        {
            try
            {
                Dispatcher.UIThread.InvokeAsync(action);
            }
            catch (TaskCanceledException)
            {
            }
        };

        ViewModel?.SetInputActive(IsActive);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsDirectionalKey(e.Key))
            return;

        if (e.Source is MenuItem)
            return;

        e.Handled = true;
    }

    private static bool IsDirectionalKey(Key key) =>
        key is Key.Left or Key.Right or Key.Up or Key.Down;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.DragEffects = TryGetRomFile(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (TryGetRomFile(e, out var romFile))
            ViewModel.LoadRomFromFile(romFile, addToMru: true);
    }

    private static bool TryGetRomFile(DragEventArgs e, out FileInfo romFile)
    {
        romFile = null;

        var files = e.Data.GetFiles();
        if (files == null)
            return false;

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path) || !IsSupportedRom(path))
                continue;
            romFile = new FileInfo(path);
            return true;
        }

        return false;
    }

    private static bool IsSupportedRom(string path) =>
        path.EndsWith(".sms", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}
