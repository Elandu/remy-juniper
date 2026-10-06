using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using NadekoBot.Medusa;

public sealed class JuniperCommunity : Snek
{
    public override string Name => "juniper_community";

    private static readonly Color Sage = new(0x89, 0x91, 0x7A);
    private static readonly Color Cream = new(0xE8, 0xE1, 0xD8);
    private static readonly Color Mauve = new(0x8C, 0x6F, 0x7D);

    private static readonly HashSet<string> GameCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "trivia", "t", "hangman", "minesweeper", "acrophobia", "connect4", "race", "roll", "flip", "draw", "fish"
    };

    private readonly ConcurrentDictionary<ulong, Member> _members = new();
    private readonly object _saveLock = new();
    private string _dir = ".";
    private string _path = "juniper_pins.json";
    private DateTimeOffset _startCutoff = DateTimeOffset.MinValue;

    public override ValueTask InitializeAsync()
    {
        _dir = Path.GetDirectoryName(typeof(JuniperCommunity).Assembly.Location) ?? ".";
        _path = Path.Combine(_dir, "juniper_pins.json");
        var cfgPath = Path.Combine(_dir, "config.json");
        try
        {
            if (File.Exists(cfgPath))
            {
                var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(cfgPath));
                if (cfg is not null && DateTimeOffset.TryParse(cfg.startCutoff, out var c))
                    _startCutoff = c;
            }
        }
        catch { }

        try
        {
            if (File.Exists(_path))
            {
                var data = JsonSerializer.Deserialize<Dictionary<ulong, Member>>(File.ReadAllText(_path));
                if (data is not null)
                    foreach (var kv in data)
                        _members[kv.Key] = kv.Value;
            }
        }
        catch { }

        return default;
    }

    public override ValueTask DisposeAsync()
    {
        Save();
        return default;
    }

    private void Save()
    {
        lock (_saveLock)
        {
            try
            {
                var map = _members.ToDictionary(x => x.Key, x => x.Value);
                File.WriteAllText(_path, JsonSerializer.Serialize(map));
            }
            catch { }
        }
    }

    private static DateTimeOffset ToSydney(DateTimeOffset t)
    {
        try { return TimeZoneInfo.ConvertTime(t, TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney")); }
        catch { return t.ToOffset(TimeSpan.FromHours(10)); }
    }

    private Member Get(ulong id)
        => _members.GetOrAdd(id, _ => new Member());

    public override ValueTask<bool> ExecOnMessageAsync(IGuild? guild, IUserMessage msg)
    {
        try
        {
            if (guild is null || msg.Author.IsBot)
                return default;

            var local = ToSydney(msg.Timestamp);
            var m = Get(msg.Author.Id);
            m.firstSeen ??= local.UtcDateTime.ToString("o");
            m.days ??= new List<string>();
            var day = local.ToString("yyyy-MM-dd");
            if (!m.days.Contains(day)) m.days.Add(day);
            var h = local.Hour;
            if (h >= 23 || h < 4) m.nightOwl++;
            Save();
        }
        catch { }
        return default;
    }

    public override ValueTask ExecPostCommandAsync(AnyContext ctx, string moduleName, string commandName)
    {
        try
        {
            var c = commandName.TrimStart('.').ToLowerInvariant();
            var m = Get(ctx.User.Id);
            if (c == "fish")
                m.fished = true;
            if (GameCommands.Contains(c))
                m.games++;
            Save();
        }
        catch { }
        return default;
    }

    [cmd]
    public async Task Menu(AnyContext ctx)
    {
        var embed = new EmbedBuilder()
            .WithColor(Sage)
            .WithTitle("JUNIPER")
            .WithDescription(
                "**PROFILE**\n" +
                "`.profile` — your profile\n" +
                "`.stats` — text profile\n" +
                "`.levels` — server activity board\n" +
                "`.balance` — your Tokens\n\n" +
                "**REWARDS**\n" +
                "`.daily` — daily Tokens\n" +
                "`.store` — community shop\n\n" +
                "**PLAY**\n" +
                "`.fish` — go fishing\n" +
                "`.fishbook` — your catches\n" +
                "`.fishtop` — fishing leaderboard\n" +
                "`.trivia` — trivia\n" +
                "`.hangman` — hangman\n" +
                "`.minesweeper` — minesweeper\n\n" +
                "**USEFUL**\n" +
                "`.afk` — set an AFK message\n" +
                "`.remind` — set a reminder\n" +
                "`.poll` — start a poll where permitted")
            .WithFooter("Juniper • .menu");
        await ctx.Channel.EmbedAsync(embed);
    }

    [cmd]
    public async Task Pins(GuildContext ctx)
        => await ctx.Channel.EmbedAsync(BuildPins(Get(ctx.User.Id), ctx.User.Username));

    [cmd]
    public async Task Pin(GuildContext ctx, IUser? user = null)
    {
        var target = user ?? ctx.User;
        string name = target is IGuildUser gu ? (gu.Nickname ?? gu.Username) : target.Username;
        DateTimeOffset? joined = null;
        if (target is IGuildUser g2) joined = g2.JoinedAt;
        await ctx.Channel.EmbedAsync(BuildPins(Get(target.Id), name, joined));
    }

    private EmbedBuilder BuildPins(Member m, string name, DateTimeOffset? joined = null)
    {
        var days = m.days?.Count ?? 0;
        var night = m.nightOwl;
        var games = m.games;
        var fished = m.fished;
        var fromStart = joined is not null && _startCutoff != DateTimeOffset.MinValue && joined < _startCutoff;

        string Row(string title, bool ok, string desc) => ok ? $"**{title}** — {desc}" : $"`{title}` — locked";

        var embed = new EmbedBuilder()
            .WithColor(fromStart || m.nightOwl >= 25 || days >= 14 || fished || games >= 10 ? Sage : Mauve)
            .WithTitle("Pins")
            .WithDescription($"Pins for {name}.")
            .AddField("NIGHT OWL", Row("NIGHT OWL", night >= 25, "Apparently sleep is optional."))
            .AddField("REGULAR", Row("REGULAR", days >= 14, "Kept coming back."))
            .AddField("PART OF THE FURNITURE", Row("PART OF THE FURNITURE", days >= 90, "Basically permanent."))
            .AddField("FIRST CATCH", Row("FIRST CATCH", fished, "Could've been bigger."))
            .AddField("SIDE QUEST", Row("SIDE QUEST", games >= 10, "Got distracted."))
            .AddField("HERE FROM THE START", Row("HERE FROM THE START", fromStart, "Before it got busy."))
            .WithFooter($"Juniper • .pins  ·  {night}/25 night  ·  {days} days  ·  {games}/10 games");
        return embed;
    }

    public sealed class Member
    {
        public int nightOwl { get; set; }
        public List<string>? days { get; set; }
        public bool fished { get; set; }
        public int games { get; set; }
        public string? firstSeen { get; set; }
    }

    public sealed class Config
    {
        public string? startCutoff { get; set; }
    }
}
