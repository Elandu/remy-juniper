using Microsoft.Data.Sqlite;

namespace LostAndFound;

/// <summary>
/// Writes Tokens straight into Nadeko's own database so rewards show up in .balance.
/// Uses an atomic upsert on DiscordUser.UserId (a UNIQUE index in Nadeko's schema) with
/// WAL + busy timeout + retries so it is safe alongside the bot's own writes.
/// </summary>
public sealed class EconomyBridge
{
    private readonly string _path;
    private readonly string _connString;
    private readonly bool _available;

    public bool Available => _available;

    public EconomyBridge(string nadekoDbPath)
    {
        _path = nadekoDbPath;
        _available = !string.IsNullOrEmpty(nadekoDbPath) && File.Exists(nadekoDbPath);
        _connString = new SqliteConnectionStringBuilder
        {
            DataSource = nadekoDbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();
    }

    public static string DefaultPath(string assemblyDir)
        => Path.Combine(assemblyDir, "..", "..", "NadekoBot.db");

    /// <summary>Adds <paramref name="amount"/> Tokens. Returns true if the write succeeded.</summary>
    public bool Award(ulong userId, int amount)
    {
        if (!_available || amount == 0)
            return false;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var c = new SqliteConnection(_connString);
                c.Open();
                using (var pragma = c.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA busy_timeout=5000;";
                    pragma.ExecuteNonQuery();
                }

                // Preferred path: unique constraint on UserId (Nadeko: AK_DiscordUser_UserId).
                try
                {
                    using var cmd = c.CreateCommand();
                    cmd.CommandText =
                        "INSERT INTO DiscordUser (UserId, Username, CurrencyAmount, TotalXp, IsClubAdmin) " +
                        "VALUES ($u, '??Unknown', $a, 0, 0) " +
                        "ON CONFLICT(UserId) DO UPDATE SET CurrencyAmount = CurrencyAmount + $a;";
                    cmd.Parameters.AddWithValue("$u", Db.L(userId));
                    cmd.Parameters.AddWithValue("$a", amount);
                    cmd.ExecuteNonQuery();
                    return true;
                }
                catch (SqliteException)
                {
                    // Fallback for a schema without a matching unique index: update then insert.
                    using (var upd = c.CreateCommand())
                    {
                        upd.CommandText = "UPDATE DiscordUser SET CurrencyAmount = CurrencyAmount + $a WHERE UserId = $u;";
                        upd.Parameters.AddWithValue("$u", Db.L(userId));
                        upd.Parameters.AddWithValue("$a", amount);
                        if (upd.ExecuteNonQuery() > 0)
                            return true;
                    }

                    using var ins = c.CreateCommand();
                    ins.CommandText =
                        "INSERT INTO DiscordUser (UserId, Username, CurrencyAmount, TotalXp, IsClubAdmin) " +
                        "VALUES ($u, '??Unknown', $a, 0, 0);";
                    ins.Parameters.AddWithValue("$u", Db.L(userId));
                    ins.Parameters.AddWithValue("$a", amount);
                    ins.ExecuteNonQuery();
                    return true;
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
            {
                // database is locked/busy - brief backoff and retry
                Thread.Sleep(50 * (attempt + 1));
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    public long GetBalance(ulong userId)
    {
        if (!_available)
            return -1;
        try
        {
            using var c = new SqliteConnection(_connString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT CurrencyAmount FROM DiscordUser WHERE UserId = $u;";
            cmd.Parameters.AddWithValue("$u", Db.L(userId));
            var result = cmd.ExecuteScalar();
            return result is null || result is DBNull ? 0 : Convert.ToInt64(result);
        }
        catch
        {
            return -1;
        }
    }
}
