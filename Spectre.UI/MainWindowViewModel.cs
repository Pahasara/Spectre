using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectre.Core;

namespace Spectre.UI;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly TrackerRepository _repo;
    private readonly ProcessTrackerService _tracker;
    private readonly ConfigService _config;
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<TrackedAppRow> Apps { get; } = [];

    [ObservableProperty]
    private string _newProcessName = string.Empty;

    [ObservableProperty]
    private string _newDisplayName = string.Empty;

    [ObservableProperty]
    private bool _isRefreshing;

    public MainWindowViewModel(TrackerRepository repo, ProcessTrackerService tracker, ConfigService config)
    {
        _repo = repo;
        _tracker = tracker;
        _config = config;
        _ = LoadAppsAsync();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, _config.Current.UiRefreshIntervalSeconds))
        };
        _refreshTimer.Tick += async (_, _) => await LoadAppsAsync();
        _refreshTimer.Start();

        _config.ConfigChanged += cfg =>
            Dispatcher.UIThread.Post(() =>
                _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, cfg.UiRefreshIntervalSeconds)));
    }

    public async Task LoadAppsAsync()
    {
        IsRefreshing = true;
        try
        {
            var apps = await _repo.GetTrackedAppsAsync();
            var rows = new List<TrackedAppRow>();

            foreach (var app in apps)
            {
                var sessions = await _repo.GetSessionsForAppAsync(app.Id);
                var totalSeconds = sessions.Sum(s => s.DurationSeconds);
                rows.Add(new TrackedAppRow(app.Id, app.DisplayName, app.ProcessName, totalSeconds));
            }

            Apps.Clear();
            foreach (var row in rows) Apps.Add(row);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAppsAsync();

    [RelayCommand]
    private async Task AddProcessAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProcessName)) return;

        var display = string.IsNullOrWhiteSpace(NewDisplayName) ? NewProcessName : NewDisplayName;
        await _repo.AddTrackedAppAsync(new TrackedApp
        {
            ProcessName = NewProcessName.Trim(),
            DisplayName = display.Trim()
        });

        NewProcessName = string.Empty;
        NewDisplayName = string.Empty;

        await _tracker.RefreshTrackedAppsAsync();
        await LoadAppsAsync();
    }

    [RelayCommand]
    private void OpenDetail(TrackedAppRow row)
    {
        _ = row ?? throw new ArgumentNullException(nameof(row));

        var detailVm = new AppDetailViewModel(_repo, _tracker);
        _ = detailVm.LoadAsync(new TrackedApp
        {
            Id = row.Id,
            ProcessName = row.ProcessName,
            DisplayName = row.DisplayName
        });

        var detailWindow = new Window
        {
            Width = 420,
            Height = 520,
            Background = Application.Current!.FindResource("BgBrush") as IBrush,
            Content = new AppDetailView { DataContext = detailVm },
            Title = row.DisplayName
        };

        detailVm.RequestClose += detailWindow.Close;
        detailWindow.Show();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsVm = new SettingsViewModel(_config);
        var settingsWindow = new Window
        {
            Width = 360,
            Height = 280,
            Background = Application.Current!.FindResource("BgBrush") as IBrush,
            Content = new SettingsView { DataContext = settingsVm },
            Title = "Settings"
        };

        settingsVm.RequestClose += settingsWindow.Close;
        settingsWindow.Show();
    }
}

public record TrackedAppRow(int Id, string DisplayName, string ProcessName, long TotalSeconds)
{
    public string TotalTimeFormatted => TimeSpan.FromSeconds(TotalSeconds).ToString(@"hh\:mm\:ss");
}
