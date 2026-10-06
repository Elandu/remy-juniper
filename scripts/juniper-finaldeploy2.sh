#!/bin/sh
set -e
D=/mnt/user/appdata/nadeko/data
DB=$D/NadekoBot.db
G=1556921331725901854
M=$D/medusae/juniper_community

docker stop nadeko >/dev/null 2>&1 || true
cp -p "$DB" "$DB.pre-juniperC-$(date +%Y%m%d-%H%M%S)"
cp -p "$D/xp.yml" "$D/xp.yml.pre-juniperC-$(date +%Y%m%d-%H%M%S)" 2>/dev/null || true

echo "== redeploy medusa =="
rm -rf "$M"
mkdir -p "$M"
cp -rf /tmp/juniper_community/. "$M/"
cat > "$M/config.json" <<'JSON'
{
  "startCutoff": "2026-09-01T00:00:00+10:00"
}
JSON
find "$M" -type f

echo "== fix feed message =="
sqlite3 "$DB" "UPDATE FeedSub SET Message='NEW UPLOAD' WHERE GuildId=$G;"
sqlite3 -header "$DB" "SELECT GuildId,ChannelId,Message FROM FeedSub;"

echo "== xp.yml shop appearances =="
awk '/^shop:/{exit} {print}' "$D/xp.yml" > /tmp/xp.new
cat >> /tmp/xp.new <<'YML'
shop:
  isEnabled: false
  frames:
    default:
      name: No frame
      price: 0
      url: ''
    charcoal:
      name: Charcoal
      price: 500
      url: ''
    sage:
      name: Sage
      price: 500
      url: ''
    mauve:
      name: Mauve
      price: 500
      url: ''
  bgs:
    default:
      name: Default Background
      price: 0
      url: ''
    charcoal:
      name: Charcoal
      price: 500
      url: ''
    sage:
      name: Sage
      price: 500
      url: ''
    mauve:
      name: Mauve
      price: 500
      url: ''
    static:
      name: Static
      price: 500
      url: ''
    cloudy:
      name: Cloudy
      price: 500
      url: ''
    garden:
      name: Garden
      price: 500
      url: ''
YML
mv /tmp/xp.new "$D/xp.yml"
grep -nE 'isEnabled|charcoal|sage|mauve|static|cloudy|garden' "$D/xp.yml" | head -20

echo "== start =="
docker start nadeko >/dev/null
sleep 22
docker ps --filter name=^/nadeko$ --format '{{.Names}} | {{.Status}}'
docker logs --tail 60 nadeko 2>&1 | grep -iE 'medusa|snek|juniper|missing|error|fail' | tail -20
