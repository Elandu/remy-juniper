#!/bin/sh
# Reapply Juniper config to a Nadeko data dir.
# Usage: DATA_DIR=/mnt/user/appdata/nadeko/data ./apply.sh
set -e
DATA_DIR="${DATA_DIR:-../data}"
STAMP=$(date +%Y%m%d-%H%M%S)
echo "Applying to $DATA_DIR"
for f in bot.yml gambling.yml xp.yml xp_template.json; do
  [ -f "$DATA_DIR/$f" ] && cp -p "$DATA_DIR/$f" "$DATA_DIR/$f.pre-juniper-$STAMP"
  cp -f "config/$f" "$DATA_DIR/$f"
done
mkdir -p "$DATA_DIR/ai/prompts"
cp -f config/ai-prompts/SOUL.md config/ai-prompts/OPERATOR.md "$DATA_DIR/ai/prompts/"
echo "Seed SQL: apply with: sqlite3 \"$DATA_DIR/NadekoBot.db\" < seed/juniper.sql"
echo "Done. Restart nadeko (and .meload juniper_community if not in medusa.yml)."
