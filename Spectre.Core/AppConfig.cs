namespace Spectre.Core;

public record AppConfig
{
    public int PollIntervalSeconds { get; init; } = 7;
    public int UiRefreshIntervalSeconds { get; init; } = 5;
}
