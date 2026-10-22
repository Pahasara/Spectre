namespace Spectre.Core;

public record TrackedApp
{
    public int Id { get; init; }
    public required string ProcessName { get; init; }
    public required string DisplayName { get; init; }
    public string? IconPath { get; init; }
}

public record AppSession
{
    public int Id { get; init; }
    public int AppId { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public long DurationSeconds { get; init; }
}

public record AppTotal
{
    public int AppId { get; init; }
    public long TotalSeconds { get; init; }
}