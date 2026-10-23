using System;
using System.Threading.Tasks;
using Avalonia;
using Spectre.Core;
using Spectre.UI;

// Exit immediately if another instance already holds the lock file.
using var singleInstance = SingleInstanceCoordinator.TryCreate(
    AppPaths.SingleInstanceLockPath);

if (singleInstance is null)
{
    // Already running (maybe hidden in the tray): ask that instance to show its window, then exit.
    SingleInstanceCoordinator.SignalExistingInstance();
    return;
}

var config = new ConfigService();
var repo = new TrackerRepository();
var tracker = new ProcessTrackerService(repo, config);
tracker.Start();

// Plain static hand-off to App: no AfterSetup lambda, so nothing captures the
// disposable singleInstance (that was the "captured variable is disposed" warning).
App.SingleInstance = singleInstance;
App.ViewModelFactory = () => new MainWindowViewModel(repo, tracker, config);

BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

// Run on the thread pool (the UI sync context is dead after shutdown) and cap the wait.
Task.Run(tracker.StopAsync).Wait(TimeSpan.FromSeconds(5));
return;

static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
