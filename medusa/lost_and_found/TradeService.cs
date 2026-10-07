namespace LostAndFound;

public sealed class TradeOffer
{
    public long Id { get; set; }
    public ulong FromUser { get; set; }
    public ulong ToUser { get; set; }
    public string ItemId { get; set; }
    public long CreatedAt { get; set; }
}

/// <summary>Gifting and simple offer/accept trading.</summary>
public sealed class TradeService
{
    private readonly LostFoundService _core;
    private readonly InventoryService _inventory;
    private readonly Catalog _cat;

    public TradeService(LostFoundService core, InventoryService inventory)
    {
        _core = core;
        _inventory = inventory;
        _cat = core.Catalog;
    }

    public TransferResult Gift(ulong from, ulong to, string itemId, int qty = 1)
        => _inventory.Transfer(from, to, itemId, qty);

    public (bool ok, string message, long offerId) Offer(ulong from, ulong to, string itemId)
    {
        if (from == to) return (false, "You can't trade with yourself.", -1);
        var item = _cat.Item(itemId);
        if (item is null) return (false, "No such item.", -1);
        if (!item.Tradable) return (false, $"{item.Name} can't be traded.", -1);
        if (_inventory.Qty(from, itemId) <= 0) return (false, "You don't have that.", -1);

        long id = -1;
        _core.Db.Immediate(c =>
        {
            Db.Run(c,
                "INSERT INTO trade_offers(from_user, to_user, item_id, created_at, status) VALUES($f,$t,$i,$n,'pending');",
                ("$f", Db.L(from)), ("$t", Db.L(to)), ("$i", itemId), ("$n", Db.Now()));
            id = Db.Get<long>(c, "SELECT last_insert_rowid();");
            return true;
        });
        return (true, $"{item.Name}", id);
    }

    public (bool ok, string message) Accept(ulong userId)
    {
        var offer = LatestPendingFor(userId);
        if (offer is null) return (false, "No trade offers waiting.");

        var item = _cat.Item(offer.ItemId);
        if (item is null) return (false, "That offer is no longer valid.");

        var transfer = _inventory.Transfer(offer.FromUser, userId, offer.ItemId, 1);
        if (transfer == TransferResult.NoItem)
        {
            Mark(offer.Id, "failed");
            return (false, "They no longer have that item.");
        }
        if (transfer == TransferResult.NotTradable)
        {
            Mark(offer.Id, "failed");
            return (false, "That item can't be traded.");
        }
        if (transfer != TransferResult.Ok)
            return (false, "Trade failed.");

        Mark(offer.Id, "accepted");
        return (true, $"You received {item.Name}.");
    }

    public bool Decline(ulong userId)
    {
        var offer = LatestPendingFor(userId);
        if (offer is null) return false;
        Mark(offer.Id, "declined");
        return true;
    }

    private TradeOffer LatestPendingFor(ulong userId)
    {
        var rows = _core.Db.Immediate(c => Db.Query(c,
            "SELECT id, from_user, to_user, item_id, created_at FROM trade_offers " +
            "WHERE to_user=$u AND status='pending' ORDER BY id DESC LIMIT 1;",
            r => new TradeOffer
            {
                Id = r.GetInt64(0),
                FromUser = Db.U(r.GetInt64(1)),
                ToUser = Db.U(r.GetInt64(2)),
                ItemId = r.GetString(3),
                CreatedAt = r.GetInt64(4)
            },
            ("$u", Db.L(userId))));
        return rows.Count > 0 ? rows[0] : null;
    }

    private void Mark(long id, string status)
        => _core.Db.Execute("UPDATE trade_offers SET status=$s WHERE id=$id;", ("$s", status), ("$id", id));

    public int PendingCount(ulong userId)
        => _core.Db.Scalar<int>("SELECT COUNT(*) FROM trade_offers WHERE to_user=$u AND status='pending';",
            ("$u", Db.L(userId)));
}
