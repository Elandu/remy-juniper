using System.Text;
using Discord;
using NadekoBot.Medusa;

namespace LostAndFound;

/// <summary>
/// REMY'S LOST &amp; FOUND — a collection game for Juniper. Thin command surface:
/// every method delegates to a service. Hooks drive drops and quest progress.
/// </summary>
public sealed class LostAndFound : Snek
{
    public override string Name => "lost_and_found";

    private static readonly Color Forest = new(0x2E, 0x3A, 0x31);
    private static readonly Color Cream = new(0xF4, 0xEB, 0xD8);
    private static readonly Color Sage = new(0xA7, 0xC7, 0xA1);
    private static readonly Color Blush = new(0xE7, 0xA6, 0xB8);
    private static readonly Color Mauve = new(0x7C, 0x6B, 0xA3);

    private string _dir = ".";
    private string _assetsDir = ".";
    private LostFoundConfig _cfg;
    private Catalog _catalog;
    private Db _db;
    private EconomyBridge _economy;
    private LostFoundService _core;
    private CollectionService _collection;
    private InventoryService _inventory;
    private ExplorationService _explore;
    private DropService _drops;
    private EncounterService _encounters;
    private QuestService _quests;
    private CraftingService _crafting;
    private TradeService _trade;
    private ParcelService _parcels;

    public override ValueTask InitializeAsync()
    {
        _dir = Path.GetDirectoryName(typeof(LostAndFound).Assembly.Location) ?? ".";
        _assetsDir = Path.Combine(_dir, "assets");
        BuildServices();
        return default;
    }

    private void BuildServices()
    {
        _cfg = LostFoundConfig.Load(_dir);
        _catalog = new Catalog(_cfg);
        _db = new Db(Db.DefaultPath(_dir));
        _db.Init();
        _economy = new EconomyBridge(EconomyBridge.DefaultPath(_dir));
        _core = new LostFoundService(_db, _catalog, _economy);
        _collection = new CollectionService(_core);
        _inventory = new InventoryService(_core);
        _explore = new ExplorationService(_core, _collection, _inventory);
        _drops = new DropService(_core, _collection, _explore, _inventory);
        _encounters = new EncounterService(_core, _collection, _explore, _inventory);
        _quests = new QuestService(_core);
        _crafting = new CraftingService(_core, _collection);
        _trade = new TradeService(_core, _inventory);
        _parcels = new ParcelService(_core, _inventory, _collection, _explore);
    }

    // ---- hooks -------------------------------------------------------------

    public override async ValueTask<bool> ExecOnMessageAsync(IGuild guild, IUserMessage msg)
    {
        try
        {
            if (_drops is not null && guild is not null)
                await _drops.OnMessageAsync(guild, msg).ConfigureAwait(false);
        }
        catch { }
        return false;
    }

    public override ValueTask ExecPostCommandAsync(AnyContext ctx, string moduleName, string commandName)
    {
        try
        {
            var c = commandName?.TrimStart('.').ToLowerInvariant();
            if (c == "fish")
                _quests.OnFish(ctx.User.Id);
            else if (c == "trivia")
                _quests.OnTrivia(ctx.User.Id);
        }
        catch { }
        return default;
    }

    // ---- member commands ---------------------------------------------------

    [cmd("lfhelp", "lostfound")]
    public async Task LfHelp(AnyContext ctx)
    {
        var embed = new EmbedBuilder()
            .WithColor(Sage)
            .WithTitle("REMY'S LOST & FOUND")
            .WithDescription(
                "Remy keeps finding things. You keep almost losing them.\n" +
                "Explore, collect, complete sets — and maybe a Relic.")
            .AddField("EXPLORE",
                "`.explore` — choose somewhere to look\n" +
                "`.explore <area>` — go (limited per day)\n" +
                "`.grab` — claim what Remy drops")
            .AddField("YOUR STUFF",
                "`.shelf [@user]` — collection profile\n" +
                "`.inventory` — everything you've kept\n" +
                "`.item <name>` — item details\n" +
                "`.sets` — set progress\n" +
                "`.collectiontop` — collection leaderboard")
            .AddField("USE IT",
                "`.craft [recipe]` — make something\n" +
                "`.equip <item>` / `.unequip` — one charm\n" +
                "`.recycle <item> [confirm]` — into Scraps\n" +
                "`.gift @user <item>` — pass it on\n" +
                "`.trade @user <item>` — offer a trade")
            .AddField("ODDS & ENDS",
                "`.quests` — weekly side quests\n" +
                "`.encounter` / `.lfchoose <n>` — resolve a choice\n" +
                "`.openparcel` / `.giftparcel @user` — mystery parcels")
            .WithFooter("Juniper • .lfhelp");
        await ctx.Channel.EmbedAsync(embed);
    }

    [cmd]
    public async Task Explore(AnyContext ctx, string area = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(area))
            {
                var p = _core.Get(ctx.User.Id);
                var offer = _explore.OfferAreas(p);
                var sb = new StringBuilder();
                sb.AppendLine("Somewhere to start?");
                foreach (var a in offer)
                    sb.AppendLine($"`.explore {a.Id}` — **{a.Name}** — {a.Description}");
                if (offer.Count == 0)
                    sb.AppendLine("Nothing open yet. Try the Bedroom.");
                sb.AppendLine();
                sb.AppendLine($"Expeditions left today: **{_core.RemainingExpeditions(ctx.User.Id)}**");
                await ctx.Channel.EmbedAsync(new EmbedBuilder().WithColor(Mauve).WithTitle("Lost & Found").WithDescription(sb.ToString()));
                return;
            }

            var res = _explore.Explore(ctx.User.Id, area.Trim());
            if (!res.Ok)
            {
                await ReportExploreFailure(ctx, res);
                return;
            }

            _quests.OnExpedition(ctx.User.Id);

            if (res.Encounter is not null)
            {
                _encounters.Offer(ctx.User.Id, res.Encounter, res.Area.Id);
                await SendEncounter(ctx, res.Encounter, res.Area);
                return;
            }

            if (res.Item is not null)
                _quests.OnFind(ctx.User.Id, res.Item);

            await SendItemAsync(ctx, res.Item, BuildItemEmbed(ctx.User.Id, res.Item, res.Grant, "FOUND",
                res.Area?.Flavour), res.Relic,
                extra: LevelNote(res.Grant) + SetNote(ctx.User.Id, res.Item) +
                       $"\nExpeditions left today: **{res.Remaining}**");
        }
        catch (Exception ex)
        {
            await ctx.SendErrorAsync("Something went wrong out there. " + ex.Message);
        }
    }

    private async Task ReportExploreFailure(AnyContext ctx, ExploreResult res)
    {
        switch (res.Failure)
        {
            case "unknown":
                await ctx.SendErrorAsync("No area by that name.");
                break;
            case "locked":
                await ctx.SendPendingAsync($"You can't get into **{res.Area?.Name}** yet." +
                    (res.Area is not null && res.Area.Secret
                        ? " It isn't open to you."
                        : $" It opens at level {res.Area?.UnlockLevel}."));
                break;
            case "daily":
                await ctx.SendPendingAsync("You've already been out today. Remy needs a rest too.");
                break;
            default:
                await ctx.SendErrorAsync("Lost & Found is off right now.");
                break;
        }
    }

    private async Task SendEncounter(AnyContext ctx, EncounterDef enc, AreaDef area)
    {
        var sb = new StringBuilder();
        sb.AppendLine(enc.Text);
        sb.AppendLine();
        for (var i = 0; i < enc.Options.Count; i++)
            sb.AppendLine($"**{i + 1}.** {enc.Options[i].Label}");
        sb.AppendLine();
        sb.AppendLine("`.lfchoose <number>`");
        await ctx.Channel.EmbedAsync(new EmbedBuilder()
            .WithColor(Mauve)
            .WithTitle($"Encounter — {enc.Title}")
            .WithDescription(sb.ToString())
            .WithFooter(area is null ? "Juniper" : area.Name));
    }

    [cmd("lfchoose")]
    public async Task LfChoose(AnyContext ctx, int index)
    {
        var pending = _encounters.Get(ctx.User.Id);
        if (pending is null)
        {
            await ctx.SendPendingAsync("There's nothing waiting on you.");
            return;
        }

        var outcome = _encounters.Choose(ctx.User.Id, index);
        if (!outcome.Ok)
        {
            await ctx.SendPendingAsync(outcome.Text);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(outcome.Text);
        if (outcome.Grant is not null && outcome.Item is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"**{outcome.Item.Name}** {RarityUtil.Stars(outcome.Item.Rarity)} {RarityUtil.Label(outcome.Item.Rarity)}");
            sb.AppendLine("_" + outcome.Item.Description + "_");
            sb.AppendLine("+" + outcome.Grant.Xp + " Explorer XP, +" + outcome.Grant.Tokens + " Tokens");
            _quests.OnFind(ctx.User.Id, outcome.Item);
        }
        else if (outcome.Xp > 0)
            sb.AppendLine("+" + outcome.Xp + " Explorer XP");
        else if (outcome.Tokens > 0)
            sb.AppendLine("+" + outcome.Tokens + " Tokens");
        else if (outcome.Scraps > 0)
            sb.AppendLine("+" + outcome.Scraps + " Scraps");

        foreach (var set in outcome.CompletedSets)
            sb.AppendLine($"\nSet complete: **{set.Name}**!");

        var color = outcome.Item is not null ? RarityColor(outcome.Item.Rarity) : Sage;
        await ctx.Channel.EmbedAsync(new EmbedBuilder().WithColor(color).WithTitle("Encounter").WithDescription(sb.ToString()));
    }

    [cmd("encounter")]
    public async Task Encounter(AnyContext ctx)
    {
        var pending = _encounters.Get(ctx.User.Id);
        if (pending is null)
        {
            await ctx.SendPendingAsync("Nothing pending. Go for a walk with `.explore`.");
            return;
        }
        await SendEncounter(ctx, pending.Encounter, _catalog.Area(pending.AreaId));
    }

    [cmd]
    public async Task Grab(GuildContext ctx)
    {
        var res = _drops.Grab(ctx.Guild.Id, ctx.Channel.Id, ctx.User.Id);
        switch (res.Status)
        {
            case "nothing":
                await ctx.SendPendingAsync("Nothing here to grab.");
                return;
            case "taken":
                await ctx.SendPendingAsync("Too slow, or you already have it.");
                return;
            case "parcel":
                await SendItemAsync(ctx, new ItemDef { Image = "mystery-parcel.png" },
                    new EmbedBuilder().WithColor(Blush).WithTitle("A Mystery Parcel")
                        .WithDescription("You pocketed something wrapped in brown paper. `.openparcel` when you're ready."),
                    false);
                return;
        }

        _quests.OnClaimDrop(ctx.User.Id);
        if (res.Item is not null)
            _quests.OnFind(ctx.User.Id, res.Item);

        await SendItemAsync(ctx, res.Item, BuildItemEmbed(ctx.User.Id, res.Item, res.Grant, "GRABBED", null),
            res.Item?.ReleaseRelic == true,
            extra: LevelNote(res.Grant) + SetNote(ctx.User.Id, res.Item));
    }

    [cmd]
    public async Task Sets(AnyContext ctx)
    {
        var progress = _collection.Progress(ctx.User.Id);
        var sb = new StringBuilder();
        var done = 0;
        foreach (var sp in progress)
        {
            if (sp.Hidden)
            {
                sb.AppendLine("**???** — ?? / ??");
                continue;
            }
            if (sp.Completed) done++;
            var mark = sp.Completed ? " ✔" : "";
            sb.AppendLine($"**{sp.Set.Name}** — {sp.Discovered} / {sp.Total}{mark}");
        }
        sb.AppendLine();
        sb.AppendLine($"{done} set{(done == 1 ? "" : "s")} complete. Rewards are one-time.");

        await ctx.Channel.EmbedAsync(new EmbedBuilder()
            .WithColor(done > 0 ? Sage : Mauve)
            .WithTitle("Sets")
            .WithDescription(sb.ToString()));
    }

    [cmd]
    public async Task Shelf(AnyContext ctx, IUser user = null)
    {
        var target = user ?? ctx.User;
        var p = _core.Get(target.Id);
        var need = _cfg.XpCurve.XpToNext(p.Level);
        var inv = _inventory.List(target.Id);
        var totalItems = Math.Max(1, _catalog.Cfg.Items.Count);
        var pct = (int)Math.Round(100.0 * inv.Count / totalItems);
        var recent = _core.RecentDiscoveries(target.Id, _cfg.MaxRecentDiscoveries);
        var doneSets = _core.CompletedSets(target.Id);
        var titles = _core.Titles(target.Id);
        var equipped = _inventory.Equipped(target.Id);

        var rarest = inv
            .Select(e => _catalog.Item(e.ItemId))
            .Where(i => i is not null)
            .OrderByDescending(i => i.Rarity)
            .FirstOrDefault();
        var favourite = inv.OrderByDescending(e => e.Qty).FirstOrDefault();

        var sb = new StringBuilder();
        sb.AppendLine($"**Level {p.Level}** — {p.ExplorerXp}/{need} XP");
        sb.AppendLine($"Unique finds: **{inv.Count}** ({pct}%) · Score: **{p.CollectionScore}**");
        var balance = _economy.GetBalance(target.Id);
        sb.AppendLine($"Tokens earned here: **{p.TokensEarned}**" + (balance >= 0 ? $" · Balance: **{balance}**" : ""));
        sb.AppendLine($"Scraps: **{p.Scraps}** · Parcels: **{_parcels.Count(target.Id)}**");

        var embed = new EmbedBuilder()
            .WithColor(Sage)
            .WithTitle($"{DisplayName(target)}'s Shelf")
            .WithDescription(sb.ToString());

        if (rarest is not null)
            embed.AddField("Rarest find", $"{rarest.Name} {RarityUtil.Stars(rarest.Rarity)}", true);
        if (favourite is not null && _catalog.Item(favourite.ItemId) is { } fav)
            embed.AddField("Favourite", $"{fav.Name} ×{favourite.Qty}", true);
        if (!string.IsNullOrEmpty(equipped) && _catalog.Item(equipped) is { } eq)
            embed.AddField("Charm", eq.Name, true);
        if (doneSets.Count > 0)
            embed.AddField("Pins", string.Join(" · ", doneSets.Take(6).Select(s => _catalog.Set(s)?.Name ?? s)), false);
        if (titles.Count > 0)
            embed.AddField("Titles", string.Join(" · ", titles.Take(6)), false);
        if (recent.Count > 0)
        {
            var line = string.Join("\n", recent.Select(r => $"{_catalog.Item(r.ItemId)?.Name ?? r.ItemId}"));
            embed.AddField("Recent finds", line, false);
        }

        embed.WithFooter($"Juniper • .shelf");
        await ctx.Channel.EmbedAsync(embed);
    }

    [cmd]
    public async Task Inventory(AnyContext ctx)
    {
        var inv = _inventory.List(ctx.User.Id);
        if (inv.Count == 0)
        {
            await ctx.SendPendingAsync("Nothing kept yet. `.explore`.");
            return;
        }

        var bySet = inv
            .GroupBy(e => _catalog.Item(e.ItemId)?.Set ?? "other")
            .OrderBy(g => g.Key);

        var sb = new StringBuilder();
        foreach (var group in bySet)
        {
            var setName = _catalog.Set(group.Key)?.Name ?? group.Key;
            sb.AppendLine($"**{setName}**");
            foreach (var e in group.OrderByDescending(e => _catalog.Item(e.ItemId)?.Rarity ?? Rarity.Common))
            {
                var item = _catalog.Item(e.ItemId);
                if (item is null) continue;
                sb.AppendLine($"  {RarityUtil.Stars(item.Rarity)} {item.Name} ×{e.Qty}");
            }
        }

        var text = sb.ToString();
        if (text.Length > 3800) text = text.Substring(0, 3800) + "\n…";

        await ctx.Channel.EmbedAsync(new EmbedBuilder()
            .WithColor(Mauve)
            .WithTitle($"Inventory — {inv.Sum(e => e.Qty)} items, {inv.Count} unique")
            .WithDescription(text));
    }

    [cmd]
    public async Task Item(AnyContext ctx, [leftover] string name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            await ctx.SendPendingAsync("Which item? `.item <name>`.");
            return;
        }

        var item = _catalog.FindFuzzy(name);
        if (item is null)
        {
            var matches = _catalog.FindAllFuzzy(name);
            if (matches.Count > 1)
                await ctx.SendPendingAsync("Several match: " + string.Join(", ", matches.Take(8).Select(m => m.Name)));
            else
                await ctx.SendPendingAsync("No item by that name.");
            return;
        }

        var qty = _inventory.Qty(ctx.User.Id, item.Id);
        var sp = _collection.Progress(ctx.User.Id).FirstOrDefault(x => x.Set.Id == item.Set);
        var sb = new StringBuilder();
        sb.AppendLine($"**{item.Name}** {RarityUtil.Stars(item.Rarity)} {RarityUtil.Label(item.Rarity)}");
        sb.AppendLine("_" + item.Description + "_");
        sb.AppendLine();
        sb.AppendLine($"Set: **{_catalog.Set(item.Set)?.Name ?? item.Set}** ({sp?.Discovered ?? 0}/{sp?.Total ?? 0})");
        sb.AppendLine($"Value: {item.Value(_cfg)} · Tradable: {(item.Tradable ? "yes" : "no")} · Recyclable: {(item.Recyclable ? "yes" : "no")}");
        if (!string.IsNullOrEmpty(item.Effect) && _cfg.Charms.TryGetValue(item.Id, out var charm))
            sb.AppendLine($"Charm: {charm.Description}");
        sb.AppendLine($"Owned: **{qty}**");

        await ctx.Channel.EmbedAsync(new EmbedBuilder()
            .WithColor(RarityColor(item.Rarity))
            .WithTitle("Item")
            .WithDescription(sb.ToString()));
    }

    [cmd]
    public async Task Gift(AnyContext ctx, IUser user = null, [leftover] string name = null)
    {
        if (user is null || string.IsNullOrWhiteSpace(name))
        {
            await ctx.SendPendingAsync("Usage: `.gift @someone <item>`.");
            return;
        }
        if (user.Id == ctx.User.Id)
        {
            await ctx.SendPendingAsync("Gifting yourself. Bold.");
            return;
        }

        var item = _catalog.FindFuzzy(name);
        if (item is null)
        {
            await ctx.SendPendingAsync("No item by that name.");
            return;
        }

        var res = _trade.Gift(ctx.User.Id, user.Id, item.Id);
        switch (res)
        {
            case TransferResult.Ok:
                _quests.OnGift(ctx.User.Id);
                await ctx.SendConfirmAsync($"Gave **{item.Name}** to {DisplayName(user)}.");
                break;
            case TransferResult.NotTradable:
                await ctx.SendErrorAsync($"{item.Name} can't be traded.");
                break;
            case TransferResult.Self:
                await ctx.SendErrorAsync("That's you.");
                break;
            default:
                await ctx.SendPendingAsync("You don't have that.");
                break;
        }
    }

    [cmd]
    public async Task Recycle(AnyContext ctx, [leftover] string args = null)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await ctx.SendPendingAsync("Usage: `.recycle <item> [confirm]`.");
            return;
        }

        var confirm = args.Trim().EndsWith("confirm", StringComparison.OrdinalIgnoreCase);
        var name = confirm ? args.Trim()[..^"confirm".Length].Trim() : args.Trim();
        var item = _catalog.FindFuzzy(name);
        if (item is null)
        {
            await ctx.SendPendingAsync("No item by that name.");
            return;
        }

        var (result, scraps) = _inventory.Recycle(ctx.User.Id, item.Id, confirm);
        switch (result)
        {
            case RecycleResult.Ok:
                await ctx.SendConfirmAsync($"Broke down **{item.Name}** for **{scraps}** Scraps.");
                break;
            case RecycleResult.NotRecyclable:
                await ctx.SendErrorAsync($"{item.Name} can't be broken down.");
                break;
            case RecycleResult.Protected:
                await ctx.SendPendingAsync($"That's your last **{item.Name}**. Add `confirm` if you're sure: `.recycle {item.Name} confirm`.");
                break;
            default:
                await ctx.SendPendingAsync("You don't have that.");
                break;
        }
    }

    [cmd]
    public async Task Craft(AnyContext ctx, [leftover] string key = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            var sb = new StringBuilder();
            foreach (var r in _crafting.Recipes)
            {
                var inputs = string.Join(" + ", r.Inputs.Select(kv => $"{kv.Value}× {_catalog.Item(kv.Key)?.Name ?? kv.Key}"));
                sb.AppendLine($"**{r.Name}** → {_catalog.Item(r.Output)?.Name ?? r.Output}");
                sb.AppendLine($"  {inputs}");
            }
            await ctx.Channel.EmbedAsync(new EmbedBuilder()
                .WithColor(Mauve)
                .WithTitle("Recipes")
                .WithDescription(sb.ToString()));
            return;
        }

        var res = _crafting.Craft(ctx.User.Id, key);
        if (!res.Ok)
        {
            await ctx.SendPendingAsync(res.Message);
            return;
        }

        _quests.OnCraft(ctx.User.Id);
        var sb2 = new StringBuilder();
        sb2.AppendLine($"Made **{res.Output.Name}** {RarityUtil.Stars(res.Output.Rarity)}");
        sb2.AppendLine("_" + res.Output.Description + "_");
        foreach (var s in res.CompletedSets)
            sb2.AppendLine($"\nSet complete: **{s.Name}**!");

        await ctx.Channel.EmbedAsync(new EmbedBuilder()
            .WithColor(RarityColor(res.Output.Rarity))
            .WithTitle("Crafted")
            .WithDescription(sb2.ToString()));
    }

    [cmd]
    public async Task Equip(AnyContext ctx, [leftover] string name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            await ctx.SendPendingAsync("Usage: `.equip <item>`. Charms only.");
            return;
        }
        var item = _catalog.FindFuzzy(name);
        if (item is null)
        {
            await ctx.SendPendingAsync("No item by that name.");
            return;
        }

        var res = _inventory.Equip(ctx.User.Id, item.Id);
        switch (res)
        {
            case EquipResult.Ok:
                await ctx.SendConfirmAsync($"Equipped **{item.Name}**. " + _cfg.Charms[item.Id].Description);
                break;
            case EquipResult.NotCharm:
                await ctx.SendPendingAsync($"{item.Name} isn't a charm.");
                break;
            default:
                await ctx.SendPendingAsync("You don't have that.");
                break;
        }
    }

    [cmd]
    public async Task Unequip(AnyContext ctx)
    {
        var equipped = _inventory.Equipped(ctx.User.Id);
        if (string.IsNullOrEmpty(equipped))
        {
            await ctx.SendPendingAsync("You're not wearing a charm.");
            return;
        }
        _inventory.Unequip(ctx.User.Id);
        await ctx.SendConfirmAsync("Charm unequipped.");
    }

    [cmd("quests", "lfquests")]
    public async Task LfQuests(AnyContext ctx)
    {
        var rows = _quests.List(ctx.User.Id);
        var sb = new StringBuilder();
        sb.AppendLine("Weekly side quests. Rewards are granted automatically.");
        sb.AppendLine();
        foreach (var row in rows)
        {
            var name = row.Def?.Name ?? row.QuestId;
            var desc = row.Def?.Description ?? "";
            var bar = row.Completed ? "done" : $"{row.Progress}/{row.Target}";
            sb.AppendLine($"**{name}** — {bar}");
            sb.AppendLine($"  _{desc}_");
        }
        await ctx.Channel.EmbedAsync(new EmbedBuilder()
            .WithColor(rows.Any(r => r.Completed) ? Sage : Mauve)
            .WithTitle("Quests")
            .WithDescription(sb.ToString()));
    }

    [cmd]
    public async Task OpenParcel(AnyContext ctx)
    {
        var res = _parcels.Open(ctx.User.Id);
        if (!res.Ok)
        {
            await ctx.SendPendingAsync(res.Message);
            return;
        }

        var sb = new StringBuilder();
        if (res.Item is not null)
        {
            sb.AppendLine($"A **{res.Item.Name}** {RarityUtil.Stars(res.Item.Rarity)}");
            sb.AppendLine("_" + res.Item.Description + "_");
            _quests.OnFind(ctx.User.Id, res.Item);
        }
        if (res.Scraps > 0)
            sb.AppendLine($"+{res.Scraps} Scraps");
        foreach (var s in res.CompletedSets)
            sb.AppendLine($"\nSet complete: **{s.Name}**!");

        await SendItemAsync(ctx, res.Item,
            new EmbedBuilder().WithColor(res.Item is not null ? RarityColor(res.Item.Rarity) : Sage)
                .WithTitle("Parcel opened").WithDescription(sb.ToString()),
            res.Item?.ReleaseRelic == true);
    }

    [cmd]
    public async Task GiftParcel(AnyContext ctx, IUser user = null)
    {
        if (user is null)
        {
            await ctx.SendPendingAsync("Usage: `.giftparcel @someone`.");
            return;
        }
        if (user.Id == ctx.User.Id)
        {
            await ctx.SendPendingAsync("That's you.");
            return;
        }
        if (!_parcels.Gift(ctx.User.Id, user.Id))
        {
            await ctx.SendPendingAsync("You don't have a parcel to give.");
            return;
        }
        await ctx.SendConfirmAsync($"Sent a mystery parcel to {DisplayName(user)}.");
    }

    [cmd("collectiontop", "ctop")]
    public async Task CollectionTop(AnyContext ctx)
    {
        var top = _collection.Top(10);
        var sb = new StringBuilder();
        var i = 1;
        foreach (var row in top)
        {
            sb.AppendLine($"**{i}.** <@{row.UserId}> — score **{row.Score}** · {row.UniqueFinds} unique · Lv{row.Level}");
            i++;
        }
        if (top.Count == 0) sb.AppendLine("No collections yet.");
        await ctx.Channel.EmbedAsync(new EmbedBuilder()
            .WithColor(Sage)
            .WithTitle("Collection Top")
            .WithDescription(sb.ToString())
            .WithFooter("Unique finds, rarity, sets and relics — not duplicate volume"));
    }

    [cmd]
    public async Task Trade(AnyContext ctx, IUser user = null, [leftover] string name = null)
    {
        if (user is null || string.IsNullOrWhiteSpace(name))
        {
            await ctx.SendPendingAsync("Usage: `.trade @someone <item>`.");
            return;
        }
        var item = _catalog.FindFuzzy(name);
        if (item is null)
        {
            await ctx.SendPendingAsync("No item by that name.");
            return;
        }
        var (ok, message, id) = _trade.Offer(ctx.User.Id, user.Id, item.Id);
        if (!ok)
        {
            await ctx.SendPendingAsync(message);
            return;
        }
        await ctx.SendConfirmAsync($"Offered **{item.Name}** to {DisplayName(user)} (offer #{id}). They can `.tradeaccept` or `.tradedecline`.");
    }

    [cmd("tradeaccept")]
    public async Task TradeAccept(AnyContext ctx)
    {
        var (ok, message) = _trade.Accept(ctx.User.Id);
        if (ok)
        {
            _quests.OnGift(ctx.User.Id);
            await ctx.SendConfirmAsync(message);
        }
        else
            await ctx.SendPendingAsync(message);
    }

    [cmd("tradedecline")]
    public async Task TradeDecline(AnyContext ctx)
    {
        if (_trade.Decline(ctx.User.Id))
            await ctx.SendConfirmAsync("Offer declined.");
        else
            await ctx.SendPendingAsync("No offers waiting.");
    }

    // ---- admin -------------------------------------------------------------

    [bot_owner_only]
    [cmd]
    public async Task LfSpawn(GuildContext ctx, [leftover] string name = null)
    {
        ItemDef item = null;
        if (!string.IsNullOrWhiteSpace(name))
        {
            item = _catalog.FindFuzzy(name);
            if (item is null)
            {
                await ctx.SendErrorAsync("No item by that name.");
                return;
            }
        }
        var id = await _drops.SpawnAsync(ctx.Channel, ctx.Guild.Id, item?.Id);
        await ctx.SendConfirmAsync(id > 0 ? $"Spawned {(item?.Name ?? "a random drop")}." : "Nothing to spawn.");
    }

    [bot_owner_only]
    [cmd]
    public async Task LfGive(GuildContext ctx, IUser user = null, [leftover] string name = null)
    {
        if (user is null || string.IsNullOrWhiteSpace(name))
        {
            await ctx.SendPendingAsync("Usage: `.lfgive @user <item>`.");
            return;
        }
        var item = _catalog.FindFuzzy(name);
        if (item is null)
        {
            await ctx.SendErrorAsync("No item by that name.");
            return;
        }
        _core.Grant(user.Id, item);
        await ctx.SendConfirmAsync($"Gave **{item.Name}** to {DisplayName(user)}.");
    }

    [bot_owner_only]
    [cmd]
    public async Task LfResetDaily(GuildContext ctx, IUser user = null)
    {
        if (user is null)
        {
            await ctx.SendPendingAsync("Usage: `.lfresetdaily @user`.");
            return;
        }
        _core.ResetDaily(user.Id);
        await ctx.SendConfirmAsync($"Reset daily expeditions for {DisplayName(user)}.");
    }

    [bot_owner_only]
    [cmd]
    public async Task LfSetLevel(GuildContext ctx, IUser user = null, int level = 1)
    {
        if (user is null)
        {
            await ctx.SendPendingAsync("Usage: `.lfsetlevel @user <level>`.");
            return;
        }
        _core.SetLevel(user.Id, level);
        await ctx.SendConfirmAsync($"{DisplayName(user)} is now Explorer level {level}.");
    }

    [bot_owner_only]
    [cmd]
    public async Task LfDebug(GuildContext ctx, IUser user = null)
    {
        var target = user ?? ctx.User;
        var p = _core.Get(target.Id);
        var inv = _inventory.List(target.Id);
        var sets = _core.CompletedSets(target.Id);
        var quests = _quests.List(target.Id);
        var equipped = _inventory.Equipped(target.Id);
        var parcels = _parcels.Count(target.Id);

        var sb = new StringBuilder();
        sb.AppendLine($"user `{target.Id}`");
        sb.AppendLine($"level {p.Level} · xp {p.ExplorerXp} · totalXp {_core.TotalXp(p)}");
        sb.AppendLine($"finds {p.TotalFinds} · unique {p.UniqueFinds} · score {p.CollectionScore}");
        sb.AppendLine($"scraps {p.Scraps} · tokensEarned {p.TokensEarned} · balance {_economy.GetBalance(target.Id)}");
        sb.AppendLine($"dailyUsed {p.DailyUsed} on {p.DailyDate} · remaining {_core.RemainingExpeditions(target.Id)}");
        sb.AppendLine($"inventory {inv.Count} unique · parcels {parcels} · charm {equipped ?? "none"}");
        sb.AppendLine($"sets done: {(sets.Count == 0 ? "none" : string.Join(", ", sets))}");
        sb.AppendLine($"quests: {string.Join(", ", quests.Select(q => $"{q.QuestId} {q.Progress}/{q.Target}{(q.Completed ? "*" : "")}"))}");

        await ctx.Channel.EmbedAsync(new EmbedBuilder()
            .WithColor(Forest)
            .WithTitle("Lost & Found debug")
            .WithDescription(sb.ToString()));
    }

    [bot_owner_only]
    [cmd]
    public async Task LfReload(AnyContext ctx)
    {
        BuildServices();
        await ctx.SendConfirmAsync($"Reloaded. {_cfg.Items.Count} items, {_cfg.Sets.Count} sets, {_cfg.Areas.Count} areas.");
    }

    // ---- rendering helpers -------------------------------------------------

    private EmbedBuilder BuildItemEmbed(ulong userId, ItemDef item, GrantResult grant, string title, string flavour)
    {
        var sb = new StringBuilder();
        if (item is null)
        {
            sb.AppendLine("Nothing. Just dust.");
            return new EmbedBuilder().WithColor(Cream).WithTitle(title).WithDescription(sb.ToString());
        }

        sb.AppendLine($"**{item.Name}** {RarityUtil.Stars(item.Rarity)} {RarityUtil.Label(item.Rarity)}");
        sb.AppendLine("_" + item.Description + "_");
        if (!string.IsNullOrWhiteSpace(flavour))
            sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(flavour))
            sb.AppendLine("_" + flavour + "_");
        if (grant is not null && grant.IsNew)
            sb.AppendLine("\nNew to your collection.");

        return new EmbedBuilder()
            .WithColor(RarityColor(item.Rarity))
            .WithTitle(title)
            .WithDescription(sb.ToString());
    }

    private async Task SendItemAsync(AnyContext ctx, ItemDef item, EmbedBuilder embed, bool relic, string extra = null)
    {
        if (!string.IsNullOrWhiteSpace(extra))
            embed.AddField("Result", extra, false);
        if (item is not null && !string.IsNullOrWhiteSpace(item.Image))
        {
            var path = Path.Combine(_assetsDir, item.Image);
            if (File.Exists(path))
            {
                embed.WithThumbnailUrl($"attachment://{item.Image}");
                await ctx.Channel.SendFileAsync(path, embed: embed.Build());
                return;
            }
        }
        await ctx.Channel.EmbedAsync(embed);
    }

    private string LevelNote(GrantResult grant)
    {
        if (grant is null) return "";
        var sb = new StringBuilder();
        sb.Append($"+{grant.Xp} Explorer XP, +{grant.Tokens} Tokens");
        if (grant.LevelsGained > 0)
            sb.Append($"\nLevel up ×{grant.LevelsGained}!");
        return sb.ToString();
    }

    private string SetNote(ulong userId, ItemDef item)
    {
        if (item is null) return "";
        var sp = _collection.Progress(userId).FirstOrDefault(x => x.Set.Id == item.Set);
        if (sp is null) return "";
        return $"\n{sp.Set.Name}: {sp.Discovered}/{sp.Total}";
    }

    private static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Common => Cream,
        Rarity.Uncommon => Sage,
        Rarity.Rare => Mauve,
        Rarity.Epic => Mauve,
        Rarity.Legendary => Blush,
        Rarity.SarahRelic => Blush,
        _ => Cream
    };

    private static string DisplayName(IUser user)
        => user is IGuildUser gu ? (gu.Nickname ?? gu.Username) : user.Username;
}
