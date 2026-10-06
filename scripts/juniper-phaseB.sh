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

echo "== stop =="; docker stop nadeko >/dev/null
cp -p "$DB" "$DB.pre-juniperB-$(date +%Y%m%d-%H%M%S)"

mkrole() {
  r=$(curl -s -X POST "$API/guilds/$G/roles" -H "$AUTH" -H "Content-Type: application/json" \
       -d "{\"name\":\"$1\",\"permissions\":\"0\",\"color\":0,\"hoist\":false,\"mentionable\":false}")
  echo "$r" | jq -r '.id'
}

echo "== create roles =="
R_REGULAR=$(mkrole "Regular"); echo "Regular=$R_REGULAR"
R_LOCAL=$(mkrole "Local"); echo "Local=$R_LOCAL"
R_FIXTURE=$(mkrole "Fixture"); echo "Fixture=$R_FIXTURE"
R_LIVES=$(mkrole "Probably Lives Here"); echo "ProbLivesHere=$R_LIVES"
R_NIGHT=$(mkrole "Night Shift"); echo "NightShift=$R_NIGHT"
R_SIDE=$(mkrole "Side Quest"); echo "SideQuest=$R_SIDE"
R_BG=$(mkrole "Background Character"); echo "BackgroundCharacter=$R_BG"
R_OFF=$(mkrole "Offline"); echo "Offline=$R_OFF"
R_FINE=$(mkrole "Probably Fine"); echo "ProbablyFine=$R_FINE"
R_HOUSE=$(mkrole "Houseplant"); echo "Houseplant=$R_HOUSE"
R_CRYPT=$(mkrole "Local Cryptid"); echo "LocalCryptid=$R_CRYPT"

echo "== aliases =="
sqlite3 "$DB" <<SQL
DELETE FROM CommandAlias WHERE GuildId=$G;
INSERT INTO CommandAlias (GuildId,Trigger,Mapping,DateAdded) VALUES
 ($G,'profile','.experience','$NOW'),
 ($G,'stats','.experiencetext','$NOW'),
 ($G,'levels','.xpleaderboard -c','$NOW'),
 ($G,'balance','.cash','$NOW'),
 ($G,'tokens','.cash','$NOW'),
 ($G,'daily','.timely','$NOW'),
 ($G,'store','.shop','$NOW'),
 ($G,'buy','.shopbuy','$NOW'),
 ($G,'fishbook','.fishlist','$NOW'),
 ($G,'fishtop','.fishlb','$NOW');
SQL

echo "== xp settings + rewards =="
sqlite3 "$DB" <<SQL
DELETE FROM XpRoleReward; DELETE FROM XpCurrencyReward; DELETE FROM XpSettings WHERE GuildId=$G;
INSERT INTO XpSettings (GuildId,XpFormulaA,XpFormulaC,DateAdded) VALUES ($G,9,27,'$NOW');
SQL
XPS=$(sqlite3 "$DB" "SELECT Id FROM XpSettings WHERE GuildId=$G;")
sqlite3 "$DB" <<SQL
INSERT INTO XpRoleReward (XpSettingsId,Level,RoleId,Remove,DateAdded) VALUES
 ($XPS,10,$R_REGULAR,0,'$NOW'),
 ($XPS,25,$R_LOCAL,0,'$NOW'),
 ($XPS,50,$R_FIXTURE,0,'$NOW'),
 ($XPS,100,$R_LIVES,0,'$NOW');
INSERT INTO XpCurrencyReward (XpSettingsId,Level,Amount,DateAdded) VALUES
 ($XPS,5,25,'$NOW'),
 ($XPS,10,50,'$NOW'),
 ($XPS,25,150,'$NOW'),
 ($XPS,50,300,'$NOW'),
 ($XPS,100,750,'$NOW');
SQL

echo "== shop entries (7 cosmetic roles @200) =="
sqlite3 "$DB" <<SQL
DELETE FROM ShopEntry WHERE GuildId=$G;
INSERT INTO ShopEntry (GuildId,Index,Price,Name,AuthorId,Type,RoleName,RoleId,DateAdded) VALUES
 ($G,1,200,'Night Shift',$BOT,0,'Night Shift',$R_NIGHT,'$NOW'),
 ($G,2,200,'Side Quest',$BOT,0,'Side Quest',$R_SIDE,'$NOW'),
 ($G,3,200,'Background Character',$BOT,0,'Background Character',$R_BG,'$NOW'),
 ($G,4,200,'Offline',$BOT,0,'Offline',$R_OFF,'$NOW'),
 ($G,5,200,'Probably Fine',$BOT,0,'Probably Fine',$R_FINE,'$NOW'),
 ($G,6,200,'Houseplant',$BOT,0,'Houseplant',$R_HOUSE,'$NOW'),
 ($G,7,200,'Local Cryptid',$BOT,0,'Local Cryptid',$R_CRYPT,'$NOW');
SQL

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
# index 0: allow all
sqlite3 "$DB" "INSERT INTO Permissions (GuildId,Index,PrimaryTarget,PrimaryTargetId,SecondaryTarget,SecondaryTargetName,IsCustomCommand,State,GuildConfigId,DateAdded) VALUES ($G,0,3,0,2,'*',0,1,$GC,'$NOW');"
IDX=1
# module denies for @everyone (role id == guild id)
for m in Waifus; do
  sqlite3 "$DB" "INSERT INTO Permissions (GuildId,Index,PrimaryTarget,PrimaryTargetId,SecondaryTarget,SecondaryTargetName,IsCustomCommand,State,GuildConfigId,DateAdded) VALUES ($G,$IDX,2,$G,0,'$m',0,0,$GC,'$NOW');"
  IDX=$((IDX+1))
done
# command denies for @everyone
CMDS="vote clubtransfer clubadmin clubcreate clubicon clubbanner clubinfo clubbans clubapps clubapply clubaccept clubreject clubleave clubkick clubban clubunban clubdesc clubdisband clublb clubrename betroll luckyladder bettest rakeback race joinrace betstats winlb gamblestats betstatsreset gamblestatsreset blackjack hit stand double betdraw betflip slot pick plant gencurrency gencurlist anime anilist manga pokemon pokemonability patron patrons patronmessage donate"
for c in $CMDS; do
  sqlite3 "$DB" "INSERT INTO Permissions (GuildId,Index,PrimaryTarget,PrimaryTargetId,SecondaryTarget,SecondaryTargetName,IsCustomCommand,State,GuildConfigId,DateAdded) VALUES ($G,$IDX,2,$G,1,'$c',0,0,$GC,'$NOW');"
  IDX=$((IDX+1))
done
# admin allow-all override at highest index (for each admin role)
for ar in 1556922117411307651 1556933611566010381; do
  sqlite3 "$DB" "INSERT INTO Permissions (GuildId,Index,PrimaryTarget,PrimaryTargetId,SecondaryTarget,SecondaryTargetName,IsCustomCommand,State,GuildConfigId,DateAdded) VALUES ($G,$IDX,2,$ar,2,'*',0,1,$GC,'$NOW');"
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

echo "== feed: youtube uploads -> #recent-uploads =="
sqlite3 "$DB" <<SQL
DELETE FROM FeedSub WHERE GuildId=$G;
INSERT INTO FeedSub (GuildId,ChannelId,Url,Message,DateAdded) VALUES
 ($G,$UPLOADS,'https://www.youtube.com/feeds/videos.xml?channel_id=UC6luqCLxAE3J_iyNvkcZasA','NEW UPLOAD
{0}
{1}','$NOW');
SQL

echo "== set bot nickname Juniper =="
curl -s -X PATCH "$API/guilds/$G/members/@me" -H "$AUTH" -H "Content-Type: application/json" -d '{"nick":"Juniper"}' -o /dev/null -w "nick HTTP %{http_code}\n"

echo "== start =="; docker start nadeko >/dev/null; echo started
