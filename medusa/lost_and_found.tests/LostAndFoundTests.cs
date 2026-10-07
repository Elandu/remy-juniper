using System.Collections.Concurrent;
using LostAndFound;
using Xunit;

namespace LostAndFound.Tests;

public class LostAndFoundTests
{
    // ---- rarity selection --------------------------------------------------

    [Fact]
    public void RollRarity_OnlyReturnsPositiveWeights()
    {
        var weights = new Dictionary<Rarity, double>
        {
            [Rarity.Common] = 1,
            [Rarity.Legendary] = 0
        };

        for (var i = 0; i < 200; i++)
            Assert.Equal(Rarity.Common, Loot.RollRarity(weights, new SequenceRandom(i / 200.0)));

        Assert.Null(Loot.RollRarity(new Dictionary<Rarity, double> { [Rarity.Common] = 0 }, new SequenceRandom(0.5)));
    }

    [Fact]
    public void RollRarity_RespectsDistributionBoundaries()
    {
        var weights = new Dictionary<Rarity, double>
        {
            [Rarity.Common] = 50,
            [Rarity.Uncommon] = 50
        };

        // 0.10 * 100 = 10 -> Common; 0.90 * 100 = 90 -> Uncommon
        Assert.Equal(Rarity.Common, Loot.RollRarity(weights, new SequenceRandom(0.10)));
        Assert.Equal(Rarity.Uncommon, Loot.RollRarity(weights, new SequenceRandom(0.90)));
    }

    // ---- XP / level --------------------------------------------------------

    [Fact]
    public void LevelCurve_IsQuickEarly_AndSlowerLater()
    {
        var curve = new LevelCurve();
        Assert.Equal(100, curve.XpToNext(1));
        Assert.Equal(150, curve.XpToNext(2));
        Assert.Equal(300, curve.XpToNext(5));
        Assert.True(curve.XpToNext(20) > curve.XpToNext(19));
        Assert.True(curve.XpToNext(15) > curve.XpToNext(10));
    }

    [Fact]
    public void ApplyXp_LevelsUpAndCarriesRemainder()
    {
        using var h = new Harness();
        h.Core.MoreXp(1, 260); // 100 -> lvl2, 150 -> lvl3, 10 carried
        var p = h.Core.Get(1);
        Assert.Equal(3, p.Level);
        Assert.Equal(10, p.ExplorerXp);
    }

    // ---- daily limits ------------------------------------------------------

    [Fact]
    public void DailyExpedition_LimitsAndResets()
    {
        using var h = new Harness(c => c.DailyExpeditions = 1);
        var original = global::LostAndFound.Clock.Now;
        global::LostAndFound.Clock.Now = () => new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero);
        try
        {
            Assert.True(h.Core.UseDailyExpedition(1).allowed);
            Assert.False(h.Core.UseDailyExpedition(1).allowed);
            Assert.Equal(0, h.Core.RemainingExpeditions(1));

            h.Core.ResetDaily(1);
            Assert.True(h.Core.UseDailyExpedition(1).allowed);
        }
        finally
        {
            global::LostAndFound.Clock.Now = original;
        }
    }

    [Fact]
    public void Explore_SecondExpeditionSameDayIsRefused()
    {
        using var h = new Harness(c => c.DailyExpeditions = 1);
        var first = h.Explore.Explore(1, "bedroom");
        Assert.True(first.Ok);
        var second = h.Explore.Explore(1, "bedroom");
        Assert.False(second.Ok);
        Assert.Equal("daily", second.Failure);
    }

    // ---- atomic drop claim -------------------------------------------------

    [Fact]
    public void DropClaim_TwoConcurrentClaims_OnlyOneWins()
    {
        using var h = new Harness(c => { c.Drops.Copies = 1; c.Drops.ClaimTimeoutMinutes = 30; });
        h.Drops.CreateDrop(1, 2, "squeaky_ball");

        var results = new ConcurrentBag<string>();
        Parallel.For(0, 2, i =>
        {
            var r = h.Drops.Grab(1, 2, (ulong)(100 + i));
            results.Add(r.Status);
        });

        Assert.Equal(1, results.Count(s => s == "ok"));
        Assert.Equal(1, results.Count(s => s == "taken"));
    }

    [Fact]
    public void DropClaim_LimitedCopies_AllCopiesClaimed()
    {
        using var h = new Harness(c => { c.Drops.Copies = 3; c.Drops.ClaimTimeoutMinutes = 30; });
        h.Drops.CreateDrop(1, 2, "squeaky_ball", copiesOverride: 3);

        Assert.Equal("ok", h.Drops.Grab(1, 2, 1).Status);
        Assert.Equal("ok", h.Drops.Grab(1, 2, 2).Status);
        Assert.Equal("ok", h.Drops.Grab(1, 2, 3).Status);
        Assert.Equal("nothing", h.Drops.Grab(1, 2, 4).Status);
    }

    // ---- inventory / collection -------------------------------------------

    [Fact]
    public void DuplicateInventory_IncrementsQtyButNotUniqueness()
    {
        using var h = new Harness();
        var item = h.Cat.Item("squeaky_ball");

        var first = h.Core.Grant(5, item);
        Assert.True(first.IsNew);
        Assert.Equal(1, first.Qty);
        var scoreAfterFirst = h.Core.Get(5).CollectionScore;

        var second = h.Core.Grant(5, item);
        Assert.False(second.IsNew);
        Assert.Equal(2, second.Qty);

        var p = h.Core.Get(5);
        Assert.Equal(1, p.UniqueFinds);
        Assert.Equal(2, p.TotalFinds);
        Assert.Equal(scoreAfterFirst, p.CollectionScore);
    }

    [Fact]
    public void CollectionScore_IgnoresDuplicateSpam()
    {
        using var h = new Harness();
        var item = h.Cat.Item("squeaky_ball");

        for (var i = 0; i < 25; i++)
            h.Core.Grant(9, item);

        Assert.Equal(item.Value(h.Cfg), h.Core.Get(9).CollectionScore);

        var other = h.Cat.Item("stray_pick");
        h.Core.Grant(9, other);
        Assert.Equal(item.Value(h.Cfg) + other.Value(h.Cfg), h.Core.Get(9).CollectionScore);
    }

    [Fact]
    public void SetCompletion_RewardsExactlyOnce()
    {
        using var h = new Harness();
        var set = h.Cat.Set("remys_things");
        foreach (var item in h.Cat.ItemsBySet["remys_things"])
            h.Core.Grant(7, item);

        var completed = h.Collection.CheckCompletions(7);
        Assert.Single(completed);
        Assert.Equal("remys_things", completed[0].Id);

        var tokensAfter = h.Core.Get(7).TokensEarned;
        var levelAfter = h.Core.Get(7).Level;

        var again = h.Collection.CheckCompletions(7);
        Assert.Empty(again);

        var p = h.Core.Get(7);
        Assert.Equal(tokensAfter, p.TokensEarned);
        Assert.Equal(levelAfter, p.Level);

        var rows = h.Db.Scalar<int>(
            "SELECT COUNT(*) FROM completed_sets WHERE user_id=$u AND set_id=$s;",
            ("$u", Db.L(7)), ("$s", "remys_things"));
        Assert.Equal(1, rows);
    }

    // ---- crafting ----------------------------------------------------------

    [Fact]
    public void Crafting_ConsumesExactQuantities_AndRefusesWhenShort()
    {
        using var h = new Harness();
        h.Core.Grant(11, h.Cat.Item("studio_cable"));
        h.Core.Grant(11, h.Cat.Item("studio_cable"));
        h.Core.Grant(11, h.Cat.Item("gaffer_tape"));

        var result = h.Crafting.Craft(11, "mixed_tape");
        Assert.True(result.Ok);
        Assert.Equal("napkin_lyrics", result.Output.Id);

        Assert.Equal(1, h.Inventory.Qty(11, "studio_cable"));
        Assert.Equal(0, h.Inventory.Qty(11, "gaffer_tape"));
        Assert.Equal(1, h.Inventory.Qty(11, "napkin_lyrics"));

        var second = h.Crafting.Craft(11, "mixed_tape");
        Assert.False(second.Ok);
        Assert.Equal(0, h.Inventory.Qty(11, "gaffer_tape"));
    }

    // ---- relics ------------------------------------------------------------

    [Fact]
    public void RelicEligibility_RequiresLevelAndSets_ThenRollsRelic()
    {
        using var h = new Harness(c =>
        {
            c.Relic.MinLevel = 20;
            c.Relic.MinCompletedSets = 3;
            c.Relic.ChancePercent = 100;
        });

        // level 1, no sets -> never
        Assert.Null(h.Collection.TryRollRelic(h.Core.Get(1), new SequenceRandom(0.0)));

        foreach (var setId in new[] { "remys_things", "studio_junk", "night_shift" })
            foreach (var item in h.Cat.ItemsBySet[setId])
                h.Core.Grant(1, item);
        h.Collection.CheckCompletions(1);

        Assert.Equal(3, h.Core.CompletedSetCount(1));

        // still level 1 -> no
        Assert.Null(h.Collection.TryRollRelic(h.Core.Get(1), new SequenceRandom(0.0)));

        h.Core.SetLevel(1, 20);
        var relic = h.Collection.TryRollRelic(h.Core.Get(1), new SequenceRandom(0.0));
        Assert.NotNull(relic);
        Assert.Equal(Rarity.SarahRelic, relic.Rarity);
        Assert.Contains(relic.Image, new[]
        {
            "yes-ive-been-crying-round.png", "tainted-timeline-round.png", "absence-round.png",
            "september-round.png", "apparently-round.png", "frame-round.png", "wollongong-road-round.png"
        });
    }

    // ---- areas -------------------------------------------------------------

    [Fact]
    public void AreaUnlocks_FollowLevelAndExplicitUnlocks()
    {
        using var h = new Harness();
        var bedroom = h.Cat.Area("bedroom");
        var studio = h.Cat.Area("studio");   // level 5
        var secret = h.Cat.Area("unknown");

        Assert.True(h.Explore.AreaAccessible(h.Core.Get(1), bedroom));
        Assert.False(h.Explore.AreaAccessible(h.Core.Get(1), studio));

        h.Core.SetLevel(1, 5);
        Assert.True(h.Explore.AreaAccessible(h.Core.Get(1), studio));

        Assert.False(h.Explore.AreaAccessible(h.Core.Get(1), secret));
        h.Core.UnlockArea(1, "unknown");
        Assert.True(h.Explore.AreaAccessible(h.Core.Get(1), secret));
    }

    [Fact]
    public void OfferAreas_ReturnsAtMostThreeUnlocked()
    {
        using var h = new Harness();
        h.Core.SetLevel(1, 99);
        var offer = h.Explore.OfferAreas(h.Core.Get(1));
        Assert.InRange(offer.Count, 1, 3);
    }

    // ---- gifts / recycle ---------------------------------------------------

    [Fact]
    public void Gift_TransfersOwnership()
    {
        using var h = new Harness();
        h.Core.Grant(1, h.Cat.Item("squeaky_ball"));
        h.Core.Grant(1, h.Cat.Item("squeaky_ball"));

        Assert.Equal(TransferResult.Ok, h.Trade.Gift(1, 2, "squeaky_ball"));
        Assert.Equal(1, h.Inventory.Qty(1, "squeaky_ball"));
        Assert.Equal(1, h.Inventory.Qty(2, "squeaky_ball"));
        Assert.Equal(1, h.Core.Get(2).UniqueFinds);
    }

    [Fact]
    public void Recycle_ProtectsLastCopy_UnlessConfirmed()
    {
        using var h = new Harness();
        h.Core.Grant(1, h.Cat.Item("squeaky_ball"));

        var (result, _) = h.Inventory.Recycle(1, "squeaky_ball", false);
        Assert.Equal(RecycleResult.Protected, result);
        Assert.Equal(1, h.Inventory.Qty(1, "squeaky_ball"));

        var (confirmed, scraps) = h.Inventory.Recycle(1, "squeaky_ball", true);
        Assert.Equal(RecycleResult.Ok, confirmed);
        Assert.True(scraps > 0);
        Assert.Equal(0, h.Inventory.Qty(1, "squeaky_ball"));
    }

    [Fact]
    public void Recycle_SarahRelic_CanNeverBeBrokenDown()
    {
        using var h = new Harness();
        h.Core.Grant(1, h.Cat.Item("relic_absence"));
        h.Core.Grant(1, h.Cat.Item("relic_absence"));

        var (blocked, _) = h.Inventory.Recycle(1, "relic_absence", false);
        Assert.NotEqual(RecycleResult.Ok, blocked);
        Assert.Equal(2, h.Inventory.Qty(1, "relic_absence"));

        var (confirmed, _) = h.Inventory.Recycle(1, "relic_absence", true);
        Assert.NotEqual(RecycleResult.Ok, confirmed);
        Assert.Equal(2, h.Inventory.Qty(1, "relic_absence"));
    }

    // ---- content sanity ----------------------------------------------------

    [Fact]
    public void Catalog_HasExpectedShape()
    {
        using var h = new Harness();
        Assert.True(h.Cfg.Items.Count >= 60, $"expected >= 60 items, got {h.Cfg.Items.Count}");
        Assert.Equal(7, h.Cfg.Areas.Count);
        Assert.Equal(7, h.Cat.Relics().Count);
        Assert.True(h.Cfg.Recipes.Count >= 5);
        Assert.True(h.Cfg.Encounters.Count >= 5);
        Assert.All(h.Cat.Relics(), r => Assert.Equal(Rarity.SarahRelic, r.Rarity));
        Assert.All(h.Cat.Relics(), r => Assert.False(r.Tradable));
    }

    [Fact]
    public void Catalog_EveryItemReferencesAnExistingImageAsset()
    {
        var cfg = new LostFoundConfig();
        cfg.Normalize();

        var assets = Path.Combine(AppContext.BaseDirectory, "assets");
        Assert.True(Directory.Exists(assets), $"assets dir not copied to test output: {assets}");

        foreach (var item in cfg.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Image), $"{item.Id} has no image");
            Assert.EndsWith(".png", item.Image);
            Assert.True(File.Exists(Path.Combine(assets, item.Image)),
                $"{item.Id} -> {item.Image} is not present in the assets folder");
        }

        // Relic art must be the exact release pins.
        var relics = cfg.Items.Where(i => i.Rarity == Rarity.SarahRelic).ToList();
        Assert.Equal(7, relics.Count);
        Assert.All(relics, r => Assert.EndsWith("-round.png", r.Image));
    }
}
