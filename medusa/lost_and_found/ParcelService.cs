namespace LostAndFound;

public sealed class ParcelResult
{
    public bool Ok { get; set; }
    public string Message { get; set; }
    public ItemDef Item { get; set; }
    public int Scraps { get; set; }
    public GrantResult Grant { get; set; }
    public List<SetDef> CompletedSets { get; set; } = new();
}

/// <summary>Mystery Parcels: opening and gifting. Never purchasable for real money.</summary>
public sealed class ParcelService
{
    private readonly LostFoundService _core;
    private readonly InventoryService _inventory;
    private readonly CollectionService _collection;
    private readonly ExplorationService _exploration;
    private readonly IRandomSource _rng;

    public ParcelService(LostFoundService core, InventoryService inventory, CollectionService collection,
        ExplorationService exploration, IRandomSource rng = null)
    {
        _core = core;
        _inventory = inventory;
        _collection = collection;
        _exploration = exploration;
        _rng = rng ?? new SystemRandomSource();
    }

    public ParcelResult Open(ulong userId)
    {
        var result = new ParcelResult();
        var parcelId = _inventory.TakeAnyParcel(userId);
        if (parcelId is null)
        {
            result.Message = "You don't have a parcel to open.";
            return result;
        }

        var def = PickParcelDef();
        var rarity = def is not null && RarityUtil.TryParse(def.Rarity, out var r) ? r : Rarity.Common;
        result.Item = _exploration.RollItemOfRarity(rarity, _rng);
        if (result.Item is not null)
        {
            result.Grant = _core.Grant(userId, result.Item);
            result.CompletedSets = _collection.CheckCompletions(userId);
        }

        if (def is not null && def.Scraps > 0)
        {
            _core.AddScraps(userId, def.Scraps);
            result.Scraps = def.Scraps;
        }

        result.Ok = true;
        return result;
    }

    public bool Gift(ulong from, ulong to)
    {
        if (from == to) return false;
        var parcelId = _inventory.TakeAnyParcel(from);
        if (parcelId is null) return false;
        _inventory.AddParcel(to, parcelId, 1);
        return true;
    }

    public int Count(ulong userId) => _inventory.ParcelCount(userId);

    private ParcelDrop PickParcelDef()
    {
        var table = _core.Cfg.Parcels;
        if (table.Count == 0) return null;
        return Loot.PickWeighted(table.Select(p => (p, p.Weight <= 0 ? 0.0001 : p.Weight)).ToList(), _rng);
    }
}
