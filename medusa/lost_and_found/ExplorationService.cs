namespace LostAndFound;

public sealed class ExploreResult
{
    public string Failure { get; set; }
    public AreaDef Area { get; set; }
    public ItemDef Item { get; set; }
    public GrantResult Grant { get; set; }
    public bool Relic { get; set; }
    public EncounterDef Encounter { get; set; }
    public List<SetDef> CompletedSets { get; set; } = new();
    public int Remaining { get; set; }
    public bool Ok => Failure is null;
}

/// <summary>Area access, item selection and the .explore expedition flow.</summary>
public sealed class ExplorationService
{
    private readonly LostFoundService _core;
    private readonly CollectionService _collection;
    private readonly InventoryService _inventory;
    private readonly Catalog _cat;
    private readonly IRandomSource _rng;

    public ExplorationService(LostFoundService core, CollectionService collection, InventoryService inventory,
        IRandomSource rng = null)
    {
        _core = core;
        _collection = collection;
        _inventory = inventory;
        _cat = core.Catalog;
        _rng = rng ?? new SystemRandomSource();
    }

    public IRandomSource Rng => _rng;

    public bool AreaAccessible(Player p, AreaDef area)
    {
        if (area is null) return false;
        if (area.Secret)
        {
            if (_core.IsAreaUnlocked(p.UserId, area.Id)) return true;
            return _core.CompletedSetCount(p.UserId) >= _core.Cfg.SecretAreaCompletedSets;
        }
        if (p.Level >= area.UnlockLevel) return true;
        return _core.IsAreaUnlocked(p.UserId, area.Id);
    }

    public List<AreaDef> UnlockedAreas(Player p)
        => _cat.Cfg.Areas.Where(a => AreaAccessible(p, a)).ToList();

    /// <summary>Up to three areas to suggest when no area was named.</summary>
    public List<AreaDef> OfferAreas(Player p)
    {
        var unlocked = UnlockedAreas(p);
        return unlocked
            .OrderByDescending(a => a.UnlockLevel)
            .Take(3)
            .OrderBy(a => a.UnlockLevel)
            .ToList();
    }

    private double CharmBonusFor(CharmDef charm, ItemDef item, AreaDef area)
    {
        var bonus = 1.0;
        if (area is not null && area.BonusSets.Contains(item.Set, StringComparer.OrdinalIgnoreCase))
            bonus *= area.BonusSetWeight <= 0 ? 1.0 : area.BonusSetWeight;
        if (charm is not null && charm.Effect == "music"
            && (item.Set == "studio_junk" || item.Set == "night_shift"))
            bonus *= 1.0 + charm.BonusChance;
        return bonus;
    }

    /// <summary>Two-step selection: rarity by area weights, then a weighted item of that rarity.</summary>
    public ItemDef RollItem(AreaDef area, CharmDef charm, IRandomSource rng)
    {
        var pool = _cat.ItemsByArea.TryGetValue(area.Id, out var l) ? l : new List<ItemDef>();
        if (pool.Count == 0)
            return RollGlobalItem(rng);

        var weights = _core.Cfg.WeightsFor(area);
        weights.Remove(Rarity.SarahRelic);

        var picks = new List<(ItemDef item, double weight)>();
        foreach (var item in pool)
        {
            var w = item.Weight <= 0 ? 1.0 : item.Weight;
            w *= CharmBonusFor(charm, item, area);
            picks.Add((item, w));
        }

        return PickByRarity(picks, weights, rng);
    }

    public ItemDef RollGlobalItem(IRandomSource rng)
    {
        var picks = _cat.Discoverable
            .Select(i => (item: i, weight: i.Weight <= 0 ? 1.0 : i.Weight))
            .ToList();
        if (picks.Count == 0) return null;
        return PickByRarity(picks, _core.Cfg.WeightsFor(null), rng);
    }

    public ItemDef RollItemOfRarity(Rarity rarity, IRandomSource rng)
    {
        var pool = _cat.Discoverable.Where(i => i.Rarity == rarity).ToList();
        if (pool.Count == 0)
            pool = _cat.Discoverable.ToList();
        if (pool.Count == 0) return null;
        return Loot.PickWeighted(pool.Select(i => (i, i.Weight <= 0 ? 1.0 : i.Weight)).ToList(), rng);
    }

    private static ItemDef PickByRarity(List<(ItemDef item, double weight)> pool,
        IReadOnlyDictionary<Rarity, double> weights, IRandomSource rng)
    {
        var rarity = Loot.RollRarity(weights, rng);
        if (rarity is not null)
        {
            var subset = pool.Where(p => p.item.Rarity == rarity.Value).ToList();
            if (subset.Count > 0)
                return Loot.PickWeighted(subset, rng);
        }
        return Loot.PickWeighted(pool, rng);
    }

    public ExploreResult Explore(ulong userId, string areaId)
    {
        var result = new ExploreResult();
        var area = _cat.Area(areaId);
        if (area is null)
        {
            result.Failure = "unknown";
            return result;
        }

        var player = _core.Get(userId);
        if (!_core.Cfg.Enabled)
        {
            result.Failure = "disabled";
            return result;
        }
        if (!AreaAccessible(player, area))
        {
            result.Failure = "locked";
            result.Area = area;
            return result;
        }

        var (allowed, remaining) = _core.UseDailyExpedition(userId);
        if (!allowed)
        {
            result.Failure = "daily";
            result.Area = area;
            return result;
        }

        result.Area = area;
        result.Remaining = remaining;

        var charm = _inventory.EquippedCharm(userId);
        var triggerEncounter = area.Encounters is { Length: > 0 }
                               && _rng.NextDouble() * 100.0 < _core.Cfg.EncounterChancePercent;

        if (triggerEncounter)
        {
            var encId = Loot.Pick(area.Encounters, _rng);
            result.Encounter = _cat.Encounters.TryGetValue(encId, out var e) ? e : null;
        }
        else
        {
            var relic = _collection.TryRollRelic(player, _rng);
            if (relic is not null)
            {
                result.Item = relic;
                result.Relic = true;
            }
            else
            {
                result.Item = RollItem(area, charm, _rng);
            }

            if (result.Item is not null)
            {
                result.Grant = _core.Grant(userId, result.Item);
                result.CompletedSets = _collection.CheckCompletions(userId);
            }
        }

        return result;
    }
}
