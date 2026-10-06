using System.Collections.Concurrent;
using Discord;

namespace LostAndFound;

public sealed class GrabResult
{
    public string Status { get; set; } // ok | nothing | taken | parcel
    public ItemDef Item { get; set; }
    public GrantResult Grant { get; set; }
    public bool Parcel { get; set; }
    public List<SetDef> CompletedSets { get; set; } = new();
}

/// <summary>Random server drops that Remy leaves around, and the .grab claim.</summary>
public sealed class DropService
{
    public const string ParcelMarker = "__parcel__";

    private sealed class ChannelState
    {
        public int Messages;
        public long LastDropAt;
    }

    private readonly LostFoundService _core;
    private readonly CollectionService _collection;
    private readonly ExplorationService _exploration;
    private readonly InventoryService _inventory;
    private readonly Catalog _cat;
    private readonly IRandomSource _rng;
    private readonly ConcurrentDictionary<ulong, ChannelState> _states = new();
    private readonly object _gate = new();

    public DropService(LostFoundService core, CollectionService collection, ExplorationService exploration,
        InventoryService inventory, IRandomSource rng = null)
    {
        _core = core;
        _collection = collection;
        _exploration = exploration;
        _inventory = inventory;
        _cat = core.Catalog;
        _rng = rng ?? new SystemRandomSource();
    }

    /// <summary>Called for every guild message. Never consumes the message.</summary>
    public async Task OnMessageAsync(IGuild guild, IUserMessage msg)
    {
        try
        {
            var cfg = _core.Cfg;
            if (!cfg.Enabled || !cfg.Drops.Enabled) return;
            if (guild is null || msg.Author.IsBot) return;
            if (cfg.AllowedGuilds.Length > 0 && !cfg.AllowedGuilds.Contains(guild.Id)) return;

            var content = msg.Content ?? string.Empty;
            if (content.Length < cfg.Drops.MinMessageLength) return;
            if (content[0] == '.' || content[0] == '/' || content[0] == '!') return; // no reward for command spam

            var channelId = msg.Channel.Id;
            if (cfg.Drops.IgnoredChannels.Contains(channelId)) return;
            if (cfg.Drops.Channels.Length > 0 && !cfg.Drops.Channels.Contains(channelId)) return;

            var now = Db.Now();
            var state = _states.GetOrAdd(channelId, _ => new ChannelState());
            bool spawn;
            lock (state)
            {
                if (state.LastDropAt > 0 && now - state.LastDropAt < cfg.Drops.CooldownMinutes * 60L)
                    return;
                state.Messages++;
                if (state.Messages < cfg.Drops.MinMessagesBetween)
                    return;
                if (_rng.NextDouble() >= cfg.Drops.ChancePerMessage)
                    return;

                state.Messages = 0;
                state.LastDropAt = now;
                spawn = true;
            }

            if (!spawn) return;
            if (ActiveDropCount(channelId) >= Math.Max(1, cfg.Drops.MaxActivePerChannel)) return;

            await SpawnAsync(msg.Channel, guild.Id, null).ConfigureAwait(false);
        }
        catch
        {
            // drops must never break message handling
        }
    }

    private int ActiveDropCount(ulong channelId)
        => _core.Db.Scalar<int>(
            "SELECT COUNT(*) FROM drops WHERE channel_id=$c AND active=1 AND expires_at > $n;",
            ("$c", Db.L(channelId)), ("$n", Db.Now()));

    /// <summary>Creates a drop. itemId null = pick randomly (possibly a mystery parcel).</summary>
    public async Task<long> SpawnAsync(IMessageChannel channel, ulong guildId, string itemId)
    {
        var cfg = _core.Cfg;
        var isParcel = false;
        ItemDef item = null;

        if (string.IsNullOrWhiteSpace(itemId))
        {
            if (_rng.NextDouble() < cfg.Drops.ParcelChance)
                isParcel = true;
            else
                item = _exploration.RollGlobalItem(_rng);
        }
        else
        {
            item = _cat.Item(itemId);
        }

        var storedId = isParcel ? ParcelMarker : item?.Id;
        if (storedId is null) return -1;

        var dropId = CreateDrop(guildId, channel.Id, storedId);

        var label = isParcel ? "something wrapped in brown paper" : $"**{item.Name}**";
        await channel.SendMessageAsync($"Remy drops {label} and pretends not to care. `.grab`")
                     .ConfigureAwait(false);
        return dropId;
    }

    /// <summary>Inserts a drop row (no message). Used by admin spawn and tests.</summary>
    public long CreateDrop(ulong guildId, ulong channelId, string storedItemId, int? copiesOverride = null)
    {
        var cfg = _core.Cfg;
        var copies = Math.Max(1, copiesOverride ?? cfg.Drops.Copies);
        var now = Db.Now();
        var expires = now + Math.Max(1, cfg.Drops.ClaimTimeoutMinutes) * 60L;

        return _core.Db.Immediate(c =>
        {
            Db.Run(c,
                "INSERT INTO drops(guild_id, channel_id, item_id, total_copies, copies_remaining, created_at, expires_at, active) " +
                "VALUES($g,$c,$i,$tc,$tc,$n,$e,1);",
                ("$g", Db.L(guildId)), ("$c", Db.L(channelId)), ("$i", storedItemId),
                ("$tc", copies), ("$n", now), ("$e", expires));
            return Db.Get<long>(c, "SELECT last_insert_rowid();");
        });
    }

    public GrabResult Grab(ulong guildId, ulong channelId, ulong userId)
    {
        var result = new GrabResult { Status = "nothing" };

        var dropId = _core.Db.Scalar<long>(
            "SELECT id FROM drops WHERE channel_id=$c AND active=1 AND expires_at > $n ORDER BY id DESC LIMIT 1;",
            ("$c", Db.L(channelId)), ("$n", Db.Now()));
        if (dropId <= 0) return result;

        var claimed = TryClaim(dropId, userId);
        if (claimed is null)
        {
            result.Status = "taken";
            return result;
        }

        if (claimed.ItemId == ParcelMarker)
        {
            _inventory.AddParcel(userId, "parcel", 1);
            result.Status = "parcel";
            result.Parcel = true;
            return result;
        }

        var item = _cat.Item(claimed.ItemId);
        if (item is null)
        {
            result.Status = "nothing";
            return result;
        }

        result.Status = "ok";
        result.Item = item;
        result.Grant = _core.Grant(userId, item);
        result.CompletedSets = _collection.CheckCompletions(userId);

        var relic = _collection.TryRollRelic(_core.Get(userId), _rng);
        if (relic is not null)
        {
            _core.Grant(userId, relic);
            result.Item = relic;
        }

        return result;
    }

    private DropRow TryClaim(long dropId, ulong userId)
    {
        DropRow row = null;
        try
        {
            _core.Db.Immediate(c =>
            {
                var rows = Db.Query(c,
                    "SELECT id, guild_id, channel_id, item_id, total_copies, copies_remaining, created_at, expires_at, active " +
                    "FROM drops WHERE id=$id;",
                    r => new DropRow
                    {
                        Id = r.GetInt64(0),
                        GuildId = Db.U(r.GetInt64(1)),
                        ChannelId = Db.U(r.GetInt64(2)),
                        ItemId = r.GetString(3),
                        TotalCopies = r.GetInt32(4),
                        CopiesRemaining = r.GetInt32(5),
                        CreatedAt = r.GetInt64(6),
                        ExpiresAt = r.GetInt64(7),
                        Active = r.GetInt32(8) == 1
                    },
                    ("$id", dropId));

                if (rows.Count == 0) return true;
                var d = rows[0];
                if (!d.Active || d.ExpiresAt < Db.Now() || d.CopiesRemaining <= 0) return true;

                var inserted = Db.Run(c,
                    "INSERT OR IGNORE INTO drop_claims(drop_id, user_id, claimed_at) VALUES($d,$u,$n);",
                    ("$d", dropId), ("$u", Db.L(userId)), ("$n", Db.Now()));
                if (inserted <= 0) return true; // this user already has one from this drop

                var updated = Db.Run(c,
                    "UPDATE drops SET copies_remaining = copies_remaining - 1, " +
                    "active = CASE WHEN copies_remaining - 1 <= 0 THEN 0 ELSE 1 END " +
                    "WHERE id=$d AND copies_remaining > 0;",
                    ("$d", dropId));
                if (updated <= 0)
                    throw new InvalidOperationException("claim race"); // rolls the claim insert back

                row = d;
                return true;
            });
        }
        catch
        {
            return null;
        }
        return row;
    }
}
