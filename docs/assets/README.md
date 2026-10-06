# JUNIPER — Assets

This folder holds Juniper visual assets. No Nadeko artwork is used.

## Avatar (required, manual)

The avatar must be generated/designed outside this environment and uploaded via the Discord Developer Portal.

Spec:
- 512x512 PNG (Discord scales down; must read at 32px)
- charcoal background `#1B1A1D`
- a single, very simple cream `#E8E1D8` botanical/geometric mark
- subtle juniper-leaf or four-point-star influence
- flat, no gradient, no character/mascot face, no text, no anime
- optional: one muted sage `#89917A` or mauve `#8C6F7D` accent

Filename when supplied: `avatar.png`

## XP card backgrounds / frames (optional)

The XP card uses `images.yml: xp.bg` for the card background and `xp.yml: shop:` for purchasable frames and backgrounds. Provide PNGs and point the URLs at them.

Required filenames (drop into a web-accessible location, e.g. `https://<host>/juniper/xp/<file>.png`):

Frames (`xp.yml: shop.frames`):
- `frame_charcoal.png` — subtle charcoal frame (default look)
- `frame_sage.png` — muted sage accent
- `frame_mauve.png` — muted mauve accent

Backgrounds (`xp.yml: shop.bgs`):
- `bg_charcoal.png`
- `bg_sage.png`
- `bg_mauve.png`
- `bg_static.png`
- `bg_cloudy.png`
- `bg_garden.png`

All: 500x245 (matching `xp_template.json: output_size`), dark/charcoal base, cream text-safe, no anime, no flowers, minimal.

Until these files exist, the shop appearance entries are wired but reference placeholder URLs; replace them in `xp.yml` and run `.configreload xp`.
