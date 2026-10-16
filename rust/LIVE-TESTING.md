# Live testing the Rust servers

How to run the Rust login + game servers against a real MySQL, the real 1.2 game data and a
headless client bot, how to compare their traffic with the C# servers (parity), how to load-test
them, and how to start the real 1.2 client against them (`aaemu-launcher`).

Everything below uses the layout of the reference run (Windows, Git Bash). Paths are examples:

| What | Where (example) |
|------|-----------------|
| worktree | `D:\aaemu-wt\live` |
| cargo target dir | `D:\aaemu-target\live` (`rust/.cargo/config.toml`) |
| runtime root (MySQL data, configs, logs, dotnet output) | `D:\aaemu-live` |
| game data | `rust/crates/aaemu-game/app/Data/compact.sqlite3` (+ `Data/*` json/xml) |
| client | `D:\AA1.2` (`game_pak` is read, never written) |

Keep large runtime files off a nearly full system drive: the MySQL datadir, `dotnet build
--artifacts-path` output and logs all go under the runtime root.

## 1. Private MySQL

Use a separate instance; never point tests at a MySQL that holds real data.

```sh
MYSQLD="/c/Program Files/MySQL/MySQL Server 8.0/bin/mysqld.exe"
MYSQL="/c/Program Files/MySQL/MySQL Server 8.0/bin/mysql.exe"
"$MYSQLD" --no-defaults --initialize-insecure --datadir=D:/aaemu-live/mysql/data --console
cat > D:/aaemu-live/mysql/my.ini <<'EOF'
[mysqld]
datadir=D:/aaemu-live/mysql/data
port=3317
bind-address=127.0.0.1
mysqlx=0
max_connections=500
log-error=D:/aaemu-live/logs/mysqld.err
default-authentication-plugin=mysql_native_password
character-set-server=utf8mb4
EOF
"$MYSQLD" --defaults-file=D:/aaemu-live/mysql/my.ini &      # background
"$MYSQL" -h127.0.0.1 -P3317 -uroot -e "CREATE USER 'aaemu'@'127.0.0.1' IDENTIFIED BY '<pw>';
  GRANT ALL ON *.* TO 'aaemu'@'127.0.0.1';"
```

`root` has no password (`--initialize-insecure`) and the instance only listens on loopback.
Keep the `aaemu` password in a git-ignored local file (e.g. `D:\aaemu-live\cfg\`).

Databases: one login + one game database per server implementation, so the C# and Rust servers
can run side by side.

```sh
for side in rs cs; do for k in login game; do
  "$MYSQL" -h127.0.0.1 -P3317 -uroot -e "CREATE DATABASE ${side}_$k CHARACTER SET utf8mb4"
  # the first two lines of the scripts select the `aaemu_*` schema
  tail -n +3 SQL/aaemu_$k.sql | sed 's/CREATE TRIGGER IF NOT EXISTS/CREATE TRIGGER/' \
    | "$MYSQL" -h127.0.0.1 -P3317 -uroot ${side}_$k
done; done
```

The `sed` is only needed for MySQL < 8.0.29 (no `CREATE TRIGGER IF NOT EXISTS`). The base
scripts already contain every `SQL/updates` change; both servers create the `updates` table on
first start and mark all scripts as installed (`AutoApplyUpdates` in the configs below applies
later ones without a prompt).

Stop: `"$MYSQL" -h127.0.0.1 -P3317 -uroot -e "SHUTDOWN"`.

## 2. Servers

Ports used side by side (C# = Rust + 10000):

| | Rust | C# |
|---|---|---|
| login (clients) | 21237 | 31237 |
| login (game servers) | 21234 | 31234 |
| game | 21239 | 31239 |
| stream | 21250 | 31250 |
| web api | 21280 | 31280 |

### Rust

```sh
cd rust && cargo build --release --bins    # or --profile live: opt-level 1 + incremental, for fast rebuilds
```

`aaemu-login` reads `Config.json` + `Config.Local.json` from its application directory;
`aaemu-game [APP_PATH]` reads `Config.json`, `Configurations/*.json`, `Config.Local.json` and
`Data/compact.sqlite3` from its application directory (`APP_PATH` when given). The application
directory is `AAEMU_APP_PATH`, else the exe / current directory when it holds a `Config.json`,
else the crate's `app/` folder (`rust/crates/aaemu-{login,game}/app`). A runtime directory per
server (run from the repository root):

```sh
mkdir -p D:/aaemu-live/rs-login D:/aaemu-live/rs-game
cp rust/crates/aaemu-login/app/Config.json D:/aaemu-live/rs-login/
cp -r rust/crates/aaemu-game/app/{Config.json,Configurations,Data} D:/aaemu-live/rs-game/
```

(`cargo xtask publish` builds the same per-server folders, see the repository README.)

`D:/aaemu-live/rs-login/Config.Local.json`:

```json
{
  "SecretKey": "livetest",
  "AutoAccount": true,
  "SkipHostResolve": true,
  "InternalNetwork": { "Host": "127.0.0.1", "Port": 21234 },
  "Network": { "Host": "127.0.0.1", "Port": 21237, "NumConnections": 100 },
  "Connections": { "AutoApplyUpdates": true, "MySQLProvider": { "Database": "rs_login",
    "Host": "127.0.0.1", "Port": "3317", "User": "aaemu", "Password": "<pw>" } },
  "GameServers": [ { "Id": 1, "Name": "LiveTest", "Host": "127.0.0.1", "Port": 21239, "Hidden": false } ]
}
```

`D:/aaemu-live/rs-game/Config.Local.json`:

```json
{
  "Id": 1,
  "SecretKey": "livetest",
  "Network": { "Host": "127.0.0.1", "Port": 21239, "NumConnections": 100 },
  "StreamNetwork": { "Host": "127.0.0.1", "Port": 21250 },
  "WebApiNetwork": { "Host": "127.0.0.1", "Port": 21280 },
  "LoginNetwork": { "Host": "127.0.0.1", "Port": "21234" },
  "Connections": { "AutoApplyUpdates": true, "MySQLProvider": { "Database": "rs_game",
    "Host": "127.0.0.1", "Port": "3317", "User": "aaemu", "Password": "<pw>" } },
  "ClientData": { "Sources": [ "D:/AA1.2/game_pak" ] }
}
```

Start (login first; the game server registers with it once its data is loaded):

```sh
(cd D:/aaemu-live/rs-login && NO_COLOR=1 RUST_LOG=info $T/aaemu-login.exe > ../logs/rs-login.log 2>&1 &)
(cd D:/aaemu-live/rs-game && NO_COLOR=1 RUST_LOG=info $T/aaemu-game.exe D:/aaemu-live/rs-game > ../logs/rs-game.log 2>&1 &)
grep "Successfully registered on LoginServer" D:/aaemu-live/logs/rs-game.log
```

(`$T` = `D:/aaemu-target/live/release`.) The game log warns once about every `ClientData`
source that does not exist (the defaults in `Configurations/ClientData.json`), about three
unparsable `npc_ai_params` (same in C#) and about the `shipyards` id table (same in C#). Handlers
slower than 250 ms are logged as `Slow packet handler: ...`.

### C#

The C# projects were removed from the repository in `a2ab80abd`; for parity runs check out the
commit before it into a separate worktree (`git worktree add D:/aaemu-wt/cs a2ab80abd^`) and build
`AAEmu.Login/` and `AAEmu.Game/` there with the .NET SDK (only needed for these parity runs):

```sh
dotnet build AAEmu.Login/AAEmu.Login.csproj -c Release --artifacts-path D:/aaemu-live/dotnet
dotnet build AAEmu.Game/AAEmu.Game.csproj  -c Release --artifacts-path D:/aaemu-live/dotnet
```

Put the same `Config.Local.json` files (ports +10000, databases `cs_login` / `cs_game`) next to
`D:/aaemu-live/dotnet/bin/AAEmu.{Login,Game}/release/AAEmu.*.exe` and start the exes from those
directories.

### Measured (reference machine, warm file cache)

| | Rust | C# |
|---|---|---|
| login: start to listening | 0.05 s | 1 s |
| game: start to registered | 17-25 s (40 s cold) | 57-95 s (134 s cold) |
| game: working set after start + a few sessions | ~580-590 MB | ~3.1-4.3 GB |
| game: working set after 250 bot sessions | ~615 MB | ~5.7 GB |
| login: working set | ~15 MB | ~80 MB |

Both load 13382 NPC / 7124 doodad / 27036 item templates, 4775 quests, 42649 + 1196 doodads,
69 transfers, 14 gimmicks, 5 slaves, 95 fish schools and the `arche_mall_world` system dungeon.

## 3. The bot: `aaemu-testclient`

A headless 1.2 (r208022) client speaking the real wire protocol:

* login: `CARequestAuthTrion` (the launcher's XML ticket with the user name and the hex SHA-256
  of the password; `AutoAccount` creates the account), `CAListWorld`, `CAEnterWorld` ->
  `ACWorldCookie`;
* game: `X2EnterWorld`, the `FinishState` 0..7 burst, `CSListCharacter`, `CSCreateCharacter`
  (Nuian fighter), `CSSelectCharacter`, `CSSpawnCharacter`, `CSNotifyInGame(Completed)`,
  `CSMoveUnit`, `CSSendChatMessage`, `CSStartSkill` (melee on an NPC it walked up to),
  `CSSwapItems` (bag slot 0 -> 49), `CSStartQuestContext` (quest 330 from NPC 3597 next to the
  Nuian spawn), `CSLeaveWorld` and the 10 s `SCLeaveWorldGranted`; it pings every 3 s (servers
  drop sessions without a `PingPacket` for 30 s);
* stream: `CTJoin` with the token from `X2EnterWorldResponse`.

Every step asserts the expected answer (e.g. `ACAuthResponse`, `ChangeState(n+1)` for each
`FinishState(n)`, `SCCreateCharacterResponse`, our own `SCUnitState`, the chat echo,
`SCSkillStarted`, `SCItemTaskSuccess`) and records every frame in both directions.

Settings come from a JSON file (`crates/aaemu-testclient/testclient.example.json`; keep the real
one as `*.local.json`, which is git-ignored) and flags:

```sh
TC=$T/aaemu-testclient.exe
$TC run    --config testclient.local.json --login 127.0.0.1:21237 --scenario full --out run.jsonl
$TC parity --config testclient.local.json --a 127.0.0.1:31237 --b 127.0.0.1:21237 \
           --scenario full --rounds 2 --masks rust/crates/aaemu-testclient/parity.json --out-dir parity
$TC diff   --a parity/full-r1-cs.jsonl --b parity/full-r1-rs.jsonl --masks .../parity.json
$TC load   --config testclient.local.json --login 127.0.0.1:21237 --bots 50 --prefix load --ramp-ms 20
```

Scenarios: `login` (login server only), `lobby` (+ game lobby, stream join, character
creation, quit), `full` (everything above), `combat` (`full` + three melee hits on a Solzreed
fox, which fights back; logout then takes the 5-minute in-combat timer), `load` (login,
create, select, spawn, move, chat, leave). `parity` exits with 2 when there are findings,
`load` when a bot failed.

`questitem` (`full`'s login/create/select/spawn, then GM setup and a quest-item cast loop) uses
the Detection Orb (item 17594, use skill 16881) of quest 2394 "Searching for Answers" on the
Soulreath "Field of Honor Participant" / "Fighter Participant" NPCs (7824 / 7823) three times and
asserts that the quest objective counts 3; a rejected cast fails the step with its `SkillResult`
(93 = `skill_urk_target_npc_group`). Setup: `/move`, `/quest add 2394 Npc 511`,
`/quest step 2394 Progress` (the start step also needs an Elf who completed quest 2392, which the
Nuian bot is not) and `/item add`. Prerequisite: the bot's account needs GM access level 100;
after the first run created the account, grant it once on the private MySQL (never 3306):
`mysql -h127.0.0.1 -P3317 -uroot -e "UPDATE rs_game.accounts SET access_level=100 WHERE account_id=<id>"`
(the id is in the `login.auth` step note and in the error of `game.questitem.gm`; the schema name
is the one of the game server's `Connections`). Fields (`quest_item_quest`, `quest_item_giver`,
`quest_item_template`, `quest_item_skill`, `quest_item_npcs`, `quest_item_uses`,
`quest_item_pos`, `quest_item_max_attempts`) default to those values.

`doodad` (same login and GM prerequisites as `questitem`) adds quest 3447 "Fellow Searchers"
(`/quest add 3447 Npc 7890`, `/quest step 3447 Progress`), teleports next to the Amber Boulder
(doodad 4252, Arach Hollow) and lists the doodads in sight. It casts the boulder's fake-use skill
16790 on it with a doodad target and asserts that the quest objective counts 1. A boulder missing
from the level spawn fails the `game.doodad.level_spawn` step. The scenario then interacts with a
`/spawn doodad 4252` copy, so the cast is still checked, and removes that copy afterwards.

The combat step selects its NPC through `CSChangeTargetPacket` and faces it in movement
packets so auto attacks and angle restrictions use the intended target. It matches skill responses to the player's object and skill IDs and reads
the 1.2 `SkillResult` extra field before optional integer data. Missing combat NPCs and
rejected casts fail the step; an NPC's own cast notification cannot count as a player cast.

Run `login` last in a parity session: a world cookie that is never used leaves a pending entry
on the game server, which makes the C# game server stop answering the login server (fixed in
the Rust server, see `aaemu-game/PORTING.md`).

## 4. Parity with the C# servers

Both servers must start from the same state so that account / character / item ids line up:

```sh
# stop all four servers, then
MYSQL="$MYSQL" PORT=3317 rust/crates/aaemu-testclient/scripts/reset-live-db.sh rs_login rs_game cs_login cs_game
# start all four, wait for both "Successfully registered on LoginServer", then
$TC parity ... --rounds 2
```

The diff compares the S->C packets step by step (names and counts, then bodies). Before
comparing it normalises what differs by construction (`parity.json`):

* run variables (account id, cookie, character id, item id, ...) recorded by the bot;
* object ids: every unit / doodad id is replaced by a hash of what it is (kind, template, a 30 m
  grid around where it was first seen; doodad records are rewritten field by field);
* item ids (`0x01xxxxxx`), renumbered in order of appearance;
* unix times (+-30 days), the buff `elapsedTime` of unit states, doodad growth timers, the
  session token, configured ports, random character guids;
* packets driven by AI / timers are compared by count only or not at all (NPC movement,
  posture, aggro, time of day, pongs); NPC / doodad appearance is compared over the whole run
  because region activation timing differs.

`combat` is diffed with `parity-combat.json`: damage, hit / miss and the 10 % back-attack
slow proc are random, so the combat packets (`SCSkillFired`, `SCUnitDamaged`, the `DD04`
`CompressedGamePackets` batches, aggro, unit points, buff create / remove) are compared by
layout only (body lengths).

Results of the final reference run (fresh databases, both servers started together):

| scenario | S->C packets | identical after normalisation | findings |
|---|---|---|---|
| `lobby` | 27 | 27 | 0 |
| `full`, round 1 (new character) | 1330 | 990 (rest: AI / timer driven, compared by count) | 0 |
| `full`, round 2 (same character, new session) | 1499 | 1398 | 10, all explained below |
| `combat` | 2247 | 1976 | 4, NPC identity only |
| `login` | 4 | 4 | 0 |

Explained differences: round 2 starts from the position the C# saved with reduced precision
(PORTING.md "Live testing"), which shifts our own unit state / character list positions; the
saved remaining time of the auction-license buff differs by the time the runs took; and wandering
NPCs of the same template can swap their normalised identities (the 30 m grid key), which shows
up as `SCUnitState` / `SCUnitPoints` bodies of the "wrong" fox being compared.

Any other finding is a porting bug until explained: decode both bodies (the capture is JSON
lines with hex bodies) and compare with the C# packet `Write`.

## 5. Load

Reference results (`load` scenario, bots and servers on the same machine; `game.spawn`,
`game.move`, `game.select`, `game.list` and `game.leave` include the bot's fixed drains and the
server's 10 s logout timer):

| step (p50 / p95 ms) | Rust, 50 bots | C#, 50 bots | Rust, 200 bots | C#, 200 bots |
|---|---|---|---|---|
| login.auth | 15 / 26 | 49 / 245 | 68 / 500 | all failed |
| game.enter | 15 / 23 | 1851 / 2874 | 57 / 203 | - |
| game.create | 21 / 31 | 48 / 224 | 245 / 643 | - |
| game.chat (echo) | 0 / 2 | 1 / 2 | 2 / 23 | - |
| bots ok | 50 / 50 | 50 / 50 | 200 / 200 | 0 / 200 |

With 200 concurrent logins the C# login server times out on its MySQL connection pool
(`Timeout expired ... obtaining a connection from the pool`) and drops every attempt.

## 6. The real 1.2 client: `aaemu-launcher`

* Client: 1.2 r208022 (see `Docs/wiki/Client.md`); the server reads its `game_pak` for data. The
  client folder is only read; nothing in it has to be changed.
* The client does not ask for credentials: a launcher hands it an authentication ticket.
  `aaemu-launcher` (`rust/crates/aaemu-launcher`) does that exactly like the Trion 1.2 launcher
  of the public AAEmu Launcher, so no third-party tool is needed:

  ```sh
  $T/aaemu-launcher.exe --client 'D:\AA1.2\bin32\archeage.exe' --login 127.0.0.1:21237 \
      --user clienttest --password-file D:/aaemu-live/cfg/client-test-password.txt
  ```

  (`--password PW`, `$AAEMU_PASSWORD` or a prompt instead of `--password-file`; `--lang en_us`
  is the default, `--nosplash`, `--arg X` for more client arguments, `--wait` to stay until the
  client exits, `--dry-run` to only print the command line.) With `AutoAccount` the login server
  creates the account on its first login.
* Protocol (unit tested in `aaemu-launcher/src/lib.rs`):
  * ticket XML `<?xml version="1.0" encoding="UTF - 8" standalone="yes"?><authTicket version =
    "1.2"><storeToken>1</storeToken><username>U</username><password>H</password></authTicket>`
    with `H` = lower-case hex SHA-256 of the password (the odd spacing is the public launcher's;
    the login server parses it);
  * plain text `TFIR` + `dGVzdA==` (signature, unchecked) + `\n` + XML, RC4-encrypted with a
    random 8-byte key;
  * an inheritable page-file mapping `archeage_auth_ticket_map` holding `key[8] | u32 LE length
    | ciphertext`, and an inheritable manual-reset event `archeage_auth_ticket_event`;
  * `archeage.exe -t +auth_ip HOST -auth_port PORT -handle MAP:EVENT -lang en_us +acpxmk`
    (handles as 8 upper-case hex digits), started with `CreateProcessW(bInheritHandles=TRUE)`
    in the `bin32` folder. The client reads the ticket, signals the event (the launcher reports
    "client signalled the ticket event") and sends the XML in `CARequestAuthTrion`.
* The login server hands the game server address from `GameServers[].Host`/`Port` to the client
  (`ACWorldCookie`); when the client connects from the login server's own machine its own
  address is used instead. For LAN / remote clients set `Host` to an address the client can
  reach and listen on it (`Network.Host` `*` or that address) for the login (21237), game
  (21239) and stream (21250) ports.
* The game server drops clients that send no `PingPacket` for 30 s; the real client pings on its
  own (several times a second in the lobby).
* `RUST_LOG=info,aaemu_game::net=debug,aaemu_login::network=debug` logs every game packet
  (`GamePacket: C->S ...` / `S->C ...`) and the login connections, which is the server-side trace
  of a client session.

Verified with the real client (Windows 11, fullscreen 1920x1080, both servers release builds):
launcher ticket accepted (`clienttest connected.`), server list (`LiveTest`, character count),
`X2EnterWorld` + `FinishState` lobby burst, `CSListCharacter`, character creation through the UI
(`CSCreateCharacter` -> `SCCreateCharacterResponse`), `CSSelectCharacter`, `CSSpawnCharacter`,
`CSNotifyInGame(Completed)` in Solzreed, walking (`CSMoveUnit`), chat (`CSSendChatMessage` and
its echo shown in the chat window and as a bubble), *Select Character* (`CSLeaveWorld`,
`SCLeaveWorldGranted` after 10 s, back in the lobby with the saved character), *Exit Game*, and a
second session entering the world again at the saved position. No server errors.

## 7. Stopping

Stop the servers (Ctrl+C, or `taskkill //IM aaemu-game.exe //IM aaemu-login.exe //IM
AAEmu.Game.exe //IM AAEmu.Login.exe`), then shut the private MySQL down (`SHUTDOWN` above).
