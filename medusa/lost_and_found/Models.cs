using System.Text.Json.Serialization;

namespace LostAndFound;

/// <summary>Item rarity ladder. Sarah Relic tops it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Rarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4,
    SarahRelic = 5
}

public static class RarityUtil
{
    public static string Label(Rarity r) => r switch
    {
        Rarity.Common => "Common",
        Rarity.Uncommon => "Uncommon",
        Rarity.Rare => "Rare",
        Rarity.Epic => "Epic",
        Rarity.Legendary => "Legendary",
        Rarity.SarahRelic => "Sarah Relic",
        _ => "Common"
    };

    /// <summary>1 star for common up to 6 for a Sarah Relic.</summary>
    public static string Stars(Rarity r) => new string('\u2605', (int)r + 1);

    public static bool TryParse(string s, out Rarity r)
    {
        r = Rarity.Common;
        if (string.IsNullOrWhiteSpace(s))
            return false;
        var norm = s.Replace(" ", "").Replace("_", "").Replace("-", "");
        return Enum.TryParse(norm, true, out r);
    }

    public static int DefaultCollectionValue(Rarity r) => r switch
    {
        Rarity.Common => 1,
        Rarity.Uncommon => 2,
        Rarity.Rare => 5,
        Rarity.Epic => 10,
        Rarity.Legendary => 25,
        Rarity.SarahRelic => 100,
        _ => 1
    };
}

public sealed class ItemDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Rarity Rarity { get; set; } = Rarity.Common;
    public string Set { get; set; } = "";
    /// <summary>Area ids this item may appear in. Empty = anywhere its set is offered.</summary>
    public string[] Areas { get; set; } = Array.Empty<string>();
    public double Weight { get; set; } = 1.0;
    public int CollectionValue { get; set; } = 0; // 0 = rarity default
    public bool Tradable { get; set; } = true;
    public bool Recyclable { get; set; } = true;
    public bool Craftable { get; set; } = false;
    public bool Secret { get; set; } = false;
    public string Image { get; set; } = "";
    public string Effect { get; set; } = "";
    public bool ReleaseRelic { get; set; } = false;

    public int Value(LostFoundConfig cfg)
        => CollectionValue > 0 ? CollectionValue : cfg.CollectionValueFor(Rarity);
}

public sealed class SetReward
{
    public int Xp { get; set; }
    public int Tokens { get; set; }
    public string Title { get; set; } = "";
    public string Badge { get; set; } = "";     // inventory item id
    public string UnlockArea { get; set; } = ""; // area id
    public string SpecialItem { get; set; } = ""; // inventory item id
}

public sealed class SetDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Hidden { get; set; } = false;
    public SetReward Reward { get; set; } = new();
}

public sealed class AreaDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Flavour { get; set; } = "";
    public int UnlockLevel { get; set; } = 1;
    public bool Secret { get; set; } = false;
    public string[] Sets { get; set; } = Array.Empty<string>();
    public Dictionary<string, double> RarityWeights { get; set; } = new();
    public string[] Encounters { get; set; } = Array.Empty<string>();
    /// <summary>Sets that get an extra weight bonus while exploring this area.</summary>
    public string[] BonusSets { get; set; } = Array.Empty<string>();
    public double BonusSetWeight { get; set; } = 1.0;
}

public sealed class RecipeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, int> Inputs { get; set; } = new();
    public string Output { get; set; } = "";
    public int OutputQty { get; set; } = 1;
}

public sealed class OutcomeDef
{
    public string Type { get; set; } = "nothing"; // nothing|item|xp|tokens|scraps|effect
    public double Chance { get; set; } = 1.0;
    public string Rarity { get; set; } = "";
    public int Xp { get; set; }
    public int Tokens { get; set; }
    public int Scraps { get; set; }
    public string Effect { get; set; } = "";
    public double EffectValue { get; set; }
    public int EffectMinutes { get; set; }
    public string Text { get; set; } = "";
}

public sealed class EncounterOptionDef
{
    public string Id { get; set; } = "";   // investigate|ignore|remy
    public string Label { get; set; } = "";
    public string Result { get; set; } = "";
    public List<OutcomeDef> Outcomes { get; set; } = new();
}

public sealed class EncounterDef
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public List<EncounterOptionDef> Options { get; set; } = new();
}

public sealed class QuestReward
{
    public int Xp { get; set; }
    public int Tokens { get; set; }
    public int Scraps { get; set; }
    public int Parcels { get; set; }
}

public sealed class QuestDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = ""; // find|find_rarity|fish|trivia|claim_drop|expedition|gift|craft
    public string Target { get; set; } = "";
    public int Amount { get; set; } = 1;
    public QuestReward Reward { get; set; } = new();
}

public sealed class CharmDef
{
    public string Effect { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] BonusSets { get; set; } = Array.Empty<string>();
    public double BonusChance { get; set; }
}

public sealed class ParcelDrop
{
    public string Id { get; set; } = "common";
    public double Weight { get; set; } = 1.0;
    public string Rarity { get; set; } = "Common";
    public int Scraps { get; set; }
}

public sealed class LevelCurve
{
    public int EarlyMax { get; set; } = 5;
    public int EarlyBase { get; set; } = 100;
    public int EarlyStep { get; set; } = 50;
    public int MidMax { get; set; } = 20;
    public int MidBase { get; set; } = 300;
    public int MidStep { get; set; } = 150;
    public double LateBase { get; set; } = 2500;
    public double LateGrowth { get; set; } = 1.15;

    /// <summary>XP required to go from <paramref name="level"/> to the next level.</summary>
    public long XpToNext(int level)
    {
        if (level < 1) level = 1;
        if (level < EarlyMax)
            return EarlyBase + (long)(level - 1) * EarlyStep;
        if (level < MidMax)
            return MidBase + (long)(level - EarlyMax) * MidStep;
        return (long)Math.Round(LateBase * Math.Pow(LateGrowth, level - MidMax));
    }

    public long TotalXpForLevel(int level)
    {
        long total = 0;
        for (var l = 1; l < level; l++)
            total += XpToNext(l);
        return total;
    }
}

public sealed class RelicRequirements
{
    public int MinLevel { get; set; } = 20;
    public int MinCompletedSets { get; set; } = 3;
    /// <summary>Percent chance per eligible expedition/drop to find a Sarah Relic.</summary>
    public double ChancePercent { get; set; } = 1.5;
    /// <summary>Only one relic per this many eligible rolls (safety throttle). 0 = disabled.</summary>
    public int MinExpeditionsBetween { get; set; } = 0;
}

public sealed class DropSettings
{
    public bool Enabled { get; set; } = true;
    public double ChancePerMessage { get; set; } = 0.015;
    public int MinMessagesBetween { get; set; } = 35;
    public int CooldownMinutes { get; set; } = 25;
    public int MaxActivePerChannel { get; set; } = 1;
    public int ClaimTimeoutMinutes { get; set; } = 30;
    public int Copies { get; set; } = 1;
    public double ParcelChance { get; set; } = 0.10;
    /// <summary>If non-empty, only these channels can spawn drops.</summary>
    public ulong[] Channels { get; set; } = Array.Empty<ulong>();
    public ulong[] IgnoredChannels { get; set; } = Array.Empty<ulong>();
    public int MinMessageLength { get; set; } = 4;
}

public sealed class QuestSettings
{
    public bool Enabled { get; set; } = true;
    public int PerWeek { get; set; } = 3; // how many of the pool to assign each week
}

public sealed class PendingEffect
{
    public string Effect { get; set; } = "";
    public double Value { get; set; }
    public long ExpiresAt { get; set; }
}

public sealed class Player
{
    public ulong UserId { get; set; }
    public long ExplorerXp { get; set; }
    public int Level { get; set; } = 1;
    public int TotalFinds { get; set; }
    public int UniqueFinds { get; set; }
    public int CollectionScore { get; set; }
    public int Scraps { get; set; }
    public int TokensEarned { get; set; }
    public string FavouriteItem { get; set; } = "";
    public int DailyUsed { get; set; }
    public string DailyDate { get; set; } = "";
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}

public sealed class InventoryEntry
{
    public string ItemId { get; set; } = "";
    public int Qty { get; set; }
    public long FirstFoundAt { get; set; }
}

public sealed class DropRow
{
    public long Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public string ItemId { get; set; } = "";
    public int TotalCopies { get; set; }
    public int CopiesRemaining { get; set; }
    public long CreatedAt { get; set; }
    public long ExpiresAt { get; set; }
    public bool Active { get; set; }
}
