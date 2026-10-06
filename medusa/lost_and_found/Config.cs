using System.Text.Json;
using System.Text.Json.Serialization;

namespace LostAndFound;

/// <summary>
/// All tunables and content for Lost &amp; Found. Defaults live here (single source of truth);
/// config.json next to the assembly may override any of them. Content can also be supplied
/// wholesale through config.json (items/sets/areas/recipes/encounters/quests/parcels).
/// </summary>
public sealed class LostFoundConfig
{
    public bool Enabled { get; set; } = true;
    public ulong[] AllowedGuilds { get; set; } = Array.Empty<ulong>();
    public string TimeZone { get; set; } = "Australia/Sydney";

    public int DailyExpeditions { get; set; } = 1;
    public int EncounterChancePercent { get; set; } = 30;
    public int MaxRecentDiscoveries { get; set; } = 8;

    public LevelCurve XpCurve { get; set; } = new();
    public RelicRequirements Relic { get; set; } = new();
    public DropSettings Drops { get; set; } = new();
    public QuestSettings Questing { get; set; } = new();
    public int SecretAreaCompletedSets { get; set; } = 5;
    public int SetCompletionScoreBonus { get; set; } = 40;
    public int SecretScoreBonus { get; set; } = 25;

    public Dictionary<string, double> RarityWeights { get; set; } = DefaultContent.DefaultRarityWeights();
    public Dictionary<string, int> XpByRarity { get; set; } = DefaultContent.DefaultXpByRarity();
    public Dictionary<string, int> TokensByRarity { get; set; } = DefaultContent.DefaultTokensByRarity();
    public Dictionary<string, int> CollectionValueByRarity { get; set; } = DefaultContent.DefaultCollectionByRarity();
    public Dictionary<string, int> ScrapValueByRarity { get; set; } = DefaultContent.DefaultScrapByRarity();

    public Dictionary<string, CharmDef> Charms { get; set; } = DefaultContent.DefaultCharms();

    public List<ItemDef> Items { get; set; } = DefaultContent.Items();
    public List<SetDef> Sets { get; set; } = DefaultContent.Sets();
    public List<AreaDef> Areas { get; set; } = DefaultContent.Areas();
    public List<RecipeDef> Recipes { get; set; } = DefaultContent.Recipes();
    public List<EncounterDef> Encounters { get; set; } = DefaultContent.Encounters();
    public List<QuestDef> Quests { get; set; } = DefaultContent.Quests();
    public List<ParcelDrop> Parcels { get; set; } = DefaultContent.Parcels();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static LostFoundConfig Load(string assemblyDir)
    {
        var path = Path.Combine(assemblyDir, "config.json");
        LostFoundConfig cfg;
        try
        {
            if (File.Exists(path))
                cfg = JsonSerializer.Deserialize<LostFoundConfig>(File.ReadAllText(path), JsonOpts)
                      ?? new LostFoundConfig();
            else
                cfg = new LostFoundConfig();
        }
        catch
        {
            cfg = new LostFoundConfig();
        }

        cfg.Normalize();
        return cfg;
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOpts);

    public void Normalize()
    {
        Items ??= DefaultContent.Items();
        Sets ??= DefaultContent.Sets();
        Areas ??= DefaultContent.Areas();
        Recipes ??= DefaultContent.Recipes();
        Encounters ??= DefaultContent.Encounters();
        Quests ??= DefaultContent.Quests();
        Parcels ??= DefaultContent.Parcels();
        Charms ??= DefaultContent.DefaultCharms();
        XpCurve ??= new LevelCurve();
        Relic ??= new RelicRequirements();
        Drops ??= new DropSettings();
        Questing ??= new QuestSettings();

        if (Items.Count == 0) Items = DefaultContent.Items();
        if (Sets.Count == 0) Sets = DefaultContent.Sets();
        if (Areas.Count == 0) Areas = DefaultContent.Areas();
        if (Quests.Count == 0) Quests = DefaultContent.Quests();
        if (Recipes.Count == 0) Recipes = DefaultContent.Recipes();
        if (Encounters.Count == 0) Encounters = DefaultContent.Encounters();
        if (Parcels.Count == 0) Parcels = DefaultContent.Parcels();
        if (XpByRarity is null || XpByRarity.Count == 0) XpByRarity = DefaultContent.DefaultXpByRarity();
        if (TokensByRarity is null || TokensByRarity.Count == 0) TokensByRarity = DefaultContent.DefaultTokensByRarity();
        if (CollectionValueByRarity is null || CollectionValueByRarity.Count == 0) CollectionValueByRarity = DefaultContent.DefaultCollectionByRarity();
        if (ScrapValueByRarity is null || ScrapValueByRarity.Count == 0) ScrapValueByRarity = DefaultContent.DefaultScrapByRarity();
        if (RarityWeights is null || RarityWeights.Count == 0) RarityWeights = DefaultContent.DefaultRarityWeights();
    }

    public int XpFor(Rarity r) => Lookup(XpByRarity, r, DefaultContent.DefaultXpByRarity());
    public int TokensFor(Rarity r) => Lookup(TokensByRarity, r, DefaultContent.DefaultTokensByRarity());
    public int CollectionValueFor(Rarity r) => Lookup(CollectionValueByRarity, r, DefaultContent.DefaultCollectionByRarity());
    public int ScrapFor(Rarity r) => Lookup(ScrapValueByRarity, r, DefaultContent.DefaultScrapByRarity());

    private static int Lookup(Dictionary<string, int> map, Rarity r, Dictionary<string, int> fallback)
    {
        if (map != null)
        {
            if (map.TryGetValue(RarityUtil.Label(r), out var v)) return v;
            if (map.TryGetValue(r.ToString(), out v)) return v;
        }
        foreach (var kv in fallback)
            if (RarityUtil.TryParse(kv.Key, out var rr) && rr == r)
                return kv.Value;
        return 0;
    }

    /// <summary>Rarity weights for an area, falling back to the global table.</summary>
    public Dictionary<Rarity, double> WeightsFor(AreaDef area)
    {
        var src = (area?.RarityWeights is { Count: > 0 }) ? area.RarityWeights : RarityWeights;
        var result = new Dictionary<Rarity, double>();
        foreach (var kv in src)
            if (RarityUtil.TryParse(kv.Key, out var r))
                result[r] = kv.Value;
        if (result.Count == 0)
            foreach (var kv in DefaultContent.DefaultRarityWeights())
                if (RarityUtil.TryParse(kv.Key, out var r))
                    result[r] = kv.Value;
        return result;
    }
}

/// <summary>Embedded default content. Config.json can override any of it.</summary>
public static class DefaultContent
{
    private static ItemDef I(
        string id, string name, string desc, Rarity r, string set,
        string areas = "", string effect = "", bool secret = false, bool tradable = true,
        bool recyclable = true, string image = "", bool craftable = false)
        => new()
        {
            Id = id, Name = name, Description = desc, Rarity = r, Set = set,
            Areas = string.IsNullOrEmpty(areas) ? Array.Empty<string>() : areas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Effect = effect, Secret = secret, Tradable = tradable, Recyclable = recyclable,
            Image = image, Craftable = craftable, ReleaseRelic = r == Rarity.SarahRelic && set == "relics"
        };

    public static Dictionary<string, double> DefaultRarityWeights() => new()
    {
        ["Common"] = 55,
        ["Uncommon"] = 24,
        ["Rare"] = 13,
        ["Epic"] = 6,
        ["Legendary"] = 2
    };

    public static Dictionary<string, int> DefaultXpByRarity() => new()
    {
        ["Common"] = 8, ["Uncommon"] = 14, ["Rare"] = 25,
        ["Epic"] = 45, ["Legendary"] = 90, ["SarahRelic"] = 200
    };

    public static Dictionary<string, int> DefaultTokensByRarity() => new()
    {
        ["Common"] = 1, ["Uncommon"] = 2, ["Rare"] = 4,
        ["Epic"] = 8, ["Legendary"] = 20, ["SarahRelic"] = 50
    };

    public static Dictionary<string, int> DefaultCollectionByRarity() => new()
    {
        ["Common"] = 1, ["Uncommon"] = 2, ["Rare"] = 5,
        ["Epic"] = 10, ["Legendary"] = 25, ["SarahRelic"] = 100
    };

    public static Dictionary<string, int> DefaultScrapByRarity() => new()
    {
        ["Common"] = 1, ["Uncommon"] = 2, ["Rare"] = 5,
        ["Epic"] = 10, ["Legendary"] = 25, ["SarahRelic"] = 50
    };

    public static Dictionary<string, CharmDef> DefaultCharms() => new()
    {
        ["studio_capo"] = new CharmDef { Effect = "music", Description = "+4% weight to music items while exploring.", BonusChance = 0.04 },
        ["midnight_rose"] = new CharmDef { Effect = "bonus", Description = "Small chance of a bonus Garden item.", BonusSets = new[] { "garden_finds" }, BonusChance = 0.12 },
        ["head_torch"] = new CharmDef { Effect = "bonus", Description = "Small chance of a bonus Night/Backstage item.", BonusSets = new[] { "night_shift", "backstage" }, BonusChance = 0.10 },
        ["pixel_keyring"] = new CharmDef { Effect = "bonus", Description = "Small chance of a bonus Arcade item.", BonusSets = new[] { "arcade_pocket" }, BonusChance = 0.12 },
        ["golden_tennis_ball"] = new CharmDef { Effect = "bonus", Description = "Small chance of a bonus Remy's Things item.", BonusSets = new[] { "remys_things" }, BonusChance = 0.12 }
    };

    public static List<ItemDef> Items() => new()
    {
        // --- Remy's Things ---
        I("remy_old_collar", "Remy's Old Collar", "Chewed soft. Smells faintly of the beach.", Rarity.Common, "remys_things", "bedroom,beach", image: "bandana.png"),
        I("squeaky_ball", "Squeaky Ball", "The squeak is mostly a wheeze now.", Rarity.Common, "remys_things", "bedroom,beach", image: "rope-toy.png"),
        I("buried_bone", "Buried Bone", "Someone's savings. Returned, reluctantly.", Rarity.Uncommon, "remys_things", "bedroom,beach", image: "acorn.png"),
        I("dented_name_tag", "Dented Name Tag", "REMY. The phone number is long gone.", Rarity.Uncommon, "remys_things", "bedroom,beach", image: "paw-coin.png"),
        I("retractable_lead", "Retractable Lead", "Retracts. Occasionally. Usually just dangles.", Rarity.Rare, "remys_things", "bedroom,beach", image: "rope-toy.png"),
        I("favourite_blanket", "Favourite Blanket", "One corner is entirely missing. No questions.", Rarity.Epic, "remys_things", "bedroom,beach", image: "bandana.png"),
        I("golden_tennis_ball", "Golden Tennis Ball", "Remy's greatest treasure. Behind the couch for years.", Rarity.Legendary, "remys_things", "bedroom,beach", image: "paw-coin.png"),

        // --- Studio Junk ---
        I("stray_pick", "Stray Guitar Pick", "Half the paint worn off. Still plays fine.", Rarity.Common, "studio_junk", "studio,backstage", image: "guitar-pick.png"),
        I("studio_cable", "Tangled Cable", "You will untangle it. You will not enjoy it.", Rarity.Common, "studio_junk", "studio,backstage", image: "thread-spool.png"),
        I("napkin_lyrics", "Napkin Lyrics", "Two good lines and a coffee ring.", Rarity.Uncommon, "studio_junk", "studio,backstage", image: "notepad.png"),
        I("studio_capo", "Well-Loved Capo", "Spring is going, but the song isn't.", Rarity.Uncommon, "studio_junk", "studio,backstage", effect: "music", image: "guitar-pick.png"),
        I("studio_metronome", "Sticky Metronome", "Ticks slightly behind. Like everyone.", Rarity.Rare, "studio_junk", "studio,backstage", image: "synth.png"),
        I("backup_microphone", "Backup Microphone", "Kept in a sock, for some reason.", Rarity.Epic, "studio_junk", "studio,backstage", image: "synth.png"),
        I("master_reel", "Master Reel", "One take. You can hear the room.", Rarity.Legendary, "studio_junk", "studio,backstage", image: "cassette-tape.png"),

        // --- Night Shift ---
        I("work_thermos", "Work Thermos", "Coffee stains older than some friendships.", Rarity.Common, "night_shift", "studio,arcade", image: "coffee-mug.png"),
        I("shift_notebook", "Shift Notebook", "Lists, doodles, one very good idea.", Rarity.Common, "night_shift", "studio,arcade", image: "moon-notebook.png"),
        I("faded_hoodie", "Faded Hoodie", "Soft in all the right places.", Rarity.Uncommon, "night_shift", "studio,arcade", image: "bandana.png"),
        I("empty_can", "Empty Energy Can", "It did nothing. You drank three.", Rarity.Uncommon, "night_shift", "studio,arcade", image: "coffee-mug.png"),
        I("last_light_sticker", "Last Light Sticker", "Peeled from a lamp post at 4am.", Rarity.Rare, "night_shift", "studio,arcade", image: "sticker-sheet.png"),
        I("head_torch", "Head Torch", "For finding things. And scaring people.", Rarity.Epic, "night_shift", "studio,arcade", effect: "backstage", image: "lantern.png"),
        I("star_chart", "Hand-Drawn Star Chart", "Wrong, probably. Beautiful, definitely.", Rarity.Legendary, "night_shift", "studio,arcade", image: "moon-charm.png"),

        // --- Coastal Days ---
        I("beach_shell", "Pale Beach Shell", "Held to the ear: just the sea, and traffic.", Rarity.Common, "coastal_days", "beach", image: "ceramic-fish.png"),
        I("bus_ticket", "Creased Bus Ticket", "Wollongong to home. Return unused.", Rarity.Common, "coastal_days", "beach", image: "old-ticket.png"),
        I("mirrored_sunnies", "Scratched Sunnies", "The world looks better this way.", Rarity.Uncommon, "coastal_days", "beach", image: "camera.png"),
        I("salt_towel", "Salt-Stiff Towel", "It never really dries at the beach.", Rarity.Uncommon, "coastal_days", "beach", image: "bandana.png"),
        I("dinged_surfboard", "Dinged Surfboard", "More ding than board. Still floats. Mostly.", Rarity.Rare, "coastal_days", "beach", image: "vinyl-record.png"),
        I("lighthouse_postcard", "Signed Lighthouse Postcard", "Wish you were here. Handwriting is a scrawl.", Rarity.Epic, "coastal_days", "beach", image: "postcard.png"),
        I("driftwood_heart", "Driftwood Heart", "Not carved. The sea did that. Probably.", Rarity.Legendary, "coastal_days", "beach", image: "maple-leaf.png"),

        // --- Arcade Pocket ---
        I("arcade_token", "Arcade Token", "Brass-warm. Good for exactly one go.", Rarity.Common, "arcade_pocket", "arcade", image: "paw-coin.png"),
        I("sour_candy", "Sour Candy", "Stuck to the wrapper since 2009.", Rarity.Common, "arcade_pocket", "arcade", image: "sticker-sheet.png"),
        I("arcade_card", "Arcade Member Card", "Ranked somewhere between novice and legend.", Rarity.Uncommon, "arcade_pocket", "arcade", image: "old-ticket.png"),
        I("pixel_keyring", "Pixel Keyring", "A tiny 8-bit something. Hard to tell what.", Rarity.Uncommon, "arcade_pocket", "arcade", effect: "bonus", image: "keychain-moon.png"),
        I("loose_joystick", "Loose Joystick", "You have to really mean it to move.", Rarity.Rare, "arcade_pocket", "arcade", image: "cassette-player.png"),
        I("highscore_printout", "High Score Printout", "Your initials. Someone else's record.", Rarity.Epic, "arcade_pocket", "arcade", image: "notepad.png"),
        I("mini_cabinet", "Miniature Cabinet", "It works. The screen is the size of a stamp.", Rarity.Legendary, "arcade_pocket", "arcade", image: "synth.png"),

        // --- Garden Finds ---
        I("garden_pebble", "Garden Pebble", "Perfectly round. Keep it. Or don't.", Rarity.Common, "garden_finds", "garden", image: "acorn.png"),
        I("mystery_seed", "Mystery Seed", "Something will grow. Eventually. Or not.", Rarity.Common, "garden_finds", "garden", image: "acorn.png"),
        I("brass_trowel", "Brass Trowel", "Heavy, serious, faintly military.", Rarity.Uncommon, "garden_finds", "garden", image: "lantern.png"),
        I("cracked_pot", "Cracked Terracotta Pot", "The plant moved on. The pot stayed.", Rarity.Uncommon, "garden_finds", "garden", image: "houseplant.png"),
        I("flower_press", "Flower Press", "Kept a whole summer flat between the pages.", Rarity.Rare, "garden_finds", "garden", image: "journal-flowers.png"),
        I("weathered_gnome", "Weathered Gnome", "He has seen things. He says nothing.", Rarity.Epic, "garden_finds", "garden", image: "moth.png"),
        I("midnight_rose", "Midnight Rose", "Only opens when no one is watching.", Rarity.Legendary, "garden_finds", "garden", effect: "bonus", image: "journal-flowers.png"),

        // --- Backstage ---
        I("backstage_pass", "Crew Pass", "Laminated. Almost official.", Rarity.Common, "backstage", "backstage", image: "camera.png"),
        I("setlist_sheet", "Setlist Sheet", "Third song crossed out. Long story.", Rarity.Common, "backstage", "backstage", image: "notepad.png"),
        I("gaffer_tape", "Roll of Gaffer Tape", "Holds the whole show together. Literally.", Rarity.Uncommon, "backstage", "backstage", image: "thread-spool.png"),
        I("crew_lanyard", "Crew Lanyard", "Tired elastic. Good memories.", Rarity.Uncommon, "backstage", "backstage", image: "thread-spool.png"),
        I("used_strings", "Used Guitar Strings", "Coiled like something alive.", Rarity.Rare, "backstage", "backstage", image: "thread-spool.png"),
        I("polaroid", "Blurry Polaroid", "Nobody is looking at the camera.", Rarity.Epic, "backstage", "backstage", image: "polaroid.png"),
        I("backstage_key", "Backstage Key", "Do not lose this one.", Rarity.Legendary, "backstage", "backstage", effect: "backstage", image: "keychain-moon.png"),

        // --- Cosy Home ---
        I("chipped_mug", "Chipped Mug", "The good mug. You know the one.", Rarity.Common, "cosy_home", "bedroom,garden", image: "coffee-mug.png"),
        I("half_candle", "Half-Melted Candle", "Smells like the end of a long day.", Rarity.Common, "cosy_home", "bedroom,garden", image: "lantern.png"),
        I("odd_sock", "Odd Sock", "The other one is somewhere. Probably.", Rarity.Uncommon, "cosy_home", "bedroom,garden", image: "moth.png"),
        I("old_teapot", "Old Teapot", "Pours slightly to the left. Adds character.", Rarity.Uncommon, "cosy_home", "bedroom,garden", image: "ceramic-fish.png"),
        I("sagging_pillow", "Sagging Pillow", "It has your shape. That's a bit much.", Rarity.Rare, "cosy_home", "bedroom,garden", image: "bandana.png"),
        I("portable_record_player", "Portable Record Player", "Crackly, warm, perfect.", Rarity.Epic, "cosy_home", "bedroom,garden", image: "cassette-player.png"),
        I("whistling_kettle", "Whistling Kettle", "Still whistles. Still ignored.", Rarity.Legendary, "cosy_home", "bedroom,garden", image: "ceramic-fish.png"),

        // --- Badges (set rewards, hidden, not tradable/recyclable) ---
        I("badge_remys_friend", "Remy's Friend Badge", "Remy trusts you. That's rare.", Rarity.Rare, "badges", secret: true, tradable: false, recyclable: false, image: "paw-coin.png"),
        I("badge_studio_hand", "Studio Hand Badge", "You know where the cables go.", Rarity.Rare, "badges", secret: true, tradable: false, recyclable: false, image: "guitar-pick.png"),
        I("badge_night_shift", "Night Shift Badge", "Still awake. Still here.", Rarity.Rare, "badges", secret: true, tradable: false, recyclable: false, image: "lantern.png"),
        I("badge_coastal", "Coastal Badge", "Sand in everything, and worth it.", Rarity.Rare, "badges", secret: true, tradable: false, recyclable: false, image: "postcard.png"),
        I("badge_high_scorer", "High Scorer Badge", "Your initials are up there.", Rarity.Rare, "badges", secret: true, tradable: false, recyclable: false, image: "pink-cassette.png"),
        I("badge_garden", "Green Thumb Badge", "Things grow for you.", Rarity.Rare, "badges", secret: true, tradable: false, recyclable: false, image: "acorn.png"),
        I("badge_backstage", "Past the Rope Badge", "You got in.", Rarity.Rare, "badges", secret: true, tradable: false, recyclable: false, image: "keychain-moon.png"),
        I("badge_homebody", "Homebody Badge", "Nothing wrong with staying in.", Rarity.Rare, "badges", secret: true, tradable: false, recyclable: false, image: "coffee-mug.png"),
        I("badge_relic_keeper", "Keeper of Relics Badge", "You kept all seven. Remarkable.", Rarity.Legendary, "badges", secret: true, tradable: false, recyclable: false, image: "moon-charm.png"),

        // --- Sarah Relics (7). Art must be the exact release pins. ---
        I("relic_yibc", "Yes, I've Been Crying", "A relic. It is what it says it is.", Rarity.SarahRelic, "relics", secret: true, tradable: false, recyclable: false, image: "yes-ive-been-crying-round.png"),
        I("relic_tainted", "Tainted Timeline", "Some timelines shouldn't be kept. This one was.", Rarity.SarahRelic, "relics", secret: true, tradable: false, recyclable: false, image: "tainted-timeline-round.png"),
        I("relic_absence", "Absence", "The space where something used to be.", Rarity.SarahRelic, "relics", secret: true, tradable: false, recyclable: false, image: "absence-round.png"),
        I("relic_september", "September", "A whole month, pressed flat.", Rarity.SarahRelic, "relics", secret: true, tradable: false, recyclable: false, image: "september-round.png"),
        I("relic_apparently", "Apparently", "Apparently this was important. It is.", Rarity.SarahRelic, "relics", secret: true, tradable: false, recyclable: false, image: "apparently-round.png"),
        I("relic_frame", "Frame", "Look through it and everything is a little clearer.", Rarity.SarahRelic, "relics", secret: true, tradable: false, recyclable: false, image: "frame-round.png"),
        I("relic_wollongong", "Wollongong Road", "The long way home, kept forever.", Rarity.SarahRelic, "relics", secret: true, tradable: false, recyclable: false, image: "wollongong-road-round.png")
    };

    public static List<SetDef> Sets() => new()
    {
        new() { Id = "remys_things", Name = "Remy's Things", Description = "Odds and ends a dog has dragged in.", Reward = new SetReward { Xp = 120, Tokens = 20, Title = "Remy's Friend", Badge = "badge_remys_friend" } },
        new() { Id = "studio_junk", Name = "Studio Junk", Description = "Left around the recording space.", Reward = new SetReward { Xp = 120, Tokens = 20, Title = "Studio Hand", Badge = "badge_studio_hand" } },
        new() { Id = "night_shift", Name = "Night Shift", Description = "For the 3am crowd.", Reward = new SetReward { Xp = 120, Tokens = 20, Title = "Night Shift", Badge = "badge_night_shift" } },
        new() { Id = "coastal_days", Name = "Coastal Days", Description = "Salt, sand and long drives.", Reward = new SetReward { Xp = 150, Tokens = 25, Title = "Coastal Days", Badge = "badge_coastal" } },
        new() { Id = "arcade_pocket", Name = "Arcade Pocket", Description = "Tokens, tickets and high scores.", Reward = new SetReward { Xp = 150, Tokens = 25, Title = "High Scorer", Badge = "badge_high_scorer" } },
        new() { Id = "garden_finds", Name = "Garden Finds", Description = "Grown, dropped, or buried.", Reward = new SetReward { Xp = 120, Tokens = 20, Title = "Green Thumb", Badge = "badge_garden" } },
        new() { Id = "backstage", Name = "Backstage", Description = "Past the rope.", Reward = new SetReward { Xp = 200, Tokens = 30, Title = "Past the Rope", Badge = "badge_backstage" } },
        new() { Id = "cosy_home", Name = "Cosy Home", Description = "Soft, warm, lived-in.", Reward = new SetReward { Xp = 120, Tokens = 20, Title = "Homebody", Badge = "badge_homebody" } },
        new() { Id = "badges", Name = "Set Badges", Description = "Proof you finished something.", Hidden = true },
        new() { Id = "relics", Name = "Sarah Relics", Description = "Seven things best kept safe.", Hidden = true, Reward = new SetReward { Xp = 500, Tokens = 100, Title = "Keeper of Relics", Badge = "badge_relic_keeper" } }
    };

    public static List<AreaDef> Areas() => new()
    {
        new() { Id = "bedroom", Name = "Bedroom", UnlockLevel = 1, Description = "Somewhere to start.", Flavour = "Under the bed and behind the door.", Sets = new[] { "remys_things", "cosy_home" }, Encounters = new[] { "bushes", "wet_parcel", "glint" } },
        new() { Id = "garden", Name = "Garden", UnlockLevel = 3, Description = "Damp grass and long shadows.", Flavour = "Something has been buried here. Recently.", Sets = new[] { "garden_finds", "cosy_home" }, Encounters = new[] { "bushes", "cat_guard", "door_note" } },
        new() { Id = "studio", Name = "Studio", UnlockLevel = 5, Description = "Cables everywhere. Always.", Flavour = "The room hums even when it's empty.", Sets = new[] { "studio_junk", "night_shift" }, BonusSets = new[] { "studio_junk" }, BonusSetWeight = 1.15, Encounters = new[] { "glint", "door_note", "jammed_machine" } },
        new() { Id = "arcade", Name = "Arcade", UnlockLevel = 8, Description = "Beeps, sticky carpet, one working machine.", Flavour = "You have exactly enough for one more go.", Sets = new[] { "arcade_pocket", "night_shift" }, BonusSets = new[] { "arcade_pocket" }, BonusSetWeight = 1.15, Encounters = new[] { "jammed_machine", "stranger_wave", "glint" } },
        new() { Id = "beach", Name = "Beach", UnlockLevel = 12, Description = "Salt in the air and sand in everything.", Flavour = "The tide has been busy.", Sets = new[] { "coastal_days", "remys_things" }, Encounters = new[] { "wet_parcel", "stranger_wave", "glint" } },
        new() { Id = "backstage", Name = "Backstage", UnlockLevel = 20, Description = "Past the rope, past the noise.", Flavour = "Everything smells of tape and sawdust.", Sets = new[] { "backstage", "studio_junk" }, BonusSets = new[] { "backstage" }, BonusSetWeight = 1.15, Encounters = new[] { "remy_digging", "stranger_wave", "door_note" } },
        new() { Id = "unknown", Name = "???", UnlockLevel = 99, Secret = true, Description = "You are not sure how you got here.", Flavour = "The air is different.", Sets = new[] { "remys_things", "studio_junk", "night_shift", "coastal_days", "arcade_pocket", "garden_finds", "backstage", "cosy_home" }, Encounters = new[] { "glint", "door_note", "remy_digging" } }
    };

    public static List<RecipeDef> Recipes() => new()
    {
        new() { Id = "warm_drink", Name = "Warm Drink Kit", Description = "Thermos plus mug equals something better.", Inputs = new() { ["work_thermos"] = 1, ["chipped_mug"] = 1 }, Output = "old_teapot" },
        new() { Id = "restored_ball", Name = "Restored Ball", Description = "Two tired balls and a sock make one good bone.", Inputs = new() { ["squeaky_ball"] = 2, ["odd_sock"] = 1 }, Output = "buried_bone" },
        new() { Id = "mixed_tape", Name = "Mixed Tape", Description = "Cable and tape, and you get the words back.", Inputs = new() { ["studio_cable"] = 1, ["gaffer_tape"] = 1 }, Output = "napkin_lyrics" },
        new() { Id = "garden_bed", Name = "Garden Bed", Description = "Seed, stone and a broken pot that still works.", Inputs = new() { ["mystery_seed"] = 2, ["garden_pebble"] = 1, ["cracked_pot"] = 1 }, Output = "flower_press" },
        new() { Id = "stage_kit", Name = "Stage Kit", Description = "Pass and lanyard get you the setlist.", Inputs = new() { ["backstage_pass"] = 1, ["crew_lanyard"] = 1 }, Output = "setlist_sheet" },
        new() { Id = "arcade_starter", Name = "Arcade Starter Pack", Description = "Tokens and sweets for a pixel keyring.", Inputs = new() { ["arcade_token"] = 5, ["sour_candy"] = 2 }, Output = "pixel_keyring" },
        new() { Id = "coastal_kit", Name = "Coastal Kit", Description = "Shell and towel make a pair of sunnies.", Inputs = new() { ["beach_shell"] = 2, ["salt_towel"] = 1 }, Output = "mirrored_sunnies" },
        new() { Id = "cosy_evening", Name = "Cosy Evening", Description = "Candle, sock, mug: a whole evening in.", Inputs = new() { ["half_candle"] = 1, ["odd_sock"] = 1, ["chipped_mug"] = 1 }, Output = "sagging_pillow" }
    };

    public static List<EncounterDef> Encounters() => new()
    {
        new()
        {
            Id = "bushes", Title = "Something in the Bushes",
            Text = "The leaves move. Too big for a bird. Too quiet for a person.",
            Options = new()
            {
                new() { Id = "investigate", Label = "Investigate", Result = "You push in and find something caught in the branches.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.6, Rarity = "Uncommon", Text = "It comes loose easily." }, new() { Type = "xp", Chance = 0.25, Xp = 15, Text = "Just a fox. The walk was worth it." }, new() { Type = "nothing", Chance = 0.15, Text = "Nothing. A fox, already gone." } } },
                new() { Id = "ignore", Label = "Ignore it", Result = "You leave it be.",
                    Outcomes = new() { new() { Type = "nothing", Chance = 0.8, Text = "You walk on. Sensible." }, new() { Type = "xp", Chance = 0.2, Xp = 6, Text = "Sometimes restraint is its own reward." } } },
                new() { Id = "remy", Label = "Send Remy", Result = "Remy is in there before you finish the thought.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.45, Rarity = "Rare", Text = "He trots out, very pleased." }, new() { Type = "scraps", Chance = 0.35, Scraps = 4, Text = "He brings back something you can at least break down." }, new() { Type = "nothing", Chance = 0.2, Text = "He returns covered in mud and nothing else." } } }
            }
        },
        new()
        {
            Id = "wet_parcel", Title = "A Wet Parcel",
            Text = "A package, sodden, sitting where the water pools.",
            Options = new()
            {
                new() { Id = "investigate", Label = "Open it", Result = "The tape gives way with a wet sound.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.7, Rarity = "Common", Text = "Dry inside, somehow." }, new() { Type = "tokens", Chance = 0.3, Tokens = 5, Text = "A few tokens, wrapped in plastic." } } },
                new() { Id = "ignore", Label = "Leave it", Result = "It isn't yours. Probably.",
                    Outcomes = new() { new() { Type = "nothing", Chance = 1.0, Text = "You leave it to the weather." } } },
                new() { Id = "remy", Label = "Let Remy handle it", Result = "Remy has opinions about parcels.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.5, Rarity = "Uncommon", Text = "He brings it over, tail going." }, new() { Type = "nothing", Chance = 0.5, Text = "He eats the corner and loses interest." } } }
            }
        },
        new()
        {
            Id = "cat_guard", Title = "The Cat Won't Move",
            Text = "A cat is sitting on something. It has no intention of moving.",
            Options = new()
            {
                new() { Id = "investigate", Label = "Negotiate", Result = "You attempt diplomacy.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.4, Rarity = "Uncommon", Text = "It relents. Briefly." }, new() { Type = "xp", Chance = 0.3, Xp = 12, Text = "You lose. Gracefully." }, new() { Type = "nothing", Chance = 0.3, Text = "It stares until you leave." } } },
                new() { Id = "ignore", Label = "Leave it", Result = "You know better.",
                    Outcomes = new() { new() { Type = "nothing", Chance = 1.0, Text = "The cat keeps the thing. The cat always keeps the thing." } } },
                new() { Id = "remy", Label = "Send Remy", Result = "Remy and the cat have history.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.55, Rarity = "Rare", Text = "A brief standoff. Remy wins this one." }, new() { Type = "nothing", Chance = 0.45, Text = "Remy backs down immediately. Betrayal." } } }
            }
        },
        new()
        {
            Id = "glint", Title = "Something Glinting",
            Text = "A point of light, low down, catching the corner of your eye.",
            Options = new()
            {
                new() { Id = "investigate", Label = "Pick it up", Result = "You crouch down.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.65, Rarity = "Uncommon", Text = "Small, but worth keeping." }, new() { Type = "tokens", Chance = 0.2, Tokens = 6, Text = "Money, of a sort." }, new() { Type = "nothing", Chance = 0.15, Text = "Just glass. Always just glass." } } },
                new() { Id = "ignore", Label = "Ignore it", Result = "You keep walking.",
                    Outcomes = new() { new() { Type = "nothing", Chance = 1.0, Text = "It'll be there tomorrow. Or it won't." } } },
                new() { Id = "remy", Label = "Point it out to Remy", Result = "Remy investigates with his nose.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.5, Rarity = "Rare", Text = "He finds it and presents it proudly." }, new() { Type = "scraps", Chance = 0.3, Scraps = 3, Text = "Something small, easily broken down." }, new() { Type = "nothing", Chance = 0.2, Text = "He finds a chip. Eats it." } } }
            }
        },
        new()
        {
            Id = "door_note", Title = "A Note Under the Door",
            Text = "Neat handwriting. No name.",
            Options = new()
            {
                new() { Id = "investigate", Label = "Read it", Result = "It's directions, mostly.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.45, Rarity = "Uncommon", Text = "They lead somewhere. To something." }, new() { Type = "xp", Chance = 0.35, Xp = 18, Text = "A good clue is worth something." }, new() { Type = "nothing", Chance = 0.2, Text = "You don't understand it. Yet." } } },
                new() { Id = "ignore", Label = "Put it back", Result = "Not your door. Not your note.",
                    Outcomes = new() { new() { Type = "nothing", Chance = 1.0, Text = "It stays there, folded." } } },
                new() { Id = "remy", Label = "Have Remy sniff it", Result = "Remy gives it a good going-over.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.4, Rarity = "Rare", Text = "He knows exactly where this leads." }, new() { Type = "xp", Chance = 0.3, Xp = 20, Text = "He's off. You follow." }, new() { Type = "nothing", Chance = 0.3, Text = "He sneezes. Case closed." } } }
            }
        },
        new()
        {
            Id = "stranger_wave", Title = "Someone Waves You Over",
            Text = "A stranger, waving. They seem to think they know you.",
            Options = new()
            {
                new() { Id = "investigate", Label = "Go over", Result = "You walk across.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.4, Rarity = "Uncommon", Text = "They hand you something. 'This is yours, I think.'" }, new() { Type = "tokens", Chance = 0.3, Tokens = 10, Text = "They settle a very old debt." }, new() { Type = "nothing", Chance = 0.3, Text = "Wrong person. They apologise." } } },
                new() { Id = "ignore", Label = "Wave back, keep going", Result = "A polite wave and nothing more.",
                    Outcomes = new() { new() { Type = "nothing", Chance = 0.85, Text = "You'll never know." }, new() { Type = "xp", Chance = 0.15, Xp = 5, Text = "Some mysteries stay mysteries." } } },
                new() { Id = "remy", Label = "Send Remy", Result = "Remy trots over to make friends.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.5, Rarity = "Rare", Text = "They give Remy something. Remy gives it to you." }, new() { Type = "effect", Chance = 0.3, Effect = "music", EffectValue = 0.06, EffectMinutes = 60, Text = "They hum something good. You'll have it stuck in your head." }, new() { Type = "nothing", Chance = 0.2, Text = "They chat. Remy listens. Nothing comes of it." } } }
            }
        },
        new()
        {
            Id = "remy_digging", Title = "Remy Is Digging Again",
            Text = "A pile of dirt is growing. Remy is delighted.",
            Options = new()
            {
                new() { Id = "investigate", Label = "Dig with him", Result = "You get on your knees.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.6, Rarity = "Uncommon", Text = "Six inches down, something solid." }, new() { Type = "scraps", Chance = 0.2, Scraps = 4, Text = "Bits and pieces. He doesn't mind." }, new() { Type = "nothing", Chance = 0.2, Text = "Just more dirt. And a worm." } } },
                new() { Id = "ignore", Label = "Leave him to it", Result = "He'll tire himself out.",
                    Outcomes = new() { new() { Type = "nothing", Chance = 0.8, Text = "He doesn't." }, new() { Type = "xp", Chance = 0.2, Xp = 8, Text = "His commitment is, at least, educational." } } },
                new() { Id = "remy", Label = "Ask Remy for it", Result = "You hold out a hand.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.55, Rarity = "Rare", Text = "He drops it in your palm, dripping." }, new() { Type = "tokens", Chance = 0.2, Tokens = 8, Text = "A coin, soil-encrusted but real." }, new() { Type = "nothing", Chance = 0.25, Text = "He buries it deeper. On purpose." } } }
            }
        },
        new()
        {
            Id = "jammed_machine", Title = "The Machine Is Jammed",
            Text = "The machine has eaten something and is not giving it up.",
            Options = new()
            {
                new() { Id = "investigate", Label = "Shake it", Result = "You shake the machine. Firmly.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.55, Rarity = "Common", Text = "It falls into the tray." }, new() { Type = "nothing", Chance = 0.3, Text = "The machine wins." }, new() { Type = "xp", Chance = 0.15, Xp = 10, Text = "A small crowd gathers to watch you fail." } } },
                new() { Id = "ignore", Label = "Leave it", Result = "Someone else's problem now.",
                    Outcomes = new() { new() { Type = "nothing", Chance = 1.0, Text = "You walk away. The machine hums on." } } },
                new() { Id = "remy", Label = "Send Remy for it", Result = "Remy has a technique.",
                    Outcomes = new() { new() { Type = "item", Chance = 0.5, Rarity = "Rare", Text = "He paws it free, somehow." }, new() { Type = "tokens", Chance = 0.25, Tokens = 12, Text = "He also knocks loose the coin tray." }, new() { Type = "nothing", Chance = 0.25, Text = "He gets his nose stuck. Briefly." } } }
            }
        }
    };

    public static List<QuestDef> Quests() => new()
    {
        new() { Id = "q_find", Name = "Lost Property", Description = "Find 5 items while exploring.", Type = "find", Amount = 5, Reward = new QuestReward { Xp = 80, Tokens = 10 } },
        new() { Id = "q_rare", Name = "Something Better", Description = "Find 1 item of Rare quality or above.", Type = "find_rarity", Target = "Rare", Amount = 1, Reward = new QuestReward { Xp = 120, Tokens = 15 } },
        new() { Id = "q_fish", Name = "Gone Fishing", Description = "Catch 3 fish with .fish.", Type = "fish", Amount = 3, Reward = new QuestReward { Xp = 90, Tokens = 12 } },
        new() { Id = "q_trivia", Name = "Quiz Night", Description = "Play 2 rounds of trivia.", Type = "trivia", Amount = 2, Reward = new QuestReward { Xp = 70, Tokens = 10 } },
        new() { Id = "q_claim", Name = "Quick Hands", Description = "Claim 2 random drops.", Type = "claim_drop", Amount = 2, Reward = new QuestReward { Xp = 100, Tokens = 12 } },
        new() { Id = "q_expedition", Name = "Out and About", Description = "Complete 3 expeditions.", Type = "expedition", Amount = 3, Reward = new QuestReward { Xp = 110, Tokens = 12 } },
        new() { Id = "q_gift", Name = "Pass It On", Description = "Gift an item to someone.", Type = "gift", Amount = 1, Reward = new QuestReward { Xp = 80, Tokens = 10, Scraps = 5 } },
        new() { Id = "q_craft", Name = "Make Do", Description = "Craft any recipe.", Type = "craft", Amount = 1, Reward = new QuestReward { Xp = 90, Tokens = 10 } }
    };

    public static List<ParcelDrop> Parcels() => new()
    {
        new() { Id = "common", Weight = 70, Rarity = "Common" },
        new() { Id = "good", Weight = 22, Rarity = "Uncommon" },
        new() { Id = "rare", Weight = 7, Rarity = "Rare", Scraps = 3 },
        new() { Id = "epic", Weight = 1, Rarity = "Epic", Scraps = 10 }
    };
}
