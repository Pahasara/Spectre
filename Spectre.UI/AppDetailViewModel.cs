using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectre.Core;

namespace Spectre.UI;

public partial class AppDetailViewModel(TrackerRepository repo, ProcessTrackerService tracker) : ObservableObject
{
    private int _appId;
    private string _processName = string.Empty;
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _refreshing;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _editableDisplayName = string.Empty;

    [ObservableProperty]
    private string _editableProcessName = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<SessionRow> Sessions { get; } = new();
    public string TotalTimeFormatted { get; private set; } = "0h 00m 00s";
    public string SessionCountText => $"{Sessions.Count} session{(Sessions.Count == 1 ? string.Empty : "s")}";
    public bool IsRunning { get; private set; }
    public string StatusText => IsRunning ? "RUNNING NOW" : "NOT RUNNING";
    public string StatusColor => IsRunning ? "#4ADE80" : "#53627A";

    public event Action? RequestClose;

    public async Task LoadAsync(TrackedApp app)
    {
        _appId = app.Id;
        DisplayName = app.DisplayName;
        EditableDisplayName = app.DisplayName;
        EditableProcessName = app.ProcessName;
        _processName = app.ProcessName;

        await RefreshLiveStateAsync();
        _liveTimer.Tick -= OnLiveTimerTick;
        _liveTimer.Tick += OnLiveTimerTick;
        _liveTimer.Start();
    }

    private async void OnLiveTimerTick(object? sender, EventArgs e) => await RefreshLiveStateAsync();

    private async Task RefreshLiveStateAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var sessions = (await repo.GetSessionsForAppAsync(_appId)).ToList();
            var wasRunning = IsRunning;
            IsRunning = tracker.IsProcessRunning(_processName);

            Sessions.Clear();
            foreach (var session in sessions)
            {
                Sessions.Add(new SessionRow(
                    session.StartTime.ToLocalTime().ToString("MMM d, HH:mm"),
                    DurationFormatter.Format(session)));
            }

            TotalTimeFormatted = DurationFormatter.Format(
                sessions.Sum(s => DurationFormatter.GetTotalSeconds(s, DateTime.UtcNow)));
            OnPropertyChanged(nameof(TotalTimeFormatted));
            OnPropertyChanged(nameof(SessionCountText));
            if (wasRunning != IsRunning)
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void StopLiveUpdates()
    {
        _liveTimer.Stop();
        _liveTimer.Tick -= OnLiveTimerTick;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        var newProcessName = EditableProcessName.Trim();
        var newDisplayName = EditableDisplayName.Trim();

        if (string.IsNullOrWhiteSpace(newProcessName) || string.IsNullOrWhiteSpace(newDisplayName))
        {
            ErrorMessage = "Both fields are required.";
            return;
        }

        var success = await repo.UpdateAppAsync(_appId, newProcessName, newDisplayName);
        if (!success)
        {
            ErrorMessage = $"\"{newProcessName}\" is already tracked under another app.";
            return;
        }

        DisplayName = newDisplayName;
        _processName = newProcessName;
        await tracker.RefreshTrackedAppsAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        await repo.DeleteTrackedAppAsync(_appId);
        await tracker.RefreshTrackedAppsAsync();
        RequestClose?.Invoke();
    }
}

public record SessionRow(string StartedAt, string Duration);

internal static class DurationFormatter
{
    public static long GetTotalSeconds(AppSession session, DateTime nowUtc)
    {
        if (session.EndTime is null)
        {
            return Math.Max(0, (long)(nowUtc - session.StartTime).TotalSeconds);
        }

        return session.DurationSeconds;
    }

    public static string Format(AppSession session)
    {
        return Format(GetTotalSeconds(session, DateTime.UtcNow));
    }

    public static string Format(long totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        var totalHours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return $"{totalHours}h {minutes:00}m {seconds:00}s";
    }
}