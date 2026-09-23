using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectre.Core;

namespace Spectre.UI;

public partial class AppDetailViewModel(TrackerRepository repo, ProcessTrackerService tracker) : ObservableObject
{
    private int _appId;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _editableDisplayName = string.Empty;

    [ObservableProperty]
    private string _editableProcessName = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<SessionRow> Sessions { get; } = new();

    public event Action? RequestClose;

    public async Task LoadAsync(TrackedApp app)
    {
        _appId = app.Id;
        DisplayName = app.DisplayName;
        EditableDisplayName = app.DisplayName;
        EditableProcessName = app.ProcessName;

        Sessions.Clear();
        var sessions = await repo.GetSessionsForAppAsync(app.Id);
        foreach (var s in sessions.Where(s => s.EndTime is not null))
        {
            Sessions.Add(new SessionRow(
                s.StartTime.ToLocalTime().ToString("MMM d, HH:mm"),
                TimeSpan.FromSeconds(s.DurationSeconds).ToString(@"hh\:mm\:ss")));
        }
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
