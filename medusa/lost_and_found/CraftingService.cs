using Microsoft.Data.Sqlite;

namespace LostAndFound;

public sealed class CraftResult
{
    public bool Ok { get; set; }
    public string Message { get; set; }
    public RecipeDef Recipe { get; set; }
    public ItemDef Output { get; set; }
    public GrantResult Grant { get; set; }
    public List<SetDef> CompletedSets { get; set; } = new();
    public List<string> Missing { get; set; } = new();
}

/// <summary>Data-driven crafting: consume inputs, grant the output, all in one transaction.</summary>
public sealed class CraftingService
{
    private readonly LostFoundService _core;
    private readonly CollectionService _collection;
    private readonly Catalog _cat;

    public CraftingService(LostFoundService core, CollectionService collection)
    {
        _core = core;
        _collection = collection;
        _cat = core.Catalog;
    }

    public List<RecipeDef> Recipes => _core.Cfg.Recipes;

    public RecipeDef Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var q = key.Trim();
        if (_cat.Recipes.TryGetValue(q, out var byId)) return byId;
        foreach (var r in _core.Cfg.Recipes)
            if (string.Equals(r.Name, q, StringComparison.OrdinalIgnoreCase))
                return r;
        var matches = _core.Cfg.Recipes
            .Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || r.Id.Contains(q.Replace(" ", "_"), StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public CraftResult Craft(ulong userId, string key)
    {
        var result = new CraftResult();
        var recipe = Resolve(key);
        if (recipe is null)
        {
            result.Message = "No such recipe.";
            return result;
        }

        var outputItem = _cat.Item(recipe.Output);
        if (outputItem is null)
        {
            result.Message = "That recipe has no output configured.";
            return result;
        }

        GrantResult lastGrant = null;
        var tokens = 0;

        _core.Db.Immediate(c =>
        {
            var missing = new List<string>();
            foreach (var kv in recipe.Inputs)
            {
                var have = LostFoundService.GetQty(c, userId, kv.Key);
                if (have < kv.Value)
                {
                    var name = _cat.Item(kv.Key)?.Name ?? kv.Key;
                    missing.Add($"{name} ({have}/{kv.Value})");
                }
            }

            if (missing.Count > 0)
            {
                result.Missing = missing;
                return true;
            }

            foreach (var kv in recipe.Inputs)
                LostFoundService.AddInventory(c, userId, kv.Key, -kv.Value);

            for (var i = 0; i < Math.Max(1, recipe.OutputQty); i++)
            {
                lastGrant = _core.GrantInTransaction(c, userId, outputItem);
                tokens += lastGrant.Tokens;
            }
            return true;
        });

        if (result.Missing.Count > 0)
        {
            result.Message = "You're missing: " + string.Join(", ", result.Missing) + ".";
            return result;
        }

        result.Ok = true;
        result.Recipe = recipe;
        result.Output = outputItem;
        result.Grant = lastGrant;
        result.CompletedSets = _collection.CheckCompletions(userId);

        if (tokens > 0)
            _core.Economy.Award(userId, tokens);

        return result;
    }
}
