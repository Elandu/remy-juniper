# REMY'S LOST & FOUND

A collection game for Juniper. Remy keeps finding things; members keep almost losing
them. Explore, collect, complete sets, and — rarely — recover a Sarah Relic.

Shipped as a Medusa plugin: `medusa/lost_and_found/`. **No Nadeko core changes.**
State lives in the medusa's own SQLite file; Tokens are written into Nadeko's own
`DiscordUser.CurrencyAmount` so rewards show up in `.balance`.

- Palette (in-game): Forest `#2E3A31`, Cream `#F4EBD8`, Sage `#A7C7A1`,
  Blush `#E7A6B8`, Mauve `#7C6BA3`.
- Tone: short, adult, dry, AU/UK spelling, minimal emoji.
- Never AI-generate or redraw Sarah art. Release pins are used exactly as supplied.

---

## 1. The game loop

1. **Explore** — `.explore` lists up to three unlocked areas; `.explore <area>` spends
   one of your daily expeditions and returns a find (or a small encounter/choice).
2. **Claim** — Remy occasionally drops something in a configured channel. `.grab`
   claims it first-come, first-served (limited copies when configured).
3. **Complete sets** — collecting every item in a set pays a **one-time** reward
   (XP, Tokens, a title and a badge). Set progress: `.sets`.
4. **Use your stuff** — `.craft` recipes, `.equip` one charm, `.recycle` duplicates
   into Scraps, `.gift` / `.trade` spares.
5. **Chase Relics** — at Explorer level 20+ with at least 3 sets complete, a low,
   configurable chance per eligible find turns up one of the seven **Sarah Relics**.
6. **Rank** — `.collectiontop` scores **unique** finds, rarity, completed sets,
   relics and secrets. Duplicate volume counts for nothing.

Separate progression: **Explorer XP / Explorer level** is entirely the medusa's own.
It is not Nadeko's server XP and does not affect level roles.

---

## 2. Commands

Member commands:

| Command | What it does |
|---------|--------------|
| `.explore` | List areas you can go to (up to 3). |
| `.explore <area>` | Spend a daily expedition in that area. |
| `.grab` | Claim the drop in this channel. |
| `.encounter` / `.lfchoose <n>` | View and resolve a pending choice encounter. |
| `.sets` | Set progress; hidden sets show as `???`. |
| `.shelf [@user]` | Collection profile (level, score, favourites). |
| `.inventory` | Everything you've kept. |
| `.item <name>` | Details for one item. |
| `.gift @user <item>` | Give a spare to someone. |
| `.trade @user <item>` / `.tradeaccept` / `.tradedecline` | Offer and settle a trade. |
| `.recycle <item> [confirm]` | Break a spare into Scraps (last copy needs confirm). |
| `.craft` / `.craft <recipe>` | List or make a recipe. |
| `.equip <item>` / `.unequip` | Wear one charm. |
| `.quests` (alias `.lfquests`) | This week's side quests and progress. |
| `.openparcel` / `.giftparcel @user` | Open or pass on a mystery parcel. |
| `.collectiontop` (alias `.ctop`) | Collection-score leaderboard. |
| `.lfhelp` (alias `.lostfound`) | How to play. |

Owner/admin commands (gated `[bot_owner_only]` — not available to members):

| Command | What it does |
|---------|--------------|
| `.lfspawn [item]` | Spawn a drop here (random if omitted). |
| `.lfgive @user <item>` | Grant an item directly. |
| `.lfresetdaily @user` | Reset a user's daily expeditions. |
| `.lfsetlevel @user <level>` | Set a user's Explorer level. |
| `.lfdebug [@user]` | Raw player/inventory/set/quest state. |
| `.lfreload` | Reload `config.json` and content. |

Design note: `.quests` was chosen over a bare `.lfquests`; Nadeko's core daily
quest log is `.questlog`, so there is no collision.

---

## 3. Areas

Seven areas. The Bedroom is open from level 1; the rest unlock by Explorer level,
or explicitly (e.g. a set reward can unlock one). Areas bias which sets can drop.

| Id | Name | Unlock | Sets drawn on |
|----|------|--------|----------------|
| `bedroom` | Bedroom | Lv 1 (default) | Remy's Things, Cosy Home |
| `garden` | Garden | Lv 3 | Garden Finds, Cosy Home |
| `studio` | Studio | Lv 5 | Studio Junk, Night Shift |
| `arcade` | Arcade | Lv 8 | Arcade Pocket, Night Shift |
| `beach` | Beach | Lv 12 | Coastal Days, Remy's Things |
| `backstage` | Backstage | Lv 20 | Backstage, Studio Junk |
| `unknown` | ??? | Secret | All sets |

The secret area (`unknown`) is never listed in `.explore`; it only becomes reachable
once explicitly unlocked (see `secretAreaCompletedSets`, default 5 completed sets, or
a set reward with `unlockArea`). Keep its identity out of public channels/docs where
it would spoil the surprise.

Each area may also carry `rarityWeights`, a set-weight bonus, and its own encounter
pool.

---

## 4. Rarities

| Rarity | Stars | Default collection value | Default XP | Default Tokens |
|--------|:-----:|:------------------------:|:----------:|:--------------:|
| Common | ★ | 1 | 8 | 1 |
| Uncommon | ★★ | 2 | 14 | 2 |
| Rare | ★★★ | 5 | 25 | 4 |
| Epic | ★★★★ | 10 | 45 | 8 |
| Legendary | ★★★★★ | 25 | 90 | 20 |
| Sarah Relic | ★★★★★★ | 100 | 200 | 50 |

Global drop weights default 55 / 24 / 13 / 6 / 2 (Common → Legendary); Relics are
never in the normal weights table — they come only from the relic roll.

---

## 5. Items, sets and relics

- **~72 catalogue entries**: 56 collectible items across 8 discoverable sets, plus 9
  set badges and 7 Sarah Relics.
- Sets: **Remy's Things, Studio Junk, Night Shift, Coastal Days, Arcade Pocket,
  Garden Finds, Backstage, Cosy Home** — plus hidden **Set Badges** and **Sarah
  Relics** sets.
- Set rewards are **one-time** and persisted in `completed_sets`.

### Sarah Relics (exact release-pin art)

| Id | Name | Image |
|----|------|-------|
| `relic_yibc` | Yes, I've Been Crying | `yes-ive-been-crying-round.png` |
| `relic_tainted` | Tainted Timeline | `tainted-timeline-round.png` |
| `relic_absence` | Absence | `absence-round.png` |
| `relic_september` | September | `september-round.png` |
| `relic_apparently` | Apparently | `apparently-round.png` |
| `relic_frame` | Frame | `frame-round.png` |
| `relic_wollongong` | Wollongong Road | `wollongong-road-round.png` |

Relics are `rarity: SarahRelic`, `tradable: false`, `recyclable: false`. Eligibility
is **Explorer level ≥ 20** and **≥ 3 completed sets**, then a low chance per eligible
find (default 1.5%). All configurable under `relic` in `config.json`.

---

## 6. Asset locations

| What | Where |
|------|-------|
| Live medusa assets (deployed) | `/mnt/user/appdata/nadeko/data/medusae/lost_and_found/assets/` |
| Repo source of truth | `medusa/lost_and_found/assets/` |
| Sliced item art origin | `Y:\Discord\Juniper\assets\items\` (also mirrored to the website) |
| Release pins (Sarah art) | `Y:\Discord\Juniper\assets\release-pins\` (also `Y:\GitHub\sarah-website\public`) |
| Brand hero | `medusa/lost_and_found/assets/juniper-hero.png` (`1672x941`) |

Item art is referenced **by filename only** (e.g. `image: "vinyl-record.png"`); the
bot resolves it against the medusa `assets/` folder and attaches the file. Anything
not present simply renders without a thumbnail, so a missing file never breaks a
command.

Current mapping is 28 item illustrations shared across 56 items (plus badges). The
`-round.png` files are used only by Relics.

---

## 7. How to add an item

1. Add an `I(...)` entry in `DefaultContent.Items()` in `Config.cs` (or an item object
   if you are overriding via `config.json`). Key fields:
   - `id` (snake_case, unique), `name`, `description`
   - `rarity`, `set`, `areas` (comma-separated area ids; empty = any area that offers
     the set)
   - `effect` (`music` / `bonus` / `backstage`), `secret`, `tradable`, `recyclable`
   - `image` — a filename that exists in `assets/`
2. Put the PNG in `medusa/lost_and_found/assets/`.
3. Rebuild and copy to the container (see §11), then `.lfreload` in Discord.
4. Run the tests — `Catalog_EveryItemReferencesAnExistingImageAsset` fails if an item
   points at a missing file.

To add a **set**, add a `SetDef` with a unique `id` and a `SetReward`; give the items
that `set` id. To add an **area**, add an `AreaDef` with `unlockLevel` and the sets it
draws on. Recipes and encounters are lists in the same file, fully data-driven.

---

## 8. How to add Sarah art safely

Non-negotiable:

1. **Never generate or redraw Sarah's art.** Only use files Sarah has supplied.
2. Release-pin art arrives as `<name>-round.png` in
   `Y:\Discord\Juniper\assets\release-pins\`. Copy the exact file; do not rename,
   recolour, crop or re-export.
3. Relics must reference the exact `*-round.png` filename. The test enforces this.
4. Do not commit anything from Sarah's private working folders that isn't already a
   release asset. Keep unreleased art, drafts and secrets out of git.
5. If new Sarah art is released, add it as a Relic (or a matching secret item) with
   `tradable: false`, `recyclable: false`, and the exact filename. Update the Relic
   table in this doc.

---

## 9. Config (`config.json`)

`config.json` sits next to the assembly and overrides any embedded default. It is
reloaded with `.lfreload`. Keys:

- `enabled`, `allowedGuilds` (empty = all guilds), `timeZone` (default
  `Australia/Sydney`)
- `dailyExpeditions` (default 1; **no punitive streaks** — missing a day just means
  you didn't spend it)
- `encounterChancePercent`, `maxRecentDiscoveries`
- `secretAreaCompletedSets`, `setCompletionScoreBonus`, `secretScoreBonus`
- `xpCurve` — `earlyMax/earlyBase/earlyStep`, `midMax/midBase/midStep`,
  `lateBase/lateGrowth`
- `relic` — `minLevel` (20), `minCompletedSets` (3), `chancePercent` (1.5),
  `minExpeditionsBetween` (0 = off)
- `drops` — `enabled`, `chancePerMessage`, `minMessagesBetween`, `cooldownMinutes`,
  `maxActivePerChannel`, `claimTimeoutMinutes`, `copies`, `parcelChance`,
  `channels` (allow-list; empty = any channel), `ignoredChannels`, `minMessageLength`
- `questing` — `enabled`, `perWeek`
- `rarityWeights`, `xpByRarity`, `tokensByRarity`, `collectionValueByRarity`,
  `scrapValueByRarity`
- `items`, `sets`, `areas`, `recipes`, `encounters`, `quests`, `parcels`, `charms`
  (content; omit to use the embedded defaults)

Drop defaults are deliberately **conservative**: ~1.5% per eligible message, a
minimum of 35 messages between, a 25-minute channel cooldown, one active drop per
channel. Bots and command-style messages are ignored. Tune per server in
`config.json`.

---

## 10. Database

The medusa keeps its own SQLite file at `data/lostfound.db` (two levels up from the
medusa assembly, alongside `NadekoBot.db`). All state that must be atomic (drop
claims, inventory increments, set completions, trades, tokens earned) runs inside
`BEGIN IMMEDIATE` transactions. Tokens are written to Nadeko's `DiscordUser` table
via an atomic upsert on `UserId`.

---

## 11. Admin setup

1. Build:
   ```
   cd medusa/lost_and_found
   dotnet build -c Release
   ```
2. Copy the built folder (dll + `assets/`, `cmds.yml`, `res.yml`, `config.json`) to
   the container's medusa directory, e.g.
   `/mnt/user/appdata/nadeko/data/medusae/lost_and_found/`.
3. Add `lost_and_found` to `loaded:` in
   `/mnt/user/appdata/nadeko/data/medusae/medusa.yml`.
4. Restart the `nadeko` container (or `.meload lost_and_found`).
5. Confirm the log line `Loaded medusa 'lost_and_found'` and no errors, then check
   `.lfhelp` and `.explore` in Discord.
6. Optional per-server tuning: edit `config.json`, then `.lfreload`.

Required admin config: set `drops.channels` to the channel ids where drops may
appear, and set `allowedGuilds` if the bot serves more than one guild.

---

## 12. Tests

```
cd medusa/lost_and_found.tests
dotnet test
```

Covers: rarity selection, XP/level curve and carry-over, daily expedition limits and
reset, atomic drop claim under concurrency, limited-copy claims, duplicate inventory
increments without inflating uniqueness, one-time set rewards, collection score
ignoring duplicate spam, crafting quantities, relic eligibility, area unlocks, and
item-image integrity.
