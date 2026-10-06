#!/bin/sh
set -e
D=/mnt/user/appdata/nadeko/data
STAMP=$(date +%Y%m%d-%H%M%S)

echo "== stop bot =="
docker stop nadeko >/dev/null
echo stopped

echo "== backup configs + db =="
for f in bot.yml xp.yml gambling.yml; do cp -p "$D/$f" "$D/$f.pre-juniper-$STAMP"; done
cp -p "$D/NadekoBot.db" "$D/NadekoBot.db.pre-juniper-$STAMP"
mkdir -p "$D/ai/prompts"
[ -f "$D/ai/prompts/SOUL.md" ] && cp -p "$D/ai/prompts/SOUL.md" "$D/ai/prompts/SOUL.md.pre-juniper-$STAMP"
[ -f "$D/ai/prompts/OPERATOR.md" ] && cp -p "$D/ai/prompts/OPERATOR.md" "$D/ai/prompts/OPERATOR.md.pre-juniper-$STAMP"
echo backed-up

echo "== gambling.yml: currency + timely + generation =="
sed -i -E 's/^(  sign: ).*/\1"✦"/' "$D/gambling.yml"
sed -i -E 's/^(  name: )Nadeko Flower$/\1Token/' "$D/gambling.yml"
sed -i -E 's/^(  chance: ).*/\10/' "$D/gambling.yml"
sed -i -E 's/^(    amount: )120$/\125/' "$D/gambling.yml"
sed -i -E 's/^(    cooldown: )12$/\124/' "$D/gambling.yml"
sed -i -E 's/^(    protType: ).*/\1None/' "$D/gambling.yml"

echo "== xp.yml: voice xp =="
sed -i -E 's/^(voiceXpPerMinute: ).*/\11/' "$D/xp.yml"

echo "== bot.yml: rotate statuses on + juniper colours =="
sed -i -E 's/^(rotateStatuses: ).*/\1true/' "$D/bot.yml"
sed -i -E 's/^(  ok: ).*/\189917a/' "$D/bot.yml"
sed -i -E 's/^(  error: ).*/\18c6f7d/' "$D/bot.yml"
sed -i -E 's/^(  pending: ).*/\18c6f7d/' "$D/bot.yml"

echo "== verify edits =="
grep -nE 'sign:|name: Token|chance:|amount:|cooldown:|protType:' "$D/gambling.yml" | head -12
grep -n 'voiceXpPerMinute' "$D/xp.yml"
grep -nE 'rotateStatuses:|  ok:|  error:|  pending:' "$D/bot.yml"

echo "== rotating statuses (5, CustomStatus=4) =="
sqlite3 "$D/NadekoBot.db" "DELETE FROM RotatingStatus;"
sqlite3 "$D/NadekoBot.db" "INSERT INTO RotatingStatus (Status,Type,DateAdded) VALUES
 ('keeping the lights on',4,strftime('%Y-%m-%dT%H:%M:%fZ','now')),
 ('somewhere in the background',4,strftime('%Y-%m-%dT%H:%M:%fZ','now')),
 ('probably AFK',4,strftime('%Y-%m-%dT%H:%M:%fZ','now')),
 ('checking the noticeboard',4,strftime('%Y-%m-%dT%H:%M:%fZ','now')),
 ('doing side quests',4,strftime('%Y-%m-%dT%H:%M:%fZ','now'));"
sqlite3 -header "$D/NadekoBot.db" "SELECT Id,Status,Type FROM RotatingStatus;"

echo "== guild colours (sage ok / mauve error+pending) =="
G=1556921331725901854
sqlite3 "$D/NadekoBot.db" "DELETE FROM GuildColors WHERE GuildId=$G;"
sqlite3 "$D/NadekoBot.db" "INSERT INTO GuildColors (GuildId,OkColor,ErrorColor,PendingColor) VALUES ($G,'89917a','8c6f7d','8c6f7d');"
sqlite3 -header "$D/NadekoBot.db" "SELECT * FROM GuildColors;"

echo "== ai prompts =="
cp /tmp/juniper-SOUL.md "$D/ai/prompts/SOUL.md"
cp /tmp/juniper-OPERATOR.md "$D/ai/prompts/OPERATOR.md"
wc -c "$D/ai/prompts/SOUL.md" "$D/ai/prompts/OPERATOR.md"

echo "== start bot =="
docker start nadeko >/dev/null
echo started
