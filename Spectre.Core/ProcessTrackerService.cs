using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Spectre.Core;

public sealed class ProcessTrackerService(TrackerRepository repo, ConfigService config)
{
    private readonly ConcurrentDictionary<string, (int SessionId, DateTime StartTime)> _activeSessions =
        new(StringComparer.OrdinalIgnoreCase);

    private volatile Dictionary<string, int> _trackedAppIds = new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    public async Task StopAsync()
    {
        if (_loop is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RefreshTrackedAppsAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Initial tracked-app load failed: {ex}");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (config.Current.MonitoringEnabled)
                    await PollOnceAsync();
                else
                    await CloseAllActiveSessionsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Poll cycle failed: {ex}");
            }

            try
            {
                var interval = TimeSpan.FromSeconds(Math.Max(1, config.Current.PollIntervalSeconds));
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // fall through so the final cleanup below actually runs
            }
        }

        await CloseAllActiveSessionsAsync();
    }

    public async Task RefreshTrackedAppsAsync()
    {
        var apps = await repo.GetTrackedAppsAsync();
        _trackedAppIds = apps.ToDictionary(a => a.ProcessName, a => a.Id, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsProcessRunning(string processName) =>
        _activeSessions.ContainsKey(processName);

    private async Task PollOnceAsync()
    {
        var tracked = _trackedAppIds;
        if (tracked.Count == 0 && _activeSessions.IsEmpty) return; // nothing to watch

        var runningNames = GetRunningProcessNames();

        foreach (var (processName, appId) in tracked)
        {
            bool isRunning = runningNames.Contains(processName);
            bool wasTracked = _activeSessions.ContainsKey(processName);

            if (isRunning && !wasTracked)
            {
                var start = DateTime.UtcNow;
                var sessionId = await repo.StartSessionAsync(appId, start);
                _activeSessions[processName] = (sessionId, start);
            }
            else if (!isRunning && wasTracked && _activeSessions.TryRemove(processName, out var session))
            {
                var end = DateTime.UtcNow;
                await repo.EndSessionAsync(session.SessionId, end, (long)(end - session.StartTime).TotalSeconds);
            }
        }
    }

    private async Task CloseAllActiveSessionsAsync()
    {
        foreach (var processName in _activeSessions.Keys)
        {
            if (!_activeSessions.TryRemove(processName, out var session)) continue;
            var end = DateTime.UtcNow;
            await repo.EndSessionAsync(session.SessionId, end, (long)(end - session.StartTime).TotalSeconds);
        }
    }

    private static HashSet<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try { names.Add(process.ProcessName); }
            catch { /* process exited or access denied */ }
            finally { process.Dispose(); }
        }
        return names;
    }
}
