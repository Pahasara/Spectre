using Dapper;
using Microsoft.Data.Sqlite;

[module: DapperAot]
namespace Spectre.Core;

public class TrackerRepository
{
    private readonly string _connString;

    public TrackerRepository()
    {
        _connString = $"Data Source={AppPaths.DbPath}";
        InitializeSchema();
    }

    private SqliteConnection Connection() => new(_connString);

    private void InitializeSchema()
    {
        using var conn = Connection();
        conn.Execute("""
                     CREATE TABLE IF NOT EXISTS TrackedApps (
                         Id INTEGER PRIMARY KEY AUTOINCREMENT,
                         ProcessName TEXT NOT NULL UNIQUE,
                         DisplayName TEXT NOT NULL,
                         IconPath TEXT
                     );

                     CREATE TABLE IF NOT EXISTS AppSessions (
                         Id INTEGER PRIMARY KEY AUTOINCREMENT,
                         AppId INTEGER NOT NULL,
                         StartTime TEXT NOT NULL,
                         EndTime TEXT,
                         DurationSeconds INTEGER NOT NULL DEFAULT 0,
                         FOREIGN KEY (AppId) REFERENCES TrackedApps(Id)
                     );
                     """);
    }

    public async Task<int> AddTrackedAppAsync(TrackedApp app)
    {
        await using var conn = Connection();
        return await conn.ExecuteScalarAsync<int>("""
                                                  INSERT INTO TrackedApps (ProcessName, DisplayName, IconPath)
                                                  VALUES (@ProcessName, @DisplayName, @IconPath);
                                                  SELECT last_insert_rowid();
                                                  """, app);
    }

    public async Task<IEnumerable<TrackedApp>> GetTrackedAppsAsync()
    {
        await using var conn = Connection();
        return await conn.QueryAsync<TrackedApp>("SELECT * FROM TrackedApps");
    }

    public async Task<int> StartSessionAsync(int appId, DateTime startTime)
    {
        await using var conn = Connection();
        return await conn.ExecuteScalarAsync<int>("""
                                                  INSERT INTO AppSessions (AppId, StartTime, DurationSeconds)
                                                  VALUES (@appId, @startTime, 0);
                                                  SELECT last_insert_rowid();
                                                  """, new { appId, startTime });
    }

    public async Task EndSessionAsync(int sessionId, DateTime endTime, long durationSeconds)
    {
        await using var conn = Connection();
        await conn.ExecuteAsync("""
                                UPDATE AppSessions
                                SET EndTime = @endTime, DurationSeconds = @durationSeconds
                                WHERE Id = @sessionId
                                """, new { sessionId, endTime, durationSeconds });
    }

    public async Task<IEnumerable<AppSession>> GetSessionsForAppAsync(int appId)
    {
        await using var conn = Connection();
        return await conn.QueryAsync<AppSession>(
            "SELECT * FROM AppSessions WHERE AppId = @appId ORDER BY StartTime DESC",
            new { appId });
    }
    
    public async Task<bool> UpdateAppAsync(int appId, string processName, string displayName)
    {
        await using var conn = Connection();
        try
        {
            await conn.ExecuteAsync(
                "UPDATE TrackedApps SET ProcessName = @processName, DisplayName = @displayName WHERE Id = @appId",
                new { appId, processName, displayName });
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE constraint violation
        {
            return false;
        }
    }

    public async Task DeleteTrackedAppAsync(int appId)
    {
        await using var conn = Connection();
        await conn.ExecuteAsync("DELETE FROM AppSessions WHERE AppId = @appId", new { appId });
        await conn.ExecuteAsync("DELETE FROM TrackedApps WHERE Id = @appId", new { appId });
    }
}