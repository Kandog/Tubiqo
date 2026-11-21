using Microsoft.Data.Sqlite;

namespace H2o.Core.Storage;

/// <summary>
/// The SQLite file that holds h2o Data (Watch History and h2o's own settings).
/// It lives next to, never inside, the Site Data folder, so wiping Site Data can't touch it.
/// </summary>
public sealed class H2oDatabase : IDisposable
{
    const int SchemaVersion = 1;

    readonly SqliteConnection connection;

    H2oDatabase(SqliteConnection connection) => this.connection = connection;

    public static H2oDatabase Open(string path)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        var connection = new SqliteConnection(cs);
        connection.Open();
        var db = new H2oDatabase(connection);
        db.Migrate();
        return db;
    }

    void Migrate()
    {
        Execute("PRAGMA journal_mode = WAL;");
        var version = Convert.ToInt32(Scalar("PRAGMA user_version;"));
        if (version >= SchemaVersion) return;

        using var tx = connection.BeginTransaction();
        Execute("""
            CREATE TABLE IF NOT EXISTS watch_entry (
                video_id         TEXT    PRIMARY KEY,
                title            TEXT    NOT NULL,
                channel          TEXT    NOT NULL,
                duration_s       REAL    NOT NULL,
                resume_s         REAL    NOT NULL,
                last_watched_ms  INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_watch_entry_last ON watch_entry (last_watched_ms DESC);
            CREATE TABLE IF NOT EXISTS setting (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);
        Execute($"PRAGMA user_version = {SchemaVersion};");
        tx.Commit();
    }

    public SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        return cmd.ExecuteNonQuery();
    }

    public object? Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        return cmd.ExecuteScalar();
    }

    public string? GetSetting(string key) => Scalar("SELECT value FROM setting WHERE key = $k", ("$k", key)) as string;

    public void SetSetting(string key, string value) =>
        Execute("INSERT INTO setting (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$k", key), ("$v", value));

    public void Dispose() => connection.Dispose();
}
