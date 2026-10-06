# JUNIPER — Community Setup

Complete record of the Juniper configuration applied to the Nadeko engine.

- Guild: **Sarah Jane's Inner Circle** — `1556921331725901854`
- Nadeko container: `nadeko` (`ghcr.io/nadeko-bot/nadekobot:v6`), data at `/mnt/user/appdata/nadeko/data`
- Bot application id: `1468398432980045876`
- Docs location: `/mnt/user/appdata/nadeko/data/juniper/`

---

## 1. BOT IDENTITY

- **Display name:** Juniper (Discord Developer Portal → Application name; not set from config)
- **Server nickname:** set to `Juniper` via API (`.setnick` equivalent). Already applied.
- **Avatar:** replace via Developer Portal with the Juniper mark (charcoal bg, cream botanical/four-point-star). See `assets/README.md` for the exact spec. No Nadeko artwork.
- **Status rotation:** enabled (`bot.yml: rotateStatuses: true`) with these Custom statuses (RotatingStatus table):
  - keeping the lights on
  - somewhere in the background
  - probably AFK
  - checking the noticeboard
  - doing side quests
  - No server/user counts, no Nadeko reference.
- Owner id (in `creds.yml`): `213499073599111168` (erunamo).

## 2. CURRENCY  (`gambling.yml`)
- `currency.name: Token`
- `currency.sign: "✦"`
- Random currency generation disabled: `generation.chance: 0`
- Decay: off (`decay.percent: 0`)

## 3. DAILY REWARD  (`gambling.yml` → timely, exposed as `.daily`)
- `timely.amount: 25`
- `timely.cooldown: 24` (hours)
- `timely.protType: None`
- Alias: `.daily` → `.timely`

## 4. XP  (`xp.yml` + DB)
- `textXpPerMessage: 3`, `textXpCooldown: 300` (5 min)
- `voiceXpPerMinute: 1`
- `textXpFromImage: 3`
- Formula: A=9, C=27 (default)
- Bots are ignored (`bot.yml: ignoreOtherBots: true`)
- Random currency spawning off (see Currency)
- Channel exclusions: none yet — provide channel IDs to exclude bot/game channels via `.xpexclude #channel`.

### Level roles (created)
| Level | Role |
|------|------|
| 10 | Regular |
| 25 | Local |
| 50 | Fixture |
| 100 | Probably Lives Here |

### Level token rewards
| Level | Tokens |
|------|--------|
| 5 | 25 |
| 10 | 50 |
| 25 | 150 |
| 50 | 300 |
| 100 | 750 |

## 5. ALIASES  (CommandAlias table)
| Alias | Maps to |
|------|---------|
| `.profile` | `.experience` |
| `.stats` | `.experiencetext` |
| `.levels` | `.xpleaderboard -c` |
| `.balance` | `.cash` |
| `.tokens` | `.cash` |
| `.daily` | `.timely` |
| `.store` | `.shop` |
| `.buy` | `.shopbuy` |
| `.fishbook` | `.fishlist` |
| `.fishtop` | `.fishlb` |

## 6. PERMISSIONS  (Permissions table; @everyone denied, Admins allowed)
`GuildConfigs.VerbosePermissions: 0`, `VerboseErrors: 0` (quiet blocked commands).

Denied for @everyone (admins keep access via a higher-index allow-all):
- **Module:** Waifus (waifu, manager, fan, backing, gift, hug/kiss/pat/nom…)
- **Clubs:** clubtransfer, clubadmin, clubcreate, clubicon, clubbanner, clubinfo, clubbans, clubapps, clubapply, clubaccept, clubreject, clubleave, clubkick, clubban, clubunban, clubdesc, clubdisband, clublb, clubrename
- **Casino/wager:** betroll, luckyladder, bettest, rakeback, race, joinrace, betstats, winlb, gamblestats, betstatsreset, gamblestatsreset, blackjack, hit, stand, double, betdraw, betflip, slot
- **Currency generation:** pick, plant, gencurrency, gencurlist
- **Anime/manga/pokemon:** anime, anilist, manga, pokemon, pokemonability
- **Public Nadeko:** vote, patron, patrons, patronmessage, donate

Kept for members: cash, give, leaderboard, timely, shop, shopbuy, raffle, roll, rolluo, nroll, draw, drawnew, deckshuffle, flip, connect4, rps, games, fishing, trivia, hangman, minesweeper, utility.

## 7. SHOP  (ShopEntry table) — cosmetic roles, 200 Tokens each
Night Shift · Side Quest · Background Character · Offline · Probably Fine · Houseplant · Local Cryptid

- Type: Role, price 200, no permissions, created below Patreon roles.
- XP card frame/background shop is separate (`xp.yml` `shop:`).

## 8. PINS  (see Medusa) — social collectibles, no token rewards
| Pin | Requirement | Description |
|-----|-------------|-------------|
| NIGHT OWL | 25 eligible messages 23:00–04:00 Australia/Sydney | "Apparently sleep is optional." |
| REGULAR | Activity on 14 distinct days | "Kept coming back." |
| PART OF THE FURNITURE | Activity on 90 distinct days | "Basically permanent." |
| FIRST CATCH | Catch ≥1 fish | "Could've been bigger." |
| SIDE QUEST | 10 Juniper game sessions | "Got distracted." |
| HERE FROM THE START | Join date before a configurable cutoff | "Before it got busy." |

Commands: `.pins`, `.pin @user`. Pin state stored under `data/medusae/juniper_community/`.

## 9. GAMES  (enabled)
Fishing (fish, fishspot, fishlist, fishlb, fishstarslb, fishshop, fishbuy, fishuse, fishunequip, fishinv), Trivia, Hangman, Minesweeper, Acrophobia (if functional), dice roll, coin flip, card draw/shuffle. nCanvas retained but not on the menu.

## 10. RSS / SOCIAL
Add feeds (member-facing uses Juniper templates):
- `.ytuploadnotif https://www.youtube.com/channel/<CHANNEL_ID>` (in the target channel)
- `.feed <RSS_URL> [#channel] [message]`

Configured feed: YouTube uploads for `UC6luqCLxAE3J_iyNvkcZasA` → `#recent-uploads` (`1556981902672400386`), template:
```
NEW UPLOAD
{0}
{1}
```
Stream template to use on `.streamadd`: `SARAH IS LIVE` / `{title}` / `{url}`. Generic feed: `NEW POST`.

## 11. AI
- AI agent is **disabled** (`ai-agent.yml: enabled: false`) — not force-enabled.
- Prompts ready at:
  - `data/ai/prompts/SOUL.md`
  - `data/ai/prompts/OPERATOR.md`
- Enable with `.conf ai-agent enabled true` (owner) or by editing `ai-agent.yml` and `.configreload ai-agent`. Prompts are hot-reloaded (`.aiprompt`).

## 12. WELCOME / GOODBYE  (GreetSettings, prepared but disabled)
- Greet: `Hey %user.mention%.` / `Welcome in.`
- Bye: `%user.name% left.` / `Hope they're alright.`
- Enable with `.greet` / `.bye` (needs a channel) only if the Patreon access workflow doesn't already welcome people.

## 13. UPDATING NADEKO
1. `cd /mnt/user/appdata/nadeko`
2. `docker compose pull` (or `docker pull ghcr.io/nadeko-bot/nadekobot:v6`)
3. `docker compose up -d` (or recreate the container)
4. Reload config in Discord: `.configreload bot`, `.configreload gambling`, `.configreload xp`
5. **Preserved custom files (do not overwrite):**
   - `data/juniper/` (this folder)
   - `data/ai/prompts/SOUL.md`, `OPERATOR.md`
   - `data/medusae/juniper_community/`
   - `data/xp_template.json` (Juniper card) — reapply if overwritten
6. **Review `CUSTOM_PATCHES.md`** after each update.

## 14. POST-DEPLOY COMMANDS (owner, in Discord)
```
.configreload bot
.configreload gambling
.configreload xp
.meload juniper_community        # if the Medusa is installed
```
Verify with `.menu`, `.daily`, `.balance`, `.store`, `.profile`, `.levels`.

## 15. MANUAL ACTIONS (Discord Developer Portal)
- Set the application/bot **name** to `Juniper`.
- Upload the **avatar** (see `assets/README.md`).
- No intent changes required beyond what is already enabled (Message Content, Server Members, Presence).
