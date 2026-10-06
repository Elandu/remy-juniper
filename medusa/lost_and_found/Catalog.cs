namespace LostAndFound;

/// <summary>Indexed, read-only view over the content in <see cref="LostFoundConfig"/>.</summary>
public sealed class Catalog
{
    public LostFoundConfig Cfg { get; }
    public Dictionary<string, ItemDef> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SetDef> Sets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AreaDef> Areas { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, RecipeDef> Recipes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, EncounterDef> Encounters { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<ItemDef>> ItemsBySet { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<ItemDef>> ItemsByArea { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ItemDef> Discoverable { get; } = new();

    public Catalog(LostFoundConfig cfg)
    {
        Cfg = cfg;

        foreach (var i in cfg.Items)
            Items[i.Id] = i;
        foreach (var s in cfg.Sets)
            Sets[s.Id] = s;
        foreach (var a in cfg.Areas)
            Areas[a.Id] = a;
        foreach (var r in cfg.Recipes)
            Recipes[r.Id] = r;
        foreach (var e in cfg.Encounters)
            Encounters[e.Id] = e;

        foreach (var i in cfg.Items)
        {
            if (!ItemsBySet.TryGetValue(i.Set, out var list))
                ItemsBySet[i.Set] = list = new List<ItemDef>();
            list.Add(i);
        }

        foreach (var a in cfg.Areas)
        {
            var pool = new List<ItemDef>();
            foreach (var setId in a.Sets)
            {
                if (!ItemsBySet.TryGetValue(setId, out var setItems)) continue;
                foreach (var it in setItems)
                {
                    if (it.Secret || it.Set == "badges" || it.Set == "relics") continue;
                    if (it.Areas.Length > 0 && !it.Areas.Contains(a.Id, StringComparer.OrdinalIgnoreCase))
                        continue;
                    pool.Add(it);
                }
            }
            ItemsByArea[a.Id] = pool;
        }

        foreach (var i in cfg.Items)
        {
            if (i.Secret || i.Set == "badges" || i.Set == "relics") continue;
            Discoverable.Add(i);
        }
    }

    public ItemDef Item(string id)
        => id is not null && Items.TryGetValue(id, out var i) ? i : null;

    public SetDef Set(string id)
        => id is not null && Sets.TryGetValue(id, out var s) ? s : null;

    public AreaDef Area(string id)
        => id is not null && Areas.TryGetValue(id, out var a) ? a : null;

    public List<ItemDef> Relics()
        => DiscoverableRelics();

    private List<ItemDef> DiscoverableRelics()
    {
        var list = new List<ItemDef>();
        foreach (var i in Cfg.Items)
            if (i.Set == "relics" || i.ReleaseRelic)
                list.Add(i);
        return list;
    }

    /// <summary>Resolves an item by id, then exact name, then unique partial name.</summary>
    public ItemDef FindFuzzy(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;
        var q = query.Trim();
        if (Items.TryGetValue(q, out var byId))
            return byId;

        foreach (var i in Cfg.Items)
            if (string.Equals(i.Name, q, StringComparison.OrdinalIgnoreCase))
                return i;

        var matches = Cfg.Items
            .Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || i.Id.Contains(q.Replace(" ", "_"), StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public List<ItemDef> FindAllFuzzy(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<ItemDef>();
        var q = query.Trim();
        return Cfg.Items
            .Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || i.Id.Contains(q.Replace(" ", "_"), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public List<ItemDef> WithRarity(IEnumerable<ItemDef> pool, Rarity r)
        => pool.Where(i => i.Rarity == r).ToList();
}
