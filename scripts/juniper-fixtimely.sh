#!/bin/sh
D=/mnt/user/appdata/nadeko/data
docker stop nadeko >/dev/null
sed -i -E 's/^  amount: 120/  amount: 25/' "$D/gambling.yml"
sed -i -E 's/^  cooldown: 12/  cooldown: 24/' "$D/gambling.yml"
sed -i -E 's/^  protType: .*/  protType: None/' "$D/gambling.yml"
echo "--- timely block ---"
sed -n '50,64p' "$D/gambling.yml"
docker start nadeko >/dev/null
echo started
sleep 20
docker ps --filter name=^/nadeko$ --format '{{.Names}} | {{.Status}}'
docker logs --tail 12 nadeko 2>&1 | tail -12
