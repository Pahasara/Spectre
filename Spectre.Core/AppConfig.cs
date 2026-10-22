namespace Spectre.Core;

public record AppConfig
{
    public bool MonitoringEnabled { get; init; } = true;
    public int PollIntervalSeconds { get; init; } = 5;
    public int UiRefreshIntervalSeconds { get; init; } = 3;
}
