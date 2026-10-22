using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public ObservableCollection<TrackedAppRow> Apps { get; } = [];
    public ObservableCollection<TrackedAppRow> VisibleApps { get; } = [];

    [ObservableProperty]
    private string _newProcessName = string.Empty;

    [ObservableProperty]
    private string _newDisplayName = string.Empty;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _overallTimeFormatted = "0h 00m 00s";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _monitoringEnabled;

    public int TrackedCount => Apps.Count;
    public bool HasApps => Apps.Count > 0;
    public bool HasNoSearchResults => HasApps && VisibleApps.Count == 0;
    public string MonitoringText => MonitoringEnabled ? "Monitoring on" : "Monitoring off";
    public string MonitoringIcon => MonitoringEnabled ? "●" : "○";
    public string MonitoringColor => MonitoringEnabled ? "#4ADE80" : "#53627A";

    public MainWindowViewModel(TrackerRepository repo, ProcessTrackerService tracker, ConfigService config)
    {
        _repo = repo;
        _tracker = tracker;
        _config = config;
        MonitoringEnabled = config.Current.MonitoringEnabled;
        _ = LoadAppsAsync();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, _config.Current.UiRefreshIntervalSeconds))
        };
        _refreshTimer.Tick += async (_, _) => await LoadAppsAsync();
        _refreshTimer.Start();

        _config.ConfigChanged += cfg =>
            Dispatcher.UIThread.Post(() =>
            {
                _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, cfg.UiRefreshIntervalSeconds));
                MonitoringEnabled = cfg.MonitoringEnabled;
                OnPropertyChanged(nameof(MonitoringText));
                OnPropertyChanged(nameof(MonitoringIcon));
                OnPropertyChanged(nameof(MonitoringColor));
            });
    }

    public async Task LoadAppsAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        IsRefreshing = true;
        try
        {
            var apps = await _repo.GetTrackedAppsAsync();
            var totals = (await _repo.GetAppTotalsAsync(DateTime.UtcNow))
                .ToDictionary(total => total.AppId, total => total.TotalSeconds);
            var rows = new List<TrackedAppRow>();

            foreach (var app in apps)
            {
                totals.TryGetValue(app.Id, out var totalSeconds);
                rows.Add(new TrackedAppRow(
                    app.Id, app.DisplayName, app.ProcessName, totalSeconds,
                    _tracker.IsProcessRunning(app.ProcessName)));
            }

            Apps.Clear();
            foreach (var row in rows) Apps.Add(row);
            ApplyFilter();
            OverallTimeFormatted = DurationFormatter.Format(rows.Sum(row => row.TotalSeconds));
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? Apps
            : Apps.Where(row =>
                row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase));

        VisibleApps.Clear();
        foreach (var row in filtered) VisibleApps.Add(row);
        OnPropertyChanged(nameof(TrackedCount));
        OnPropertyChanged(nameof(HasApps));
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAppsAsync();

    [RelayCommand]
    private void ToggleMonitoring()
    {
        var next = !MonitoringEnabled;
        _config.Save(new AppConfig
        {
            MonitoringEnabled = next,
            PollIntervalSeconds = _config.Current.PollIntervalSeconds,
            UiRefreshIntervalSeconds = _config.Current.UiRefreshIntervalSeconds
        });
        MonitoringEnabled = next;
        OnPropertyChanged(nameof(MonitoringText));
        OnPropertyChanged(nameof(MonitoringIcon));
        OnPropertyChanged(nameof(MonitoringColor));
        _ = LoadAppsAsync();
    }

    [RelayCommand]
    private async Task AddProcessAsync()
    {
        ErrorMessage = null;
        var processName = NewProcessName.Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            ErrorMessage = "Enter an executable process name before adding it.";
            return;
        }

        var display = string.IsNullOrWhiteSpace(NewDisplayName) ? processName : NewDisplayName.Trim();
        try
        {
            await _repo.AddTrackedAppAsync(new TrackedApp
            {
                ProcessName = processName,
                DisplayName = display
            });
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            ErrorMessage = $"“{processName}” is already being tracked.";
            return;
        }

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
            Width = 580,
            Height = 680,
            MinWidth = 500,
            MinHeight = 580,
            Background = Application.Current!.FindResource("BgBrush") as IBrush,
            Content = new AppDetailView { DataContext = detailVm },
            Title = row.DisplayName
        };

        detailVm.RequestClose += detailWindow.Close;
        detailWindow.Closed += (_, _) => detailVm.StopLiveUpdates();
        detailWindow.Show();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsVm = new SettingsViewModel(_config);
        var settingsWindow = new Window
        {
            Width = 500,
            Height = 620,
            Background = Application.Current!.FindResource("BgBrush") as IBrush,
            Content = new SettingsView { DataContext = settingsVm },
            Title = "Settings"
        };

        settingsVm.RequestClose += settingsWindow.Close;
        settingsWindow.Show();
    }
}

public record TrackedAppRow(
    int Id, string DisplayName, string ProcessName, long TotalSeconds, bool IsRunning)
{
    public string TotalTimeFormatted => DurationFormatter.Format(TotalSeconds);
    public string StatusText => IsRunning ? "RUNNING NOW" : "NOT RUNNING";
    public string StatusColor => IsRunning ? "#4ADE80" : "#53627A";
}