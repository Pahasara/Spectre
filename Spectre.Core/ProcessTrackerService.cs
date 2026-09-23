using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Spectre.Core;

public class ProcessTrackerService(TrackerRepository repo, ConfigService config, ILogger<ProcessTrackerService> logger)
    : BackgroundService
{
    private readonly Dictionary<string, (int SessionId, DateTime StartTime)> _activeSessions = new();
    private Dictionary<string, int> _trackedAppIds = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshTrackedAppsAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Poll cycle failed");
            }

            var interval = TimeSpan.FromSeconds(Math.Max(1, config.Current.PollIntervalSeconds));
            await Task.Delay(interval, stoppingToken);
        }

        await CloseAllActiveSessionsAsync();
    }

    public async Task RefreshTrackedAppsAsync()
    {
        var apps = await repo.GetTrackedAppsAsync();
        _trackedAppIds = apps.ToDictionary(a => a.ProcessName, a => a.Id, StringComparer.OrdinalIgnoreCase);
    }

    private async Task PollOnceAsync()
    {
        var runningNames = System.Diagnostics.Process.GetProcesses()
            .Select(p => SafeName(p))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (processName, appId) in _trackedAppIds)
        {
            bool isRunning = runningNames.Contains(processName);
            bool wasTracked = _activeSessions.ContainsKey(processName);

            switch (isRunning)
            {
                case true when !wasTracked:
                {
                    var start = DateTime.UtcNow;
                    var sessionId = await repo.StartSessionAsync(appId, start);
                    _activeSessions[processName] = (sessionId, start);
                    logger.LogInformation("Started tracking {Process}", processName);
                    break;
                }
                case false when wasTracked:
                {
                    var (sessionId, start) = _activeSessions[processName];
                    var end = DateTime.UtcNow;
                    await repo.EndSessionAsync(sessionId, end, (long)(end - start).TotalSeconds);
                    _activeSessions.Remove(processName);
                    logger.LogInformation("Stopped tracking {Process}", processName);
                    break;
                }
            }
        }
    }

    private async Task CloseAllActiveSessionsAsync()
    {
        foreach (var (processName, (sessionId, start)) in _activeSessions)
        {
            var end = DateTime.UtcNow;
            await repo.EndSessionAsync(sessionId, end, (long)(end - start).TotalSeconds);
        }
        _activeSessions.Clear();
    }

    private static string? SafeName(System.Diagnostics.Process p)
    {
        try { return p.ProcessName; }
        catch { return null; }
    }
}
