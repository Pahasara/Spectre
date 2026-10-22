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

        // A forced quit can leave a session open forever. Finalize it before
        // the tracker starts a new session for currently running processes.
        var endTime = DateTime.UtcNow;
        var openSessions = conn.Query<AppSession>(
            "SELECT * FROM AppSessions WHERE EndTime IS NULL").AsList();
        foreach (var session in openSessions)
        {
            var durationSeconds = Math.Max(0, (long)(endTime - session.StartTime).TotalSeconds);
            conn.Execute("""
                         UPDATE AppSessions
                         SET EndTime = @endTime, DurationSeconds = @durationSeconds
                         WHERE Id = @sessionId
                         """, new { sessionId = session.Id, endTime, durationSeconds });
        }
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

    public async Task<IEnumerable<AppTotal>> GetAppTotalsAsync(DateTime nowUtc)
    {
        await using var conn = Connection();
        return await conn.QueryAsync<AppTotal>("""
            SELECT AppId,
                   COALESCE(SUM(
                       CASE WHEN EndTime IS NULL
                           THEN MAX(0, CAST((julianday(@nowUtc) - julianday(StartTime)) * 86400 AS INTEGER))
                           ELSE DurationSeconds
                       END
                   ), 0) AS TotalSeconds
            FROM AppSessions
            GROUP BY AppId
            """, new { nowUtc });
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