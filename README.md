# Juniper — Nadeko customization

Juniper is a themed community bot built on the **unmodified upstream** Nadeko engine
(`ghcr.io/nadeko-bot/nadekobot:v6`). This repo holds only our customization — no Nadeko
source, no fork.

## Contents
- `config/` — Nadeko YAML config + `xp_template.json` + AI prompts
- `medusa/juniper_community/` — the only compiled artifact (a Medusa plugin: `.menu`, `.pins`, `.pin`)
- `docs/` — BRANDING, COMMUNITY_SETUP, CUSTOM_PATCHES, assets
- `seed/juniper.sql` — DB rows (aliases, permissions, XP rewards, shop, expressions, greet, feed, statuses, colours)
- `scripts/` — the apply scripts used during initial setup (reference)

## Why not a fork
We do not modify Nadeko's code. Upstream bug fixes arrive via the official image
(`docker pull` then recreate). A fork would only be needed if we patched core source —
we don't. `docs/CUSTOM_PATCHES.md` is intentionally empty.

## Updating Nadeko
1. `docker pull ghcr.io/nadeko-bot/nadekobot:v6`
2. Recreate the container.
3. In Discord: `.configreload bot`, `.configreload gambling`, `.configreload xp`.
4. Check `docs/CUSTOM_PATCHES.md` (empty) and confirm the Medusa still loads
   (`.meinfo juniper_community`). Rebuild it only if a Nadeko major change breaks its API.

## Rebuilding the Medusa (only if needed)
Requires .NET 8 SDK.
```
cd medusa/juniper_community
dotnet publish -o bin/medusae/juniper_community /p:DebugType=embedded
```
Copy the output folder to `data/medusae/juniper_community/` and restart (or `.meload juniper_community`).

## Edge-case note
The Medusa's command *help strings* log a cosmetic "missing for '' locale" warning.
Commands still register and run. Fix the locale key only if you want help text populated.
