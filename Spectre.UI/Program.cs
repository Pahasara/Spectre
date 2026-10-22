using Avalonia;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Core;
using Spectre.UI;

using var singleInstance = SingleInstanceCoordinator.TryCreate(
    AppPaths.SingleInstanceLockPath);

if (singleInstance is null)
{
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<MainWindowViewModel>();
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<TrackerRepository>();
builder.Services.AddSingleton<ProcessTrackerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProcessTrackerService>());

var host = builder.Build();
host.Start();

BuildAvaloniaApp()
    .AfterSetup(_ =>
    {
        App.Host = host;
        App.SingleInstance = singleInstance;
    })
    .StartWithClassicDesktopLifetime(args);

host.StopAsync().GetAwaiter().GetResult();
return;

static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
