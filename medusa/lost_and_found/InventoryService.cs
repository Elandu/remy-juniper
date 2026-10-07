using Microsoft.Data.Sqlite;

namespace LostAndFound;

public enum TransferResult { Ok, NoItem, NotTradable, Self }
public enum RecycleResult { Ok, NoItem, NotRecyclable, Protected }
public enum EquipResult { Ok, NotCharm, NotOwned }

/// <summary>Inventory reads, recycling, charm equipping, parcels and item transfers.</summary>
public sealed class InventoryService
{
    private readonly LostFoundService _core;
    private readonly Catalog _cat;

    public InventoryService(LostFoundService core)
    {
        _core = core;
        _cat = core.Catalog;
    }

    public List<InventoryEntry> List(ulong userId)
        => _core.Db.Immediate(c => Db.Query(c,
            "SELECT item_id, qty, first_found_at FROM inventory WHERE user_id=$u AND qty > 0 ORDER BY first_found_at;",
            r => new InventoryEntry { ItemId = r.GetString(0), Qty = r.GetInt32(1), FirstFoundAt = r.GetInt64(2) },
            ("$u", Db.L(userId))));

    public int Qty(ulong userId, string itemId) => _core.Db.Scalar<int>(
        "SELECT COALESCE(qty,0) FROM inventory WHERE user_id=$u AND item_id=$i;",
        ("$u", Db.L(userId)), ("$i", itemId));

    // ---- recycle -----------------------------------------------------------

    public (RecycleResult result, int scraps) Recycle(ulong userId, string itemId, bool confirm)
    {
        var result = RecycleResult.NoItem;
        var scraps = 0;
        _core.Db.Immediate(c =>
        {
            var item = _cat.Item(itemId);
            var qty = LostFoundService.GetQty(c, userId, itemId);
            if (item is null || qty <= 0) { result = RecycleResult.NoItem; return true; }
            if (!item.Recyclable) { result = RecycleResult.NotRecyclable; return true; }
            if (item.Rarity == Rarity.SarahRelic && !confirm) { result = RecycleResult.Protected; return true; }
            if (qty == 1 && !confirm) { result = RecycleResult.Protected; return true; }

            LostFoundService.AddInventory(c, userId, itemId, -1);
            scraps = _core.Cfg.ScrapFor(item.Rarity);
            var p = LostFoundService.EnsurePlayer(c, userId);
            p.Scraps += scraps;
            LostFoundService.SavePlayer(c, p);

            if (LostFoundService.GetQty(c, userId, itemId) <= 0)
                Db.Run(c, "DELETE FROM equipped WHERE user_id=$u AND item_id=$i;", ("$u", Db.L(userId)), ("$i", itemId));

            result = RecycleResult.Ok;
            return true;
        });
        return (result, scraps);
    }

    // ---- charm -------------------------------------------------------------

    public EquipResult Equip(ulong userId, string itemId)
    {
        if (!_core.Cfg.Charms.ContainsKey(itemId))
            return EquipResult.NotCharm;
        if (Qty(userId, itemId) <= 0)
            return EquipResult.NotOwned;

        _core.Db.Execute(
            "INSERT INTO equipped(user_id, item_id, equipped_at) VALUES($u,$i,$n) " +
            "ON CONFLICT(user_id) DO UPDATE SET item_id=$i, equipped_at=$n;",
            ("$u", Db.L(userId)), ("$i", itemId), ("$n", Db.Now()));
        return EquipResult.Ok;
    }

    public void Unequip(ulong userId)
        => _core.Db.Execute("DELETE FROM equipped WHERE user_id=$u;", ("$u", Db.L(userId)));

    public string Equipped(ulong userId)
    {
        var val = _core.Db.Scalar<string>("SELECT item_id FROM equipped WHERE user_id=$u;", ("$u", Db.L(userId)));
        return val;
    }

    public CharmDef EquippedCharm(ulong userId)
    {
        var id = Equipped(userId);
        return id is not null && _core.Cfg.Charms.TryGetValue(id, out var c) ? c : null;
    }

    // ---- transfer (gift / trade) ------------------------------------------

    public TransferResult Transfer(ulong from, ulong to, string itemId, int qty = 1)
    {
        var result = TransferResult.NoItem;
        if (from == to) return TransferResult.Self;
        if (qty <= 0) return TransferResult.NoItem;
        var item = _cat.Item(itemId);
        if (item is null) return TransferResult.NoItem;
        if (!item.Tradable) return TransferResult.NotTradable;

        _core.Db.Immediate(c =>
        {
            var have = LostFoundService.GetQty(c, from, itemId);
            if (have < qty) { result = TransferResult.NoItem; return true; }

            var toQty = LostFoundService.GetQty(c, to, itemId);
            LostFoundService.AddInventory(c, from, itemId, -qty);
            LostFoundService.AddInventory(c, to, itemId, qty);

            if (toQty == 0)
            {
                var p = LostFoundService.EnsurePlayer(c, to);
                p.UniqueFinds++;
                p.TotalFinds += qty;
                p.CollectionScore += item.Value(_core.Cfg);
                if (item.Secret)
                    p.CollectionScore += _core.Cfg.SecretScoreBonus;
                LostFoundService.SavePlayer(c, p);
            }

            result = TransferResult.Ok;
            return true;
        });
        return result;
    }

    // ---- parcels -----------------------------------------------------------

    public void AddParcel(ulong userId, string parcelId, int qty = 1)
    {
        if (qty == 0) return;
        _core.Db.Execute(
            "INSERT INTO parcels(user_id, parcel_id, qty) VALUES($u,$p,$q) " +
            "ON CONFLICT(user_id, parcel_id) DO UPDATE SET qty = qty + $q;",
            ("$u", Db.L(userId)), ("$p", parcelId), ("$q", qty));
    }

    public int ParcelCount(ulong userId)
        => _core.Db.Scalar<int>("SELECT COALESCE(SUM(qty),0) FROM parcels WHERE user_id=$u;", ("$u", Db.L(userId)));

    public List<(string ParcelId, int Qty)> Parcels(ulong userId)
        => _core.Db.Immediate(c => Db.Query(c,
            "SELECT parcel_id, qty FROM parcels WHERE user_id=$u AND qty > 0 ORDER BY parcel_id;",
            r => (r.GetString(0), r.GetInt32(1)), ("$u", Db.L(userId))));

    /// <summary>Takes one parcel of any kind. Returns the parcel id or null.</summary>
    public string TakeAnyParcel(ulong userId)
    {
        string taken = null;
        _core.Db.Immediate(c =>
        {
            var rows = Db.Query(c,
                "SELECT parcel_id FROM parcels WHERE user_id=$u AND qty > 0 ORDER BY qty;",
                r => r.GetString(0), ("$u", Db.L(userId)));
            if (rows.Count == 0) return true;
            taken = rows[0];
            LostFoundService.AddParcelDelta(c, userId, taken, -1);
            return true;
        });
        return taken;
    }
}
