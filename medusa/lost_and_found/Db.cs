using Microsoft.Data.Sqlite;

namespace LostAndFound;

/// <summary>
/// SQLite store for Lost &amp; Found. Lives at data/lostfound.db (two levels up from the
/// medusa assembly folder, i.e. alongside NadekoBot.db). All writes that must be atomic
/// run inside a BEGIN IMMEDIATE transaction.
/// </summary>
public sealed class Db
{
    private readonly string _path;
    private readonly string _connString;

    public string Path => _path;

    public Db(string path)
    {
        _path = path;
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    /// <summary>The default location: data/lostfound.db given the medusa assembly dir.</summary>
    public static string DefaultPath(string assemblyDir)
        => System.IO.Path.Combine(assemblyDir, "..", "..", "lostfound.db");

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connString);
        c.Open();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteScalar();
            cmd.CommandText = "PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();
        }
        return c;
    }

    public void Init()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();
    }

    // ---- low level helpers -------------------------------------------------

    public static SqliteParameter[] P(params (string name, object value)[] ps)
    {
        var list = new SqliteParameter[ps.Length];
        for (var i = 0; i < ps.Length; i++)
            list[i] = new SqliteParameter(ps[i].name, ps[i].value ?? DBNull.Value);
        return list;
    }

    public static long L(ulong v) => unchecked((long)v);
    public static ulong U(long v) => unchecked((ulong)v);
    public static long Now() => Clock.UnixNow();

    public int Execute(string sql, params (string, object)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (args.Length > 0) cmd.Parameters.AddRange(P(args));
        return cmd.ExecuteNonQuery();
    }

    public T Scalar<T>(string sql, params (string, object)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (args.Length > 0) cmd.Parameters.AddRange(P(args));
        var result = cmd.ExecuteScalar();
        if (result is null || result is DBNull) return default;
        return (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>Runs a body inside a BEGIN IMMEDIATE / COMMIT transaction. Rolls back on error.</summary>
    public T Immediate<T>(Func<SqliteConnection, T> body)
    {
        using var c = Open();
        using (var begin = c.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;";
            begin.ExecuteNonQuery();
        }
        try
        {
            var result = body(c);
            using (var commit = c.CreateCommand())
            {
                commit.CommandText = "COMMIT;";
                commit.ExecuteNonQuery();
            }
            return result;
        }
        catch
        {
            try
            {
                using var rb = c.CreateCommand();
                rb.CommandText = "ROLLBACK;";
                rb.ExecuteNonQuery();
            }
            catch { /* connection already broken */ }
            throw;
        }
    }

    public void Immediate(Action<SqliteConnection> body)
        => Immediate<object>(c => { body(c); return null; });

    public static int Run(SqliteConnection c, string sql, params (string, object)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (args.Length > 0) cmd.Parameters.AddRange(P(args));
        return cmd.ExecuteNonQuery();
    }

    public static T Get<T>(SqliteConnection c, string sql, params (string, object)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (args.Length > 0) cmd.Parameters.AddRange(P(args));
        var result = cmd.ExecuteScalar();
        if (result is null || result is DBNull) return default;
        return (T)Convert.ChangeType(result, typeof(T));
    }

    public static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string, object)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (args.Length > 0) cmd.Parameters.AddRange(P(args));
        var list = new List<T>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(map(r));
        return list;
    }

    private const string Schema = @"
CREATE TABLE IF NOT EXISTS players (
    user_id          INTEGER PRIMARY KEY,
    explorer_xp      INTEGER NOT NULL DEFAULT 0,
    level            INTEGER NOT NULL DEFAULT 1,
    total_finds      INTEGER NOT NULL DEFAULT 0,
    unique_finds     INTEGER NOT NULL DEFAULT 0,
    collection_score INTEGER NOT NULL DEFAULT 0,
    scraps           INTEGER NOT NULL DEFAULT 0,
    tokens_earned    INTEGER NOT NULL DEFAULT 0,
    favourite_item   TEXT    NOT NULL DEFAULT '',
    daily_used       INTEGER NOT NULL DEFAULT 0,
    daily_date       TEXT    NOT NULL DEFAULT '',
    created_at       INTEGER NOT NULL,
    updated_at       INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS inventory (
    user_id        INTEGER NOT NULL,
    item_id        TEXT    NOT NULL,
    qty            INTEGER NOT NULL DEFAULT 0,
    first_found_at INTEGER NOT NULL,
    PRIMARY KEY (user_id, item_id)
);
CREATE TABLE IF NOT EXISTS equipped (
    user_id     INTEGER PRIMARY KEY,
    item_id     TEXT    NOT NULL,
    equipped_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS completed_sets (
    user_id      INTEGER NOT NULL,
    set_id       TEXT    NOT NULL,
    completed_at INTEGER NOT NULL,
    PRIMARY KEY (user_id, set_id)
);
CREATE TABLE IF NOT EXISTS titles (
    user_id   INTEGER NOT NULL,
    title     TEXT    NOT NULL,
    earned_at INTEGER NOT NULL,
    PRIMARY KEY (user_id, title)
);
CREATE TABLE IF NOT EXISTS area_unlocks (
    user_id     INTEGER NOT NULL,
    area_id     TEXT    NOT NULL,
    unlocked_at INTEGER NOT NULL,
    PRIMARY KEY (user_id, area_id)
);
CREATE TABLE IF NOT EXISTS quests (
    user_id    INTEGER NOT NULL,
    quest_id   TEXT    NOT NULL,
    week_key   TEXT    NOT NULL,
    progress   INTEGER NOT NULL DEFAULT 0,
    target     INTEGER NOT NULL,
    completed  INTEGER NOT NULL DEFAULT 0,
    claimed    INTEGER NOT NULL DEFAULT 0,
    updated_at INTEGER NOT NULL,
    PRIMARY KEY (user_id, quest_id, week_key)
);
CREATE TABLE IF NOT EXISTS drops (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    guild_id         INTEGER NOT NULL,
    channel_id       INTEGER NOT NULL,
    item_id          TEXT    NOT NULL,
    total_copies     INTEGER NOT NULL,
    copies_remaining INTEGER NOT NULL,
    created_at       INTEGER NOT NULL,
    expires_at       INTEGER NOT NULL,
    active           INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS drop_claims (
    drop_id    INTEGER NOT NULL,
    user_id    INTEGER NOT NULL,
    claimed_at INTEGER NOT NULL,
    PRIMARY KEY (drop_id, user_id)
);
CREATE TABLE IF NOT EXISTS parcels (
    user_id   INTEGER NOT NULL,
    parcel_id TEXT    NOT NULL,
    qty       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, parcel_id)
);
CREATE TABLE IF NOT EXISTS discoveries (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id    INTEGER NOT NULL,
    item_id    TEXT    NOT NULL,
    rarity     TEXT    NOT NULL,
    created_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS temp_effects (
    user_id    INTEGER NOT NULL,
    effect_id  TEXT    NOT NULL,
    value      REAL    NOT NULL DEFAULT 0,
    expires_at INTEGER NOT NULL,
    PRIMARY KEY (user_id, effect_id)
);
CREATE TABLE IF NOT EXISTS trade_offers (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    from_user  INTEGER NOT NULL,
    to_user    INTEGER NOT NULL,
    item_id    TEXT    NOT NULL,
    created_at INTEGER NOT NULL,
    status     TEXT    NOT NULL DEFAULT 'pending'
);
CREATE INDEX IF NOT EXISTS ix_drops_channel ON drops (channel_id, active);
CREATE INDEX IF NOT EXISTS ix_discoveries_user ON discoveries (user_id, created_at);
CREATE INDEX IF NOT EXISTS ix_quests_user ON quests (user_id, week_key);
";
}

/// <summary>Testable clock.</summary>
public static class Clock
{
    public static Func<DateTimeOffset> Now = () => DateTimeOffset.UtcNow;

    public static long UnixNow() => Now().ToUnixTimeSeconds();
    public static DateTimeOffset UtcNow => Now().ToUniversalTime();
}
