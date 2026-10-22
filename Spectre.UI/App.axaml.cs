using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Spectre.UI;

public partial class App : Application
{
    public static IHost? Host { get; set; }
    internal static SingleInstanceCoordinator? SingleInstance { get; set; }
    private Window? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var window = new MainWindow
            {
                DataContext = Host!.Services.GetRequiredService<MainWindowViewModel>()
            };
            _mainWindow = window;

            window.Closing += (_, e) =>
            {
                e.Cancel = true;
                window.Hide();
            };

            desktop.MainWindow = window;
            SetupTrayIcon(desktop, window);
            ShowMainWindow();
            desktop.ShutdownRequested += (_, _) => SingleInstance?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        ShowWindow(_mainWindow);
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, Window window)
    {
        var iconUri = new Uri("avares://Spectre/Assets/Icons/tray-icon.ico");

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(iconUri)),
            ToolTipText = "Spectre",
            Menu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem("Show") { Command = new RelayCommand(() => ShowWindow(window)) },
                    new NativeMenuItem("Quit") { Command = new RelayCommand(() => desktop.Shutdown()) }
                }
            }
        };

        tray.Clicked += (_, _) =>
        {
            if (window.IsVisible)
                window.Hide();
            else
                ShowWindow(window);
        };

        tray.IsVisible = true;
        TrayIcon.SetIcons(this, [tray]);
    }

    private static void ShowWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();

        if (window.DataContext is MainWindowViewModel vm)
            _ = vm.LoadAppsAsync();
    }
}
