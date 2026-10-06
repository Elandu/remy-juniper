using Microsoft.Data.Sqlite;

namespace LostAndFound;

public sealed class QuestRow
{
    public string QuestId { get; set; }
    public int Progress { get; set; }
    public int Target { get; set; }
    public bool Completed { get; set; }
    public QuestDef Def { get; set; }
}

/// <summary>Weekly side quests. Progress comes from finds, Nadeko games and Lost &amp; Found actions.</summary>
public sealed class QuestService
{
    private readonly LostFoundService _core;
    private readonly Catalog _cat;
    private readonly Dictionary<string, QuestDef> _defs = new(StringComparer.OrdinalIgnoreCase);

    public QuestService(LostFoundService core)
    {
        _core = core;
        _cat = core.Catalog;
        foreach (var q in core.Cfg.Quests)
            _defs[q.Id] = q;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 17;
            foreach (var ch in s) h = h * 31 + ch;
            return h;
        }
    }

    private List<QuestDef> WeekSelection(string week)
    {
        var all = _core.Cfg.Quests;
        if (all.Count == 0) return new List<QuestDef>();
        var n = Math.Clamp(_core.Cfg.Questing.PerWeek, 1, all.Count);
        return all.OrderBy(q => StableHash(week + "|" + q.Id)).Take(n).ToList();
    }

    private void EnsureWeekInTx(SqliteConnection c, ulong userId, string week)
    {
        Db.Run(c, "DELETE FROM quests WHERE user_id=$u AND week_key <> $w;",
            ("$u", Db.L(userId)), ("$w", week));

        foreach (var q in WeekSelection(week))
            Db.Run(c,
                "INSERT OR IGNORE INTO quests(user_id, quest_id, week_key, progress, target, completed, claimed, updated_at) " +
                "VALUES($u,$q,$w,0,$t,0,0,$n);",
                ("$u", Db.L(userId)), ("$q", q.Id), ("$w", week), ("$t", Math.Max(1, q.Amount)), ("$n", Db.Now()));
    }

    public void EnsureWeek(ulong userId)
    {
        var week = _core.WeekKey();
        _core.Db.Immediate(c => { EnsureWeekInTx(c, userId, week); return true; });
    }

    public List<QuestRow> List(ulong userId)
    {
        EnsureWeek(userId);
        var week = _core.WeekKey();
        var rows = _core.Db.Immediate(c => Db.Query(c,
            "SELECT quest_id, progress, target, completed FROM quests WHERE user_id=$u AND week_key=$w ORDER BY rowid;",
            r => new QuestRow
            {
                QuestId = r.GetString(0),
                Progress = r.GetInt32(1),
                Target = r.GetInt32(2),
                Completed = r.GetInt32(3) == 1
            },
            ("$u", Db.L(userId)), ("$w", week)));

        foreach (var row in rows)
            if (_defs.TryGetValue(row.QuestId, out var def))
                row.Def = def;
        return rows;
    }

    public void OnFind(ulong userId, ItemDef item)
    {
        if (item is null) return;
        Advance(userId, "find", _ => true);
        Advance(userId, "find_rarity", def =>
            RarityUtil.TryParse(def.Target, out var need) && item.Rarity >= need);
    }

    public void OnFish(ulong userId) => Advance(userId, "fish", _ => true);
    public void OnTrivia(ulong userId) => Advance(userId, "trivia", _ => true);
    public void OnClaimDrop(ulong userId) => Advance(userId, "claim_drop", _ => true);
    public void OnExpedition(ulong userId) => Advance(userId, "expedition", _ => true);
    public void OnGift(ulong userId) => Advance(userId, "gift", _ => true);
    public void OnCraft(ulong userId) => Advance(userId, "craft", _ => true);

    private void Advance(ulong userId, string type, Func<QuestDef, bool> extra)
    {
        var week = _core.WeekKey();
        var tokensAwarded = 0;
        _core.Db.Immediate(c =>
        {
            EnsureWeekInTx(c, userId, week);
            var rows = Db.Query(c,
                "SELECT quest_id, progress, target, completed FROM quests WHERE user_id=$u AND week_key=$w;",
                r => (Id: r.GetString(0), Progress: r.GetInt32(1), Target: r.GetInt32(2), Completed: r.GetInt32(3) == 1),
                ("$u", Db.L(userId)), ("$w", week));

            foreach (var row in rows)
            {
                if (row.Completed) continue;
                if (!_defs.TryGetValue(row.Id, out var def)) continue;
                if (!string.Equals(def.Type, type, StringComparison.OrdinalIgnoreCase)) continue;
                if (!extra(def)) continue;

                var progress = row.Progress + 1;
                var complete = progress >= row.Target;
                Db.Run(c,
                    "UPDATE quests SET progress=$p, completed=$c, claimed=$c, updated_at=$n " +
                    "WHERE user_id=$u AND quest_id=$q AND week_key=$w;",
                    ("$p", progress), ("$c", complete ? 1 : 0), ("$n", Db.Now()),
                    ("$u", Db.L(userId)), ("$q", row.Id), ("$w", week));

                if (!complete) continue;

                var rw = def.Reward ?? new QuestReward();
                var p = LostFoundService.EnsurePlayer(c, userId);
                if (rw.Xp > 0) _core.ApplyXp(p, rw.Xp);
                if (rw.Tokens > 0) { p.TokensEarned += rw.Tokens; tokensAwarded += rw.Tokens; }
                if (rw.Scraps > 0) p.Scraps += rw.Scraps;
                LostFoundService.SavePlayer(c, p);
                if (rw.Parcels > 0)
                    LostFoundService.AddParcelDelta(c, userId, "parcel", rw.Parcels);
            }
            return true;
        });

        if (tokensAwarded > 0)
            _core.Economy.Award(userId, tokensAwarded);
    }
}
