using Microsoft.Data.Sqlite;

namespace LostAndFound;

public sealed class SetProgress
{
    public SetDef Set { get; set; }
    public int Discovered { get; set; }
    public int Total { get; set; }
    public bool Completed { get; set; }
    public bool Hidden { get; set; }
    public double Percent => Total == 0 ? 0 : (double)Discovered / Total;
}

/// <summary>Set completion, collection score and Sarah Relic eligibility.</summary>
public sealed class CollectionService
{
    private readonly LostFoundService _core;
    private readonly Catalog _cat;

    public CollectionService(LostFoundService core)
    {
        _core = core;
        _cat = core.Catalog;
    }

    public HashSet<string> Owned(SqliteConnection c, ulong userId)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Db.Query(c,
                     "SELECT item_id FROM inventory WHERE user_id=$u AND qty > 0;",
                     r => r.GetString(0), ("$u", Db.L(userId))))
            owned.Add(id);
        return owned;
    }

    public List<SetProgress> Progress(ulong userId)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inv in _core.Inventory(userId))
            owned.Add(inv.ItemId);

        var completed = new HashSet<string>(_core.CompletedSets(userId), StringComparer.OrdinalIgnoreCase);

        var list = new List<SetProgress>();
        foreach (var set in _cat.Cfg.Sets)
        {
            var items = _cat.ItemsBySet.TryGetValue(set.Id, out var l) ? l : new List<ItemDef>();
            var discovered = items.Count(i => owned.Contains(i.Id));
            list.Add(new SetProgress
            {
                Set = set,
                Discovered = discovered,
                Total = items.Count,
                Completed = completed.Contains(set.Id),
                Hidden = set.Hidden && discovered == 0
            });
        }
        return list;
    }

    /// <summary>Awards any newly-completed sets exactly once. Returns the sets completed by this call.</summary>
    public List<SetDef> CheckCompletions(ulong userId)
    {
        var newly = new List<SetDef>();
        var tokensToAward = 0;

        _core.Db.Immediate(c =>
        {
            var p = LostFoundService.EnsurePlayer(c, userId);
            var owned = Owned(c, userId);
            var completedNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in Db.Query(c, "SELECT set_id FROM completed_sets WHERE user_id=$u;",
                         r => r.GetString(0), ("$u", Db.L(userId))))
                completedNow.Add(id);

            foreach (var set in _cat.Cfg.Sets)
            {
                if (completedNow.Contains(set.Id)) continue;

                var items = _cat.ItemsBySet.TryGetValue(set.Id, out var l) ? l : null;
                if (items is null || items.Count == 0) continue;
                if (items.Any(i => !owned.Contains(i.Id))) continue;

                var inserted = Db.Run(c,
                    "INSERT OR IGNORE INTO completed_sets(user_id, set_id, completed_at) VALUES($u,$s,$n);",
                    ("$u", Db.L(userId)), ("$s", set.Id), ("$n", Db.Now()));
                if (inserted <= 0) continue;

                p.CollectionScore += _core.Cfg.SetCompletionScoreBonus;
                var rw = set.Reward ?? new SetReward();
                if (rw.Xp > 0)
                    _core.ApplyXp(p, rw.Xp);
                if (rw.Tokens > 0)
                {
                    p.TokensEarned += rw.Tokens;
                    tokensToAward += rw.Tokens;
                }

                if (!string.IsNullOrWhiteSpace(rw.Title))
                    Db.Run(c, "INSERT OR IGNORE INTO titles(user_id, title, earned_at) VALUES($u,$t,$n);",
                        ("$u", Db.L(userId)), ("$t", rw.Title), ("$n", Db.Now()));

                if (!string.IsNullOrWhiteSpace(rw.Badge))
                    GrantSilent(c, p, rw.Badge);

                if (!string.IsNullOrWhiteSpace(rw.SpecialItem))
                    GrantSilent(c, p, rw.SpecialItem);

                if (!string.IsNullOrWhiteSpace(rw.UnlockArea))
                    Db.Run(c, "INSERT OR IGNORE INTO area_unlocks(user_id, area_id, unlocked_at) VALUES($u,$a,$n);",
                        ("$u", Db.L(userId)), ("$a", rw.UnlockArea), ("$n", Db.Now()));

                completedNow.Add(set.Id);
                newly.Add(set);
            }

            LostFoundService.SavePlayer(c, p);
            return true;
        });

        if (tokensToAward > 0)
            _core.Economy.Award(userId, tokensToAward);

        return newly;
    }

    /// <summary>Inventory-only grant used by set rewards; player totals are updated in-memory.</summary>
    private void GrantSilent(SqliteConnection c, Player p, string itemId)
    {
        var item = _cat.Item(itemId);
        if (item is null) return;
        var isNew = LostFoundService.GetQty(c, p.UserId, itemId) == 0;
        LostFoundService.AddInventory(c, p.UserId, itemId, 1);
        if (isNew)
        {
            p.UniqueFinds++;
            p.CollectionScore += item.Value(_core.Cfg);
            if (item.Secret)
                p.CollectionScore += _core.Cfg.SecretScoreBonus;
        }
        Db.Run(c, "INSERT INTO discoveries(user_id, item_id, rarity, created_at) VALUES($u,$i,$r,$n);",
            ("$u", Db.L(p.UserId)), ("$i", item.Id), ("$r", RarityUtil.Label(item.Rarity)), ("$n", Db.Now()));
    }

    /// <summary>Rolls for a Sarah Relic if the player is eligible. Returns null otherwise.</summary>
    public ItemDef TryRollRelic(Player p, IRandomSource rng)
    {
        var cfg = _core.Cfg;
        if (p.Level < cfg.Relic.MinLevel) return null;
        if (_core.CompletedSetCount(p.UserId) < cfg.Relic.MinCompletedSets) return null;
        if (rng.NextDouble() * 100.0 >= cfg.Relic.ChancePercent) return null;

        var relics = _cat.Relics();
        if (relics.Count == 0) return null;

        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inv in _core.Inventory(p.UserId))
            owned.Add(inv.ItemId);

        var unowned = relics.Where(r => !owned.Contains(r.Id)).ToList();
        return Loot.Pick(unowned.Count > 0 ? unowned : relics, rng);
    }

    public List<(ulong UserId, int Score, int UniqueFinds, int Level)> Top(int limit)
        => _core.Db.Immediate(c => Db.Query(c,
            "SELECT user_id, collection_score, unique_finds, level FROM players " +
            "ORDER BY collection_score DESC, unique_finds DESC LIMIT $l;",
            r => (Db.U(r.GetInt64(0)), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)),
            ("$l", limit)));
}
