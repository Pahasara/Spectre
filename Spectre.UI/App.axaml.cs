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

            window.Closing += (_, e) =>
            {
                e.Cancel = true;
                window.Hide();
            };

            desktop.MainWindow = window;
            SetupTrayIcon(desktop, window);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, Window window)
    {
        var iconUri = new Uri("avares://Spectre.UI/Assets/Icons/tray-icon.ico");

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
        window.Show();
        window.Activate();

        if (window.DataContext is MainWindowViewModel vm)
            _ = vm.LoadAppsAsync();
    }
}
