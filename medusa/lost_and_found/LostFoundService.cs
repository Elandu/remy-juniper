using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LostAndFound;

public sealed class GrantResult
{
    public ItemDef Item { get; set; }
    public bool IsNew { get; set; }
    public int Qty { get; set; }
    public int Xp { get; set; }
    public int Tokens { get; set; }
    public int LevelsGained { get; set; }
    public int CollectionValue { get; set; }
}

/// <summary>
/// Core player state: explorer XP/levels, totals, collection score, scraps, titles,
/// daily expedition usage and item granting (the one place inventory is written from rewards).
/// </summary>
public sealed class LostFoundService
{
    public Db Db { get; }
    public Catalog Catalog { get; }
    public EconomyBridge Economy { get; }
    public LostFoundConfig Cfg => Catalog.Cfg;

    private readonly TimeZoneInfo _tz;

    public LostFoundService(Db db, Catalog catalog, EconomyBridge economy)
    {
        Db = db;
        Catalog = catalog;
        Economy = economy;
        _tz = ResolveTimezone(catalog.Cfg.TimeZone);
    }

    internal static TimeZoneInfo ResolveTimezone(string id)
    {
        foreach (var candidate in new[] { id, "Australia/Sydney", "AUS Eastern Standard Time" })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch { /* try next */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("juniper+10", TimeSpan.FromHours(10), "AEST", "AEST");
    }

    public DateTimeOffset LocalNow => TimeZoneInfo.ConvertTime(Clock.Now(), _tz);
    public string DateKey() => LocalNow.ToString("yyyy-MM-dd");
    public string WeekKey()
    {
        var d = LocalNow.Date;
        return $"{ISOWeek.GetYear(d)}-W{ISOWeek.GetWeekOfYear(d):D2}";
    }

    // ---- low level helpers -------------------------------------------------

    public static Player EnsurePlayer(SqliteConnection c, ulong userId)
    {
        var now = Db.Now();
        Db.Run(c,
            "INSERT OR IGNORE INTO players(user_id, level, created_at, updated_at) VALUES($u, 1, $n, $n);",
            ("$u", Db.L(userId)), ("$n", now));
        return ReadPlayer(c, userId);
    }

    public static Player ReadPlayer(SqliteConnection c, ulong userId)
    {
        var rows = Db.Query(c,
            "SELECT user_id, explorer_xp, level, total_finds, unique_finds, collection_score, scraps, " +
            "tokens_earned, favourite_item, daily_used, daily_date, created_at, updated_at FROM players WHERE user_id=$u;",
            r => new Player
            {
                UserId = Db.U(r.GetInt64(0)),
                ExplorerXp = r.GetInt64(1),
                Level = r.GetInt32(2),
                TotalFinds = r.GetInt32(3),
                UniqueFinds = r.GetInt32(4),
                CollectionScore = r.GetInt32(5),
                Scraps = r.GetInt32(6),
                TokensEarned = r.GetInt32(7),
                FavouriteItem = r.IsDBNull(8) ? "" : r.GetString(8),
                DailyUsed = r.GetInt32(9),
                DailyDate = r.IsDBNull(10) ? "" : r.GetString(10),
                CreatedAt = r.GetInt64(11),
                UpdatedAt = r.GetInt64(12)
            },
            ("$u", Db.L(userId)));
        return rows.Count > 0 ? rows[0] : new Player { UserId = userId };
    }

    public static void SavePlayer(SqliteConnection c, Player p)
    {
        Db.Run(c,
            "UPDATE players SET explorer_xp=$x, level=$l, total_finds=$tf, unique_finds=$uf, collection_score=$cs, " +
            "scraps=$s, tokens_earned=$te, favourite_item=$f, daily_used=$du, daily_date=$dd, updated_at=$n WHERE user_id=$u;",
            ("$x", p.ExplorerXp), ("$l", p.Level), ("$tf", p.TotalFinds), ("$uf", p.UniqueFinds),
            ("$cs", p.CollectionScore), ("$s", p.Scraps), ("$te", p.TokensEarned),
            ("$f", p.FavouriteItem ?? ""), ("$du", p.DailyUsed), ("$dd", p.DailyDate ?? ""),
            ("$n", Db.Now()), ("$u", Db.L(p.UserId)));
    }

    public Player Get(ulong userId)
    {
        using var c = Db.Open();
        return EnsurePlayer(c, userId);
    }

    public static int GetQty(SqliteConnection c, ulong userId, string itemId)
        => Db.Get<int>(c, "SELECT COALESCE(qty,0) FROM inventory WHERE user_id=$u AND item_id=$i;",
            ("$u", Db.L(userId)), ("$i", itemId));

    public static int AddInventory(SqliteConnection c, ulong userId, string itemId, int delta)
    {
        if (delta >= 0)
            Db.Run(c,
                "INSERT INTO inventory(user_id, item_id, qty, first_found_at) VALUES($u,$i,$d,$t) " +
                "ON CONFLICT(user_id, item_id) DO UPDATE SET qty = qty + $d;",
                ("$u", Db.L(userId)), ("$i", itemId), ("$d", delta), ("$t", Db.Now()));
        else
            Db.Run(c, "UPDATE inventory SET qty = qty + $d WHERE user_id=$u AND item_id=$i;",
                ("$u", Db.L(userId)), ("$i", itemId), ("$d", delta));

        Db.Run(c, "DELETE FROM inventory WHERE user_id=$u AND item_id=$i AND qty <= 0;",
            ("$u", Db.L(userId)), ("$i", itemId));
        return GetQty(c, userId, itemId);
    }

    public static int AddParcelDelta(SqliteConnection c, ulong userId, string parcelId, int delta)
    {
        if (delta >= 0)
            Db.Run(c,
                "INSERT INTO parcels(user_id, parcel_id, qty) VALUES($u,$p,$d) " +
                "ON CONFLICT(user_id, parcel_id) DO UPDATE SET qty = qty + $d;",
                ("$u", Db.L(userId)), ("$p", parcelId), ("$d", delta));
        else
            Db.Run(c, "UPDATE parcels SET qty = qty + $d WHERE user_id=$u AND parcel_id=$p;",
                ("$u", Db.L(userId)), ("$p", parcelId), ("$d", delta));

        Db.Run(c, "DELETE FROM parcels WHERE user_id=$u AND parcel_id=$p AND qty <= 0;",
            ("$u", Db.L(userId)), ("$p", parcelId));
        return Db.Get<int>(c, "SELECT COALESCE(qty,0) FROM parcels WHERE user_id=$u AND parcel_id=$p;",
            ("$u", Db.L(userId)), ("$p", parcelId));
    }

    public void ApplyXp(Player p, long amount)
    {
        p.ExplorerXp += amount;
        var guard = 0;
        while (p.Level < 1000 && p.ExplorerXp >= Cfg.XpCurve.XpToNext(p.Level))
        {
            p.ExplorerXp -= Cfg.XpCurve.XpToNext(p.Level);
            p.Level++;
            if (++guard > 1000) break;
        }
    }

    public long TotalXp(Player p) => Cfg.XpCurve.TotalXpForLevel(p.Level) + p.ExplorerXp;

    // ---- granting ----------------------------------------------------------

    public GrantResult GrantInTransaction(SqliteConnection c, ulong userId, ItemDef item, bool countStats = true)
    {
        var p = EnsurePlayer(c, userId);
        var qty = GetQty(c, userId, item.Id);
        var isNew = qty == 0;
        AddInventory(c, userId, item.Id, 1);

        var levelsBefore = p.Level;
        var xp = 0;
        var tokens = 0;

        if (countStats)
        {
            p.TotalFinds++;
            if (isNew)
            {
                p.UniqueFinds++;
                p.CollectionScore += item.Value(Cfg);
                if (item.Secret)
                    p.CollectionScore += Cfg.SecretScoreBonus;
            }
            xp = Cfg.XpFor(item.Rarity);
            tokens = Cfg.TokensFor(item.Rarity);
            if (xp > 0)
                ApplyXp(p, xp);
            p.TokensEarned += tokens;
        }

        Db.Run(c, "INSERT INTO discoveries(user_id, item_id, rarity, created_at) VALUES($u,$i,$r,$t);",
            ("$u", Db.L(userId)), ("$i", item.Id), ("$r", RarityUtil.Label(item.Rarity)), ("$t", Db.Now()));

        SavePlayer(c, p);

        return new GrantResult
        {
            Item = item,
            IsNew = isNew,
            Qty = qty + 1,
            Xp = xp,
            Tokens = tokens,
            LevelsGained = p.Level - levelsBefore,
            CollectionValue = isNew ? item.Value(Cfg) : 0
        };
    }

    /// <summary>Grants an item, its XP and Tokens, and mirrors Tokens into Nadeko's balance.</summary>
    public GrantResult Grant(ulong userId, ItemDef item, bool countStats = true)
    {
        GrantResult result = null;
        Db.Immediate(c => { result = GrantInTransaction(c, userId, item, countStats); return true; });
        if (result.Tokens > 0)
            Economy.Award(userId, result.Tokens);
        return result;
    }

    public int MoreXp(ulong userId, int amount)
    {
        var gained = 0;
        Db.Immediate(c =>
        {
            var p = EnsurePlayer(c, userId);
            var before = p.Level;
            ApplyXp(p, amount);
            gained = p.Level - before;
            SavePlayer(c, p);
            return true;
        });
        return gained;
    }

    public void AddTokens(ulong userId, int amount)
    {
        if (amount == 0) return;
        Db.Immediate(c =>
        {
            var p = EnsurePlayer(c, userId);
            p.TokensEarned += amount;
            SavePlayer(c, p);
            return true;
        });
        Economy.Award(userId, amount);
    }

    public void AddScraps(ulong userId, int amount)
    {
        if (amount == 0) return;
        Db.Immediate(c =>
        {
            var p = EnsurePlayer(c, userId);
            p.Scraps += amount;
            SavePlayer(c, p);
            return true;
        });
    }

    public void AddTitle(ulong userId, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        Db.Execute("INSERT OR IGNORE INTO titles(user_id, title, earned_at) VALUES($u,$t,$n);",
            ("$u", Db.L(userId)), ("$t", title), ("$n", Db.Now()));
    }

    public List<string> Titles(ulong userId)
        => Db.Immediate(c => Db.Query(c, "SELECT title FROM titles WHERE user_id=$u ORDER BY earned_at;",
            r => r.GetString(0), ("$u", Db.L(userId))));

    public bool IsAreaUnlocked(ulong userId, string areaId)
        => Db.Scalar<int>("SELECT COUNT(*) FROM area_unlocks WHERE user_id=$u AND area_id=$a;",
            ("$u", Db.L(userId)), ("$a", areaId)) > 0;

    public void UnlockArea(ulong userId, string areaId)
    {
        if (string.IsNullOrWhiteSpace(areaId)) return;
        Db.Execute("INSERT OR IGNORE INTO area_unlocks(user_id, area_id, unlocked_at) VALUES($u,$a,$n);",
            ("$u", Db.L(userId)), ("$a", areaId), ("$n", Db.Now()));
    }

    public int CompletedSetCount(ulong userId)
        => Db.Scalar<int>("SELECT COUNT(*) FROM completed_sets WHERE user_id=$u;", ("$u", Db.L(userId)));

    public List<string> CompletedSets(ulong userId)
        => Db.Immediate(c => Db.Query(c, "SELECT set_id FROM completed_sets WHERE user_id=$u ORDER BY completed_at;",
            r => r.GetString(0), ("$u", Db.L(userId))));

    // ---- daily expeditions -------------------------------------------------

    public (bool allowed, int remaining) UseDailyExpedition(ulong userId)
    {
        var allowed = false;
        var remaining = 0;
        var limit = Math.Max(1, Cfg.DailyExpeditions);
        var key = DateKey();
        Db.Immediate(c =>
        {
            var p = EnsurePlayer(c, userId);
            if (!string.Equals(p.DailyDate, key, StringComparison.Ordinal))
            {
                p.DailyDate = key;
                p.DailyUsed = 0;
            }
            if (p.DailyUsed < limit)
            {
                p.DailyUsed++;
                allowed = true;
            }
            remaining = Math.Max(0, limit - p.DailyUsed);
            SavePlayer(c, p);
            return true;
        });
        return (allowed, remaining);
    }

    public int RemainingExpeditions(ulong userId)
    {
        var p = Get(userId);
        var limit = Math.Max(1, Cfg.DailyExpeditions);
        if (!string.Equals(p.DailyDate, DateKey(), StringComparison.Ordinal))
            return limit;
        return Math.Max(0, limit - p.DailyUsed);
    }

    // ---- admin -------------------------------------------------------------

    public void SetLevel(ulong userId, int level)
    {
        level = Math.Clamp(level, 1, 999);
        Db.Immediate(c =>
        {
            var p = EnsurePlayer(c, userId);
            p.Level = level;
            p.ExplorerXp = 0;
            SavePlayer(c, p);
            return true;
        });
    }

    public void ResetDaily(ulong userId)
    {
        Db.Immediate(c =>
        {
            var p = EnsurePlayer(c, userId);
            p.DailyUsed = 0;
            p.DailyDate = "";
            SavePlayer(c, p);
            return true;
        });
    }

    public List<(string ItemId, int Qty, long FirstFoundAt)> Inventory(ulong userId)
        => Db.Immediate(c => Db.Query(c,
            "SELECT item_id, qty, first_found_at FROM inventory WHERE user_id=$u AND qty > 0 ORDER BY first_found_at;",
            r => (r.GetString(0), r.GetInt32(1), r.GetInt64(2)),
            ("$u", Db.L(userId))));

    public List<(ulong UserId, string ItemId, long At)> RecentDiscoveries(ulong userId, int limit)
        => Db.Immediate(c => Db.Query(c,
            "SELECT user_id, item_id, created_at FROM discoveries WHERE user_id=$u ORDER BY id DESC LIMIT $l;",
            r => (Db.U(r.GetInt64(0)), r.GetString(1), r.GetInt64(2)),
            ("$u", Db.L(userId)), ("$l", limit)));
}
