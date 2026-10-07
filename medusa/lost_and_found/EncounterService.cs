using System.Collections.Concurrent;

namespace LostAndFound;

public sealed class PendingEncounter
{
    public EncounterDef Encounter { get; set; }
    public string AreaId { get; set; }
    public long ExpiresAt { get; set; }
}

public sealed class EncounterOutcome
{
    public bool Ok { get; set; }
    public string Text { get; set; }
    public ItemDef Item { get; set; }
    public GrantResult Grant { get; set; }
    public int Xp { get; set; }
    public int Tokens { get; set; }
    public int Scraps { get; set; }
    public List<SetDef> CompletedSets { get; set; } = new();
}

/// <summary>Encounter offers and their Investigate / Ignore / Send Remy resolution.</summary>
public sealed class EncounterService
{
    private readonly LostFoundService _core;
    private readonly CollectionService _collection;
    private readonly ExplorationService _exploration;
    private readonly InventoryService _inventory;
    private readonly Catalog _cat;
    private readonly IRandomSource _rng;
    private readonly ConcurrentDictionary<ulong, PendingEncounter> _pending = new();

    private const int PendingSeconds = 600;

    public EncounterService(LostFoundService core, CollectionService collection, ExplorationService exploration,
        InventoryService inventory, IRandomSource rng = null)
    {
        _core = core;
        _collection = collection;
        _exploration = exploration;
        _inventory = inventory;
        _cat = core.Catalog;
        _rng = rng ?? new SystemRandomSource();
    }

    public void Offer(ulong userId, EncounterDef encounter, string areaId)
    {
        if (encounter is null) return;
        _pending[userId] = new PendingEncounter
        {
            Encounter = encounter,
            AreaId = areaId,
            ExpiresAt = Db.Now() + PendingSeconds
        };
    }

    public PendingEncounter Get(ulong userId)
    {
        if (!_pending.TryGetValue(userId, out var p)) return null;
        if (p.ExpiresAt < Db.Now())
        {
            _pending.TryRemove(userId, out _);
            return null;
        }
        return p;
    }

    public EncounterOutcome Choose(ulong userId, int index)
    {
        var outcome = new EncounterOutcome();
        var pending = Get(userId);
        if (pending is null)
        {
            outcome.Text = "There's nothing waiting on you.";
            return outcome;
        }

        var options = pending.Encounter.Options;
        if (index < 1 || index > options.Count)
        {
            outcome.Text = $"Pick a number between 1 and {options.Count}.";
            return outcome;
        }

        var option = options[index - 1];
        outcome.Ok = true;
        _pending.TryRemove(userId, out _);

        var pick = PickOutcome(option);
        switch (pick?.Type)
        {
            case "item":
                var rarity = RarityUtil.TryParse(pick.Rarity, out var r) ? r : Rarity.Uncommon;
                outcome.Item = _exploration.RollItemOfRarity(rarity, _rng);
                if (outcome.Item is not null)
                {
                    outcome.Grant = _core.Grant(userId, outcome.Item);
                    outcome.CompletedSets = _collection.CheckCompletions(userId);
                    var extra = _collection.TryRollRelic(_core.Get(userId), _rng);
                    if (extra is not null)
                    {
                        outcome.Grant = _core.Grant(userId, extra);
                        outcome.Item = extra;
                    }
                }
                break;
            case "xp":
                outcome.Xp = pick.Xp;
                _core.MoreXp(userId, pick.Xp);
                break;
            case "tokens":
                outcome.Tokens = pick.Tokens;
                _core.AddTokens(userId, pick.Tokens);
                break;
            case "scraps":
                outcome.Scraps = pick.Scraps;
                _core.AddScraps(userId, pick.Scraps);
                break;
            case "effect":
                StoreEffect(userId, pick);
                break;
        }

        outcome.Text = string.IsNullOrWhiteSpace(pick?.Text) ? option.Result : pick.Text;
        return outcome;
    }

    private OutcomeDef PickOutcome(EncounterOptionDef option)
    {
        if (option?.Outcomes is null || option.Outcomes.Count == 0) return null;
        if (option.Outcomes.Count == 1) return option.Outcomes[0];
        return Loot.PickWeighted(option.Outcomes.Select(o => (o, o.Chance <= 0 ? 0.0001 : o.Chance)).ToList(), _rng);
    }

    private void StoreEffect(ulong userId, OutcomeDef pick)
    {
        if (string.IsNullOrWhiteSpace(pick.Effect)) return;
        var expires = Db.Now() + Math.Max(1, pick.EffectMinutes) * 60L;
        _core.Db.Execute(
            "INSERT INTO temp_effects(user_id, effect_id, value, expires_at) VALUES($u,$e,$v,$x) " +
            "ON CONFLICT(user_id, effect_id) DO UPDATE SET value=$v, expires_at=$x;",
            ("$u", Db.L(userId)), ("$e", pick.Effect), ("$v", (double)pick.EffectValue), ("$x", expires));
    }

    public bool HasPending(ulong userId) => Get(userId) is not null;
}
