# aaemu-game porting status

Reference: `AAEmu.Game` at upstream `develop` 6e2f40734 (1.2 client, `client_12_r208022`).
Status: **done** = ported for its current C# scope, **partial** = only what the lobby / character
list needs, **todo** = not started.

## 1.2 lobby flow (what phase 1 covers)

```
login  --LGPlayerEnter(acc, connId)-->  game: EnterWorldManager.AddAccount -> GLPlayerEnter(connId, gsId, 0|1)
client --X2EnterWorld(lvl1, cookie)-->  game: Login(token check) -> AccountManager.Add, StreamManager.AddToken
                                         <-- X2EnterWorldResponse(lvl1) + ChangeState(0) (lvl2)
client --FinishState(0)(lvl2)------->   <-- ChangeState(1), SetGameType("w_hanuimaru_1"), SCInitialConfig,
                                             SCTrionConfig, SCAccountInfo, SCChatSpamDelay,
                                             SCAccountAttributeConfig, SCLevelRestrictionConfig
client --FinishState(1..6)---------->   <-- ChangeState(state+1);  FinishState(7) -> SCUpdatePremiumPoint
client --CSListCharacter(lvl1)------>   <-- SCGetSlotCount, SCAccountInfo, [LoadAccount], SCRaceCongestion,
                                             SCCharacterList x ceil(n/2), (SCLoginCharInfoHouse per house: todo)
client --CSLeaveWorld(ServerSelect)->   game -> GLPlayerReconnect;  LGPlayerReconnect -> SCReconnectAuth
```

Framing: level 1 = `[dd 01][hash][count][u16 op][body]` (server writes 0/0 and never verifies the
client's bytes, as in C#), level 2 = `[dd 02][u16 op][body]`. Any other level has no packet table:
the C# lookup throws and the connection is shut down. There is no stream encryption in 1.2.

## Subsystems

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `Core/Network/Game` (GameNetwork, GameProtocolHandler, GamePacket) | `net/` | done | Levels 1/2 framing, registry keyed by (level, opcode), `0xFFF` packets dropped on send. |
| `Core/Network/Connections` (GameConnection, GameConnectionTable, GameState) | `net/connection.rs` | partial | No ActiveChar / Houses / subscribers / session attributes yet. |
| `Core/Network/Login` + `Packets/G2L`, `Packets/L2G` | `login/` | done | All 5 G2L and 4 L2G packets (byte-identical), LoadTask, reconnect loop. |
| `Core/Network/Stream` + `Packets/C2S`, `Packets/S2C` (stream server) | – | todo | Only the StreamManager token map exists (`managers/stream.rs`). |
| `Core/Packets/C2G` | `packets/c2g.rs` | partial | X2EnterWorld, CSLeaveWorld (lobby path), CSListCharacter. ~250 others todo; full opcode table in `opcodes.rs`. |
| `Core/Packets/G2C` | `packets/g2c.rs` | partial | Every packet of the lobby flow (12) except `SCLoginCharInfoHouse`. |
| `Core/Packets/Proxy` | `packets/proxy.rs` | done | All registered proxy packets (most are inert in C# too). |
| `Core/Packets/CompressedGamePackets`, `Debug` | – | todo | |
| `Core/Managers/World/EnterWorldManager` | `managers/enter_world.rs`, `packets/c2g.rs` | partial | AddAccount, Login, Leave(ServerSelect). Leave(QuitGame/CharacterSelect) from the world and LeaveWorldTask todo. |
| `Core/Managers/AccountManager` | `managers/account.rs`, `db.rs` | partial | Add/Remove/Contains/Count, GetAccountDetails, UpdateLoginTime. Credits/labor/loyalty updates, dead-connection sweep todo. |
| `Core/Managers/FeaturesManager`, `Models/Game/Features` | `data/features.rs` | done | |
| `Core/Managers/ExperienceManager` (+ `SqliteExperienceLevelTemplateLoader`) | `data/experience.rs` | partial | Loader validation, level caps, GetExpForLevel, GetLevelFromExp. Mate/skill-point/exp-loss helpers todo. |
| `Core/Managers/ItemManager` | `models/item.rs`, `db.rs`, `data/mod.rs` | partial | Equipment rows of one character + `bind_id`/`fixed_grade`/`gradable`. Templates, containers, loot, timers todo. |
| `Core/Managers/UnitManagers/CharacterManager` | `db.rs` | partial | LoadCharacters (login info). Create/delete/select, deletion finalisation todo. |
| `Core/Managers/World/StreamManager` | `managers/stream.rs` | partial | Tokens only. |
| `Core/Managers/TimedRewardsManager` | – | todo | Daily login + offline labor on enter (called from AccountManager.Add). |
| `Core/Managers/HousingManager`, `Models/Game/Housing` | – | todo | Needed for `Connection.Houses` / `SCLoginCharInfoHouse` in CSListCharacter. |
| `Core/Managers/World/FactionManager`, `ExpeditionManager` | `db.rs` | partial | Faction id written as stored (or Pirate, see below); expedition existence check only. |
| `Core/Managers/CrimeManager` | `db.rs` | partial | Load-time CrimePoint/InfamyPoint clamps and the pirate threshold of CheckWantedThreshold. |
| `Core/Managers/World/*` (World, Zone, Spawn, AreaTrigger, Physics, Specialty, SphereQuest) | – | todo | |
| `Core/Managers/Id/*` (id managers) | – | todo | Session ids come from a counter. |
| `Core/Managers/Stream/UccManager` | – | todo | |
| Other `Core/Managers/*` (AI, AccessLevel, Animation, Auction, CashShop, Chat, Command, Craft, Duel, EffectTask, ExpressText, Family, FishSchool, Formula, Friend, GameSchedule, Gimmick, Indun, InstantGame, Localization, Mail, ManaRegen, Mate, Model, Music, Name, Plot, Portal, PublicFarm, Quest, Radar, Save, Shipyard, Skill, Slave, SubZone, Sus, Task/Tick/Time, Taxations, Team, Trade, Trial) | – | todo | |
| `Models/Game/Char` (Character, Inventory, ...) | `models/character.rs`, `db.rs` | partial | `Character.Load` (all columns checked) + `Character.Write`; ValidateAndFixExpAndLevel ported; no HP/MP clamp to MaxHp/MaxMp (needs formulas/stats). |
| `Models/Game/Units/UnitCustomModelParams` | `models/unit_model.rs` | done | |
| `Models/Game/Items` | `models/item.rs` | partial | Item/EquipItem/... persistence + `Write`; no gameplay logic. |
| `Models/AccountPayment`, `Models/Account` | `net/connection.rs`, `db.rs` | done | |
| `Models/AppConfiguration`, `Models/Game/Configurations` + `Program` config loading | `config.rs` | partial | Config.json + Configurations/** + Config.Local.json merge; only the sections used so far. No env/CLI/user-secrets. |
| Other `Models/Game/*` (AI, Achievement, Auction, Chat, Crafts, Crime, DoodadObj, Expeditions, Faction, Mails, Mate, NPChar, Quests, Skills, Slaves, Team, World, ...) | – | todo | |
| `GameData/Framework` + `GameData/*GameData` loaders | `data/mod.rs` | todo | Only `levels` and `items` of `compact.sqlite3` are read (failures are fatal, like C#). |
| `Scripts/Commands`, `Utils/Scripts` | – | todo | |
| `Physics/*`, `Core/Managers/World/PhysicsManager` | – | todo | |
| `IO` (ClientFileManager, client paks), `ClientData` | – | todo | |
| `Services` (WebApi, DiscordBot) | – | todo | |
| `GameService` startup / `ManagerOrchestrator` | `main.rs` | partial | MySQL check, game data (fatal on failure), login link, game listener. |

## Known deviations from the C#

* `LGPlayerEnter` for an already registered connection id: C# `Dictionary.Add` throws inside the
  login handler, which has no catch (the exception escapes into the network client). Rust logs it,
  sends nothing (same as C#) and keeps the link up.
* `Character.Load` for a character with InfamyPoint >= 3000 that is not yet a pirate: listed with
  the Pirate faction as in C#. The C# also kicks it from its guild via
  `ExpeditionManager.Kick(this.Connection, ...)`, which dereferences the null lobby connection and
  aborts `CSListCharacter`; that crash and the buff/house side effects are not reproduced.
* `CheckForDeletedCharactersDeletion`: characters whose delete time has passed are hidden from the
  list, but the finalisation (rename, `deleted = 1`, asset deletion, `SCCharacterDeleted`) is not
  done yet.
* `GameConnection.OnDisconnect` → `UpdateLoginTime(0)` for connections that never entered (inserts
  an `accounts` row with id 0 in C#) is skipped.
* Session ids are a counter, not `RemoteEndPoint.GetHashCode()`.
