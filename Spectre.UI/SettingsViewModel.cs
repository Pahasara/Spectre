using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectre.Core;

namespace Spectre.UI;

public partial class SettingsViewModel(ConfigService config) : ObservableObject
{
    [ObservableProperty]
    private string _pollIntervalSeconds = config.Current.PollIntervalSeconds.ToString();

    [ObservableProperty]
    private string _uiRefreshIntervalSeconds = config.Current.UiRefreshIntervalSeconds.ToString();

    [ObservableProperty]
    private string? _errorMessage;

    public event Action? RequestClose;

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        if (!int.TryParse(PollIntervalSeconds, out var poll) || poll < 1)
        {
            ErrorMessage = "Poll interval must be a whole number of seconds (1+).";
            return;
        }

        if (!int.TryParse(UiRefreshIntervalSeconds, out var ui) || ui < 1)
        {
            ErrorMessage = "UI refresh interval must be a whole number of seconds (1+).";
            return;
        }

        config.Save(new AppConfig
        {
            PollIntervalSeconds = poll,
            UiRefreshIntervalSeconds = ui
        });

        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
