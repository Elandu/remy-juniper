#!/bin/sh
set -e
D=/mnt/user/appdata/nadeko/data
DB=$D/NadekoBot.db
G=1556921331725901854
BOT=1468398432980045876
UPLOADS=1556981902672400386
TOKEN='YOUR_BOT_TOKEN'
AUTH="Authorization: Bot $TOKEN"
API=https://discord.com/api/v10
NOW=$(date -u +%Y-%m-%dT%H:%M:%SZ)

docker stop nadeko >/dev/null 2>&1 || true

rid() { curl -s "$API/guilds/$G/roles" -H "$AUTH" | jq -r --arg n "$1" '.[]|select(.name==$n)|.id' | head -1; }
R_NIGHT=$(rid "Night Shift"); R_SIDE=$(rid "Side Quest"); R_BG=$(rid "Background Character"); R_OFF=$(rid "Offline")
R_FINE=$(rid "Probably Fine"); R_HOUSE=$(rid "Houseplant"); R_CRYPT=$(rid "Local Cryptid")
echo "shop role ids: $R_NIGHT $R_SIDE $R_BG $R_OFF $R_FINE $R_HOUSE $R_CRYPT"

echo "== shop entries =="
sqlite3 "$DB" <<SQL
DELETE FROM ShopEntry WHERE GuildId=$G;
INSERT INTO ShopEntry (GuildId,"Index",Price,Name,AuthorId,Type,RoleName,RoleId,DateAdded) VALUES
 ($G,1,200,'Night Shift',$BOT,0,'Night Shift',$R_NIGHT,'$NOW'),
 ($G,2,200,'Side Quest',$BOT,0,'Side Quest',$R_SIDE,'$NOW'),
 ($G,3,200,'Background Character',$BOT,0,'Background Character',$R_BG,'$NOW'),
 ($G,4,200,'Offline',$BOT,0,'Offline',$R_OFF,'$NOW'),
 ($G,5,200,'Probably Fine',$BOT,0,'Probably Fine',$R_FINE,'$NOW'),
 ($G,6,200,'Houseplant',$BOT,0,'Houseplant',$R_HOUSE,'$NOW'),
 ($G,7,200,'Local Cryptid',$BOT,0,'Local Cryptid',$R_CRYPT,'$NOW');
SQL
echo "shop rows: $(sqlite3 "$DB" "SELECT COUNT(*) FROM ShopEntry WHERE GuildId=$G;")"

echo "== expressions =="
sqlite3 "$DB" <<SQL
DELETE FROM Expressions WHERE GuildId=$G;
INSERT INTO Expressions (GuildId,Response,Trigger,AutoDeleteTrigger,DmResponse,ContainsAnywhere,AllowTarget,DateAdded) VALUES
 ($G,'I''ll take it.','good bot',0,0,0,0,'$NOW'),
 ($G,'Fair.','bad bot',0,0,0,0,'$NOW');
SQL

echo "== guildconfigs + permissions =="
sqlite3 "$DB" <<SQL
DELETE FROM Permissions WHERE GuildId=$G;
DELETE FROM GuildConfigs WHERE GuildId=$G;
INSERT INTO GuildConfigs (GuildId,Prefix,DeleteMessageOnCommand,VerbosePermissions,CleverbotEnabled,WarningsInitialized,VerboseErrors,NotifyStreamOffline,DeleteStreamOnlineMessage,WarnExpireHours,WarnExpireAction,DisableGlobalExpressions,ExpressionOverrideEnabled,StickyRoles,DateAdded)
 VALUES ($G,'.',0,0,0,0,0,0,0,0,0,0,0,0,'$NOW');
SQL
GC=$(sqlite3 "$DB" "SELECT Id FROM GuildConfigs WHERE GuildId=$G;")
sqlite3 "$DB" "INSERT INTO Permissions (GuildId,\"Index\",PrimaryTarget,PrimaryTargetId,SecondaryTarget,SecondaryTargetName,IsCustomCommand,State,GuildConfigId,DateAdded) VALUES ($G,0,3,0,2,'*',0,1,$GC,'$NOW');"
IDX=1
sqlite3 "$DB" "INSERT INTO Permissions (GuildId,\"Index\",PrimaryTarget,PrimaryTargetId,SecondaryTarget,SecondaryTargetName,IsCustomCommand,State,GuildConfigId,DateAdded) VALUES ($G,$IDX,2,$G,0,'Waifus',0,0,$GC,'$NOW');"
IDX=$((IDX+1))
CMDS="vote clubtransfer clubadmin clubcreate clubicon clubbanner clubinfo clubbans clubapps clubapply clubaccept clubreject clubleave clubkick clubban clubunban clubdesc clubdisband clublb clubrename betroll luckyladder bettest rakeback race joinrace betstats winlb gamblestats betstatsreset gamblestatsreset blackjack hit stand double betdraw betflip slot pick plant gencurrency gencurlist anime anilist manga pokemon pokemonability patron patrons patronmessage donate"
for c in $CMDS; do
  sqlite3 "$DB" "INSERT INTO Permissions (GuildId,\"Index\",PrimaryTarget,PrimaryTargetId,SecondaryTarget,SecondaryTargetName,IsCustomCommand,State,GuildConfigId,DateAdded) VALUES ($G,$IDX,2,$G,1,'$c',0,0,$GC,'$NOW');"
  IDX=$((IDX+1))
done
for ar in 1556922117411307651 1556933611566010381; do
  sqlite3 "$DB" "INSERT INTO Permissions (GuildId,\"Index\",PrimaryTarget,PrimaryTargetId,SecondaryTarget,SecondaryTargetName,IsCustomCommand,State,GuildConfigId,DateAdded) VALUES ($G,$IDX,2,$ar,2,'*',0,1,$GC,'$NOW');"
  IDX=$((IDX+1))
done
echo "perm rows: $(sqlite3 "$DB" "SELECT COUNT(*) FROM Permissions WHERE GuildId=$G;")"

echo "== greet (prepared, disabled) =="
sqlite3 "$DB" <<SQL
DELETE FROM GreetSettings WHERE GuildId=$G;
INSERT INTO GreetSettings (GuildId,GreetType,MessageText,IsEnabled,ChannelId,AutoDeleteTimer) VALUES
 ($G,0,'Hey %user.mention%.
Welcome in.',0,NULL,0),
 ($G,2,'%user.name% left.
Hope they''re alright.',0,NULL,0);
SQL

echo "== feed =="
sqlite3 "$DB" <<SQL
DELETE FROM FeedSub WHERE GuildId=$G;
INSERT INTO FeedSub (GuildId,ChannelId,Url,Message,DateAdded) VALUES
 ($G,$UPLOADS,'https://www.youtube.com/feeds/videos.xml?channel_id=UC6luqCLxAE3J_iyNvkcZasA','NEW UPLOAD
{0}
{1}','$NOW');
SQL

echo "== nickname =="
curl -s -X PATCH "$API/guilds/$G/members/@me" -H "$AUTH" -H "Content-Type: application/json" -d '{"nick":"Juniper"}' -o /dev/null -w "nick HTTP %{http_code}\n" || true

echo "== totals =="
for t in CommandAlias XpSettings XpRoleReward XpCurrencyReward ShopEntry Expressions Permissions GreetSettings FeedSub; do
  printf "%s: %s\n" "$t" "$(sqlite3 "$DB" "SELECT COUNT(*) FROM $t;")"
done

echo "== start =="; docker start nadeko >/dev/null; echo started
sleep 18
docker ps --filter name=^/nadeko$ --format '{{.Names}} | {{.Status}}'
docker logs --tail 8 nadeko 2>&1 | tail -8
