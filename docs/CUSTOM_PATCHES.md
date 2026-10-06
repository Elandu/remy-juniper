# JUNIPER — Custom Patches

Policy: prefer config → permissions → aliases → expressions → XP template → Medusa → source patch (last resort). Every source patch must be marked `// JUNIPER PATCH` and recorded here.

**Current source patches: NONE.**

The implementation uses only Nadeko's configuration, permissions, aliases, expressions, XP template and the `juniper_community` Medusa. No Nadeko core source was modified, so pulling Nadeko updates requires no patch reconciliation.

## Watch list after a Nadeko update
- `data/xp_template.json` — verify juniper card still renders (`.xptempreload`).
- `data/medusae/juniper_community/` — verify it still loads (`.meinfo juniper_community`).
- `data/ai/prompts/SOUL.md` / `OPERATOR.md` — Nadeko may overwrite defaults on version bump.
- `xp.yml` `shop:` — custom XP card appearances live here.
- Permissions/aliases/shop rows live in the database and survive image updates.

If a future Nadeko version changes a signature the Medusa depends on, the fix belongs in the Medusa, not in core.
