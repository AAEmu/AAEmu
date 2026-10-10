# aaemu-game porting status

Reference: `AAEmu.Game` at upstream `develop` 6e2f40734 (1.2 client, `client_12_r208022`).
Status: **done** = ported for its current C# scope, **partial** = only what the lobby / world
entry / movement path needs, **todo** = not started.

Layout rules, the object model API and testing conventions: `CONTRIBUTING-PORT.md`.

## 1.2 lobby flow (phase 1)

```
login  --LGPlayerEnter(acc, connId)-->  game: EnterWorldManager.AddAccount -> GLPlayerEnter(connId, gsId, 0|1)
client --X2EnterWorld(lvl1, cookie)-->  game: Login(token check) -> AccountManager.Add, StreamManager.AddToken
                                         <-- X2EnterWorldResponse(lvl1) + ChangeState(0) (lvl2)
client --FinishState(0)(lvl2)------->   <-- ChangeState(1), SetGameType("w_hanuimaru_1"), SCInitialConfig,
                                             SCTrionConfig, SCAccountInfo, SCChatSpamDelay,
                                             SCAccountAttributeConfig, SCLevelRestrictionConfig
client --FinishState(1..6)---------->   <-- ChangeState(state+1);  FinishState(7) -> SCUpdatePremiumPoint
client --CSListCharacter(lvl1)------>   <-- SCGetSlotCount, SCAccountInfo, [LoadAccount: Character.Load +
                                             CheckForDeletedCharactersDeletion -> SCCharacterDeleted +
                                             SCDeleteCharacterResponse(1)], SCRaceCongestion,
                                             SCCharacterList x ceil(n/2), SCLoginCharInfoHouse per house
client --CSLeaveWorld(ServerSelect)->   game -> GLPlayerReconnect;  LGPlayerReconnect -> SCReconnectAuth
```

## Character lifecycle and world (phase 2)

```
lobby:
client --CSCreateCharacter------->  NameManager.Validate -> SCCharacterCreationFailed(3|4|5)
                                    | CharacterManager.Create: template, starting kit (equip pack + body
                                    |   parts -> Equipment, supplies -> Bag), action slots, abilities,
                                    |   actabilities, default/start skills, MaxHp/MaxMp,
                                    |   SaveDirectlyToDatabase -> SCCreateCharacterResponse | SCCharacterCreationFailed(3)
client --CSDeleteCharacter------->  SetDeleteCharacter -> SCDeleteCharacterResponse(2|0) | SCErrorMsg; CharacterDeleteTask
client --CSCancelCharacterDelete->  SetRestoreCharacter -> SCCancelCharacterDeleteResponse(3|4)

enter world:
client --CSSelectCharacter------->  Character.Load() (abilities, actabilities, skills, appellations, portals,
                                    friends, blocked, quests, slots), ObjId (UsedCharacterObjIds), WorldManager.TryAddCharacter
                                    <-- SCCharacterState, SCCharacterGamePoints, SCCharacterInvenInit,
                                        SCCharacterInvenContents x (bag/10 + bank/10), SCActionSlots, SCQuests,
                                        SCCompletedQuests, SCActability, SCCountUnreadMail, [SCAppellations],
                                        [SCCharacterPortals], [SCCharacterReturnDistricts], [SCFriends], [SCBlockedUsers],
                                        SCConflictZoneState per conflict, SCFactionList x n (or the 0x007 empty one),
                                        SCFactionRelationList x n (or the 0x008 empty one), SCFactionList (guilds),
                                        SCExpeditionRolePolicyList([]), [guild policies + members],
                                        SCResponseUIData(1/2/5)
                                    + LoggedOn / template / saved buffs, CheckWantedThreshold, RestoreSavedHpMp,
                                      OnZoneChange(0, zone) (-> SCExpeditionMemberStatusChanged to the guild)
client --CSSpawnCharacter-------->  State = World, visual options <-- SCUnitState(self), SCDetailedTimeOfDay
                                    (TimeOfDayObserver: SCTimeOfDay every 10 s)
client --CSNotifyInGame---------->  IsOnline (SCFriendStatusChanged to friends), Character.Spawn:
                                    region add -> others: SCUnitState + SCUnitPoints both ways (2-region neighbourhood)
                                    <-- SCJoinedChatChannel (zone group, nation, faction; guild)
client --CSNotifyInGameCompleted->  <-- SCOnOffSnow

in world:
client --CSMoveUnit(UnitMoveType)-> position update -> SCOneUnitMovement to neighbours + self (first move: MOTD),
                                    region change -> SCUnitsRemoved / SCUnitState+SCUnitPoints for the diff regions,
                                    zone change -> zone chat leave/join
client --CSSendChatMessage-------> say: SCChatMessage to neighbours + self; shout/trade/lfg: zone channel;
                                    whisper / clan / family / nation / faction channels; party/raid -> SCErrorMsg
client --CSLeaveWorld(0|1)-------> <-- [system logout message], SCPrepareLeaveWorld(10000);  CSCancelLeaveWorld ->
                                    SCLeaveWorldCanceled;  after 10 s LeaveWorldTask: chat leave, guild status,
                                    Delete (SCUnitsRemoved to neighbours), save <-- SCLeaveWorldGranted, ChangeState(0)
disconnect                    ---> OnDisconnect: offline status, SaveAndRemoveFromWorld

server: SaveManager every World.AutoSaveInterval minutes (items + online characters), final save on shutdown.
```

Framing: level 1 = `[dd 01][hash][count][u16 op][body]` (server writes 0/0 and never verifies the
client's bytes, as in C#), level 2 = `[dd 02][u16 op][body]`. Any other level has no packet table:
the C# lookup throws and the connection is shut down. There is no stream encryption in 1.2.

Sending: `ConnectionHandle::send` encodes and queues on the connection's writer immediately, so
packets sent to a player from another connection's handler keep the C# call order relative to the
packets its own handler sends.

## Object model (phase 3a)

All world objects share one model (details in `CONTRIBUTING-PORT.md` §2):

```
WorldObject = Player(Arc<Player>) | Npc | Doodad | Slave | Mate | House | Transfer | Shipyard | Gimmick
              (Entity<D> = obj_id + ObjectCore{region, IsVisible, children} + Mutex<D>)
D: Character | Npc | Doodad | Slave | Mate | House | Transfer | Shipyard | Gimmick
   every unit kind embeds UnitBase (GameObject + BaseUnit + Unit members) and implements UnitData
WorldInstance: objects (by obj id) + characters + region grid; spawn / show / hide / delete /
   add_visible_object / remove_visible_object for every kind; around / objects_in_cell;
   attach_child / world_pos_rot (parent transforms)
systems::{skills, npcs, economy, social, quests}: Data (tables), System (managers), register
   (packets), start, save stages; systems::Hooks (UnitHooks: buff events, unit zone change)
```

## Subsystems

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `Core/Network/Game` (GameNetwork, GameProtocolHandler, GamePacket) | `net/` | done | Levels 1/2 framing, registry keyed by (level, opcode), `0xFFF` packets dropped on send. |
| `Core/Network/Connections` (GameConnection, GameConnectionTable, GameState) | `net/connection.rs`, `world/session.rs` | partial | ActiveChar, LeaveTask (+ cancel), OnDisconnect / SaveAndRemoveFromWorld. Houses, session attributes, connection subscribers todo. |
| `Core/Network/Login` + `Packets/G2L`, `Packets/L2G` | `login/` | done | All 5 G2L and 4 L2G packets (byte-identical), LoadTask, reconnect loop. |
| `Core/Network/Stream` + `Packets/C2S`, `Packets/S2C` (stream server) | `stream/` | done | StreamNetwork listener on `StreamNetwork.Host:Port`, `[u16 len][u16 op][body]` framing, connection table, all 17 CT and 15 TC packets (byte-exact). World lookups go through `stream::world::StreamWorld`, implemented by `stream_world::GameStreamWorld` (names, items/UCC ids, ActiveChar, money, Crest Ink grant, doodads of a cell via `WorldInstance::objects_in_cell`). Truncated packet bodies close the connection (C# reads zeros). |
| `Core/Packets/C2G` | `packets/c2g.rs`, `packets/c2g_world.rs` | partial | X2EnterWorld, CSLeaveWorld, CSCancelLeaveWorld, CSListCharacter, CSCreateCharacter, CSDeleteCharacter, CSCancelCharacterDelete, CSSelectCharacter, CSSpawnCharacter, CSNotifyInGame, CSNotifyInGameCompleted, CSMoveUnit, CSSendChatMessage. Others todo; full opcode table in `opcodes.rs`. |
| `Core/Packets/G2C` | `packets/g2c.rs`, `packets/world.rs` | partial | Lobby packets + every packet sent by the phase-2 paths (≈45, byte-exact tests). `SCLoginCharInfoHouse`, `SCMyHouse`, item tasks, buff packets todo. |
| `Core/Packets/Proxy` | `packets/proxy.rs` | done | All registered proxy packets (most are inert in C# too). |
| `Core/Packets/CompressedGamePackets`, `Debug` | – | todo | |
| `Core/Managers/World/EnterWorldManager` | `managers/enter_world.rs`, `world/session.rs` | done | AddAccount, Login, Leave (all targets), LeaveWorldTask (mates/slaves/coffers/quest timers are no-ops: not ported). |
| `Core/Managers/AccountManager` | `managers/account.rs`, `db.rs`, `systems/services/accounts.rs` | done | Add (login time + timed rewards), Remove/Contains/Count, GetAccountDetails, UpdateLoginTime, AddCredits/RemoveCredits/AddLoyalty/UpdateLabor/UpdateTickTimes/UpdateDivineClock/GetDivineClock under the per-account lock, RemoveDeadConnections (30 s tick). See `systems::services`. |
| `Core/Managers/FeaturesManager`, `Models/Game/Features` | `data/features.rs` | done | |
| `Core/Managers/ExperienceManager` (+ `SqliteExperienceLevelTemplateLoader`) | `data/experience.rs` | partial | Loader validation, level caps, GetExpForLevel, GetLevelFromExp (both overloads), GetSkillPointsForLevel. Mate/exp-loss helpers todo. |
| `Core/Managers/Id/*` (`IdManager`, `BitSet`, `PrimeFinder`) | `managers/id.rs` | done | Every C# id manager (incl. `distinct` for FamilyIdManager) with the C# allocation algorithm and DB seeding; `UccIdManager` in `stream/ucc.rs`. The static `SkillTlIdManager` is the skills subsystem's. |
| `Core/Managers/NameManager` | `managers/name.rs` | done | Load, ValidateCharacterName (regex from `CharacterNameRegex`), Add/Remove, NormalizeName. |
| `Core/Managers/ItemManager` (templates + user items) | `data/items.rs`, `managers/item.rs`, `models/item.rs`, `models/container.rs` | done | See `systems::economy`. |
| `Models/Game/Items/Containers` (ItemContainer, EquipmentContainer, MateEquipmentContainer, CofferContainer, LootingContainer) | `models/container.rs`, `systems/economy/looting.rs` | done | See `systems::economy`. |
| `Core/Managers/UnitManagers/CharacterManager` | `data/character_templates.rs`, `managers/character.rs`, `db_character.rs` | partial | Load (templates, body parts, buffs, supplies, equip packs, appellations, actabilities, expert limits, CharTemplates.json), Create, SetDeleteCharacter, SetRestoreCharacter, CheckForDeletedCharacters(+Deletion) with the delete task, IsCharacterPendingDeletion, LoadCharacters. Bag expands, beauty salon, rolls, online tracking todo. |
| `Models/Game/Char` (Character, Inventory, Abilities, Actability, Skills, Appellations, Portals, Friends, Blocked, Quests (rows), VisualOptions, ActionSlot) | `models/character.rs`, `models/char_data.rs`, `db_character.rs` | partial | Character.Load (both variants' columns), Character.Load() sub-loads, Save / SaveDirectlyToDatabase (characters, options, abilities, actabilities, appellations, active buffs, portals, friends, blocked, skills, quests), Write, CheckWantedThreshold (buffs + pirate faction). Mates, craft, combat, events todo. |
| `Models/Game/Units/Buffs`, `Models/Game/Skills/Buff` | `models/buff.rs`, `systems/mod.rs` | partial | Indices, stack rules, GetAllBuffs, CheckBuff(s), GetEffectByIndex / FromBuffId, GetBuffCountById, RemoveBuff / RemoveEffect (index, template+skill) / RemoveAllEffects, persistence (SaveActiveBuffs / LoadActiveBuffs), the NetBuff wire format and `BuffEvent`s (started / refreshed / removed) dispatched to `systems::UnitHooks`. Effects (`Start` / `Dispel`), tolerance, triggers, `TriggerRemoveOn`, tag checks, timers, buff packets: skills subsystem. |
| `Models/Game/World/GameObject`, `Models/Game/Units/{BaseUnit,Unit,IBaseUnit,IUnit,BaseUnitType,UnitTypeFlag,ModelPostureType}` | `models/unit.rs`, `world/object.rs` | partial | All data members (`UnitBase`), `Hpp`, `IsDead`, `NeedsRegen`, trigger counts, `CalculateWithBonuses`, `UnitData` / `BaseUnitData` traits, handles (`Entity<D>`, `WorldObject`). Behaviour (`ReduceCurrentHp`, `DoDie`, `CanAttack`, regen, aggro, `UseSkill`, `SetFaction`, `UpdateGearBonuses`) belongs to the phase-3 subsystems. |
| `Models/Game/Units/{UnitCooldowns,CombatBuffs,UnitAttribute,UnitModifierType}`, `Models/Game/Skills/{Bonus,DynamicBonus,SkillModifier(s),BuffModifier(s)}`, `Skills/Static/{SkillAttribute,BuffAttribute}`, `Templates/{BonusTemplate,DynamicBonusTemplate,LinearFuncTemplate}` | `models/unit.rs` | done / partial | Data model + pure logic (`ApplyModifiers`, `Evaluate`, cooldown checks). The DB loaders for the rows and `TriggerCombatBuffs` are the skills subsystem's. |
| `Models/Game/World/Transform` (`Transform`, `PositionAndRotation`) | `models/transform.rs`, `world/mod.rs` | partial | Local position/rotation, `ToRollPitchYawSBytes/Shorts`, quaternions, `GetWorldPosition` parent composition (`WorldInstance::world_pos_rot`), `GetRootObj`, attach / detach children. Sticky parents, debug trackers, `FinalizeTransform` batching: later. |
| `Models/Game/NPChar/Npc`, `Units/Portal`, `DoodadObj/Doodad`, `Units/{Slave,Mate,Transfer}`, `Housing/House`, `Shipyard/{Shipyard,ShipyardData}`, `Gimmicks/Gimmick` | `models/{npc,doodad,slave,mate,transfer,house,shipyard,gimmick}.rs` | partial | Identity / state fields the visibility packets read, `Doodad.Write`, `House.Write`, `ShipyardData.Write`, `Gimmick.Write`, `TimeLeft`. Templates, funcs, spawners, AI, physics, persistence: phase-3 subsystems. |
| `Core/Packets/G2C` object visibility (`SCUnitStatePacket` all kinds, `SCDoodadCreated/sCreated/Removed/sRemoved`, `SCGimmicksCreated/Removed`, `SCGimmickJointsBroken`, `SCHouseState`, `SCShipyardState`, `SCMateState`, `SCSlaveState`, `SCTargetChanged`, `SCUnitAttached`) | `packets/world_objects.rs`, `world/unit_state.rs`, `world/visibility.rs` | done | Byte-exact tests (`world/unit_state_tests.rs`, `packets/world_objects.rs`, `object_flow_tests.rs`). The NPC reference-height adjust in `SCUnitStatePacket` needs heightmaps (todo). |
| Phase-3 subsystem framework | `systems/` | done | Per-subsystem data / managers / packets / start / save stages / hooks; packet registration per module with duplicate detection. |
| `Core/Managers/SkillManager` | `data/skills.rs` | partial | skills (learning fields), default_skills, passive_buffs, buffs (subset), start-ability / common skill lists. Effects, plots, tags, modifiers todo. |
| `Core/Managers/FormulaManager` + Jace engine | `data/formulas.rs`, `models/stats.rs` | done / partial | Formula tables + a Jace-compatible evaluator (differentially tested against Jace 1.0.0). Character Str..Fai / MaxHp / MaxMp from the unit formulas; bonuses and gear stats are approximated (see deviations). |
| `Core/Managers/World/WorldManager` (+ `WorldInstance`, `Region`) | `world/mod.rs`, `world/visibility.rs`, `data/world_templates.rs` | partial | World templates from `game/worlds/*/world.xml` (directory or game_pak) + world_spawns.json, GetZoneId, main_world instance, region grid, AddVisibleObject / RemoveVisibleObject / AddToCharacters / RemoveFromCharacters (all object kinds, region batches, children, the non-player removal filter), Show / Hide / Spawn / Delete, AddObject / RemoveObject / Get* lookups, GetAround, GetInCell, GetRegion / GetRootObj, server/instance character lists, UsedCharacterObjIds, OnPlayerJoin. Heightmaps, water, physics, `ResendVisibleObjectsToCharacter` todo. |
| `IO` (ClientFileManager, AAPak) | `data/client_files.rs`, `data/pak.rs` | done | Read-only pak (types A/B/F) and directory sources, file listing semantics of both. |
| `Core/Managers/World/ZoneManager` | `data/world_data.rs` | partial | zones, zone_groups, conflict_zones (initial state only; the state timers are todo). |
| `Core/Managers/World/FactionManager`, `LocalizationManager` | `data/world_data.rs`, `systems/services/localization.rs` | partial / done | system factions (+ localized names), relations, SendFactions / SendRelations (relation checks: `systems::social`). The complete `LocalizationManager` (all translations, item-name cache, `MatchItemName`) is `systems::services::localization`. |
| `Core/Managers/ExpeditionManager`, `Models/Game/Expeditions` | `managers/expedition.rs` | partial | Load, GetExpedition, SendExpeditions, SendExpeditionInfo, member refresh / login / logout status packets. Guild management todo. |
| `Core/Managers/FriendMananger` | `managers/friend.rs`, `db_character.rs` | partial | Load, SendStatusChange, GetFriendInfo, FormatFriend. Add/remove todo. |
| `Core/Managers/PortalManager` | `data/world_data.rs` | partial | recalls.json + district_return_points (GetRecallBySubZoneId, GetDistrictReturnPoint). Teleports, world gates, respawns todo. |
| `Core/Managers/ChatManager`, `Models/Game/Chat` | `managers/chat.rs`, `world/session.rs` | partial | Faction / nation / zone / guild / family channels, join/leave/send; CSSendChatMessage for every chat type (party/raid -> "not in party/raid" errors as there are no teams). Trial channels, commands todo. |
| `Core/Managers/TimeManager` (+ `TimeOfDayObserver`) | `managers/time.rs` | done | `Get` / `Set` / `Stop` added (phase 4). Storm-ship time override todo. |
| `Core/Managers/SaveManager` | `managers/save.rs` | partial | Periodic + final save of items and online characters; the subsystems' rows through `systems::save_stage` (`BeforeItems`: houses, mails; `AfterItems`: auction, crimes; `AfterCharacters`: slaves). |
| `Core/Managers/World/StreamManager` | `managers/stream.rs`, `stream/cells.rs` | done | AddToken/RemoveToken/Login (wrong account removes the token, as C#), RequestCell/ContinueCell incl. the C# `next - 1` copy off-by-one. |
| `Core/Managers/Stream/UccManager`, `Models/Stream`, `Core/Managers/Id/UccIdManager` | `stream/ucc.rs`, `stream/models.rs` | partial | Load/Save (`uccs` table, data in the MEDIUMBLOB as the default C# build), upload (simple + custom), download, checks, id allocation. `CreateStamp`/`ApplyStamp` (item-side, used by skill effects) todo. ConfirmDefaultUcc saves before granting the Crest Ink (C# creates the item first). |
| `Core/Managers/TimedRewardsManager` | `systems/services/timed_rewards.rs` | done | `TimedRewardsTask` every minute, daily login reward, offline labor. |
| `Core/Managers/HousingManager`, `Models/Game/Housing` | `systems/housing/` | done | See "Phase 3 subsystems". |
| `Core/Managers/CrimeManager` | `db_character.rs` | partial | CheckWantedThreshold thresholds only. |
| Other `Core/Managers/*` | – | see the subsystems | AccessLevel, Command, ExpressText, Model, Music, Radar, Sus, Task/Tick, TimedRewards: `systems::services`; Animation, EffectTask, ManaRegen, Plot: `systems::skills`; FishSchool, Gimmick, PublicFarm, SubZone, Spawn, AreaTrigger, Specialty, SphereQuest: `systems::npcs`; Family, Duel, Team, Trial, Crime: `systems::social`; Quest: `systems::quests`. Still todo (other owners): AI / AiPaths / AiGeodata, Auction, CashShop, Craft, Mail, Trade, Taxations (economy); Housing; Mate, Slave, Shipyard, Transfer (vehicles); Indun, InstantGame (instances); World/Physics. |
| `Models/Game/Units/UnitCustomModelParams` | `models/unit_model.rs` | done | |
| `Models/Game/Units/Movements` (MoveType hierarchy) + `MathUtil` direction helpers | `models/movement.rs` | done | Default / Unit / Vehicle / Ship / ShipRequest / Transfer, byte-exact read/write (all actor-flag combinations tested). |
| `Models/AccountPayment`, `Models/Account` | `net/connection.rs`, `db.rs` | done | |
| `Models/AppConfiguration`, `Models/Game/Configurations` + `Program` config loading | `config.rs` | done | Every member (incl. WebApiNetwork, DiscordToken, Dungeons, AccessLevel, LaborOffline, Specialty, Scripts, full World), Config.json + Configurations/** + Config.Local.json + user secrets + environment (`Section__Key`) + command line, enums by name / value, `ConfigCell` (= `AppConfiguration.Instance`, replaceable at runtime). |
| Other `Models/Game/*` (AI, Achievement, Auction, Crafts, Crime, DoodadObj, Mails, Mate, NPChar, Quests logic, Slaves, Team, ...) | – | todo | |
| `Scripts/Commands`, `Utils/Scripts` | `systems/services/commands/` | partial | Infrastructure done (`CommandManager`, `AccessLevelManager`, `ICommand`, `IMessageOutput` / `CharacterMessageOutput`, `SubCommandBase` + parameters, script loader hook, the `Utils/Scripts/SubCommands` World / Time / FishFinder / Feature / AStar sub commands); chat `/` messages go through `CommandManager.Handle`. The commands of `Scripts/Commands/**` (and the `SphereCommandUtil` / `QuestCommandUtil` bodies) are the scripts phase. |
| `Services` (WebApi, DiscordBot) | `systems/services/webapi/`, `systems/services/discord.rs` | done | axum transport with the C# regex routing; mail / auction controllers call the economy through `WebApiEconomyHooks`. |
| `GameService` startup / `ManagerOrchestrator`, `Program` | `main.rs`, `systems/services/game_service.rs` | done | `Program.Main` (initialization log, `compiler-check`, config command line, MySQL / SQLite checks) and `GameService.StartAsync` stages in order (DB updater, client files, holdables check, game data, user items, DB loads, time / task managers, subsystems, save manager, deleted characters, online tracking, networks, "Server started! Took ..."), then WebApi / Discord; shutdown in reverse (Discord, WebApi, final save, task / tick / time managers). `GameService.StartTime` / `TimeSinceStart`, `Program.UpTime`, `ManagerOrchestrator.BuildBatches` ported with their tests. |

## Phase 3 subsystems

Each subsystem records its status here, in its own section only (see `CONTRIBUTING-PORT.md` §3).

### systems::skills

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `SkillManager` (+ `Sqlite*` effect / buff / plot loaders), `PlotManager`, `FormulaManager` remainder, `AnimationManager`, `AreaTriggers` shapes, `ModelManager` radii, `UnitRequirementsManager`, `TagsManager` (skills / buffs) | `systems/skills/data/` | done | Every skill / buff / effect / plot / tag / modifier / trigger / reagent / product / linear func / tolerance / anim / aoe shape table, incl. all 30+ effect types. Gated real-DB test `skills_real_compact_db` checks row counts. |
| `Models/Game/Skills/{Skill,SkillCaster,SkillCastTarget,SkillObject,CastAction,SkillController*}`, `Core/Packets/{C2G,G2C}` skill / buff packets | `systems/skills/{types,packets,skill,handlers}.rs` | done | Byte-exact wire types (tested), `Skill.Use` / `Cast` / `Channeling` / `Apply` / `EndSkill` / `Stop`, GCD + cooldowns, mana, labor / vocation, reagents via `ItemSkillHooks`, auto attack loop, `CompressedGamePackets`. |
| `Models/Game/Skills/Plots/*` | `systems/skills/plots.rs` | done | Plot tree execution (async), conditions 1..17, target info updates (area / random unit / random area), channeling finish, `SCPlotEvent` / `SCPlotEnded`. |
| `Models/Game/Units/Buffs` behaviour, `Skills/Buff`, `Skills/Effects/{Buff,Damage,Heal,Dispel,RestoreMana,ManaBurn,Aggro,FlyingStateChange,SkillController,...}`, `CombatBuffs`, `BuffTriggers` | `systems/skills/{buffs,effects/*}.rs` | done | Start / dispel / tick tasks, tolerance, stack rules, charges, `TriggerRemoveOn`, tag removal, absorption, triggers, area ticks. Special effects routed through `ExtHooks::route_special` where they belong to another subsystem. |
| `Units/Unit` combat (`ReduceCurrentHp`, `DoDie`, aggro / tagging, `SetInBattle`, criminal / force attack, invisibility, `DoFallDamage`), `BaseUnit.CanAttack` / relations, regen, `UpdateGearBonuses`, all `Character` stat getters | `systems/skills/{combat,relation,stats,regen}.rs` | done | MaxHp / MaxMp etc. with buffs, passives and gear (`GearStats` hook). NPC death side effects through `NpcSkillHooks`. |
| Learning / progression (`LearnSkill`, `LearnBuff`, `ResetSkills`, `SwapAbility`, `AddExp`, labor, game points) | `systems/skills/progress.rs` | done | |
| `Skills/Effects/SpecialEffects/*` (all 141 `SpecialType` actions), `BubbleEffect`, `ImpulseEffect` | `systems/skills/effects/{special/*,unit_fx}.rs`, `packets_special.rs` | done | Actions owned by other subsystems (doodads, mates, slaves, items, portals, quests, teams) call the `ext` hooks. 13 extra S->C packets, byte-exact tests. |
| `Skills/SkillControllers/{Floating,Wandering,Leap,Dash}` | `systems/skills/controllers.rs` | done | `ShipHarpoonRopeController` is the slaves owner's. |
| Movement hooks (`CSMoveUnit.RemoveEffects`, `Character.SetPosition` Move removal, fall damage) | `systems/skills/mod.rs` (`on_move_request`, `on_moved`) | done | Called from `world::session::move_unit`. |

### systems::npcs

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `NpcManager.Load` (customs, body parts, templates, equip packs, postures, unit modifiers, initial buffs, merchants / goods) | `npc/data.rs` | done | 13 382 templates on the real DB. Creatures.xml names, `LoadAiParams` not ported. |
| `NpcManager.Create`, `LoadCustom` (.NET `Random(123456789)` parity), `Npc` stats (Str..KillExp from the unit formulas + template bonuses), `InitializeSpawnBuffs`, `AnimActionId` | `npc/manager.rs`, `npc/stats.rs` | done | Every real template creates without error. AI: `has_ai` only (later phase). |
| `Npc.DoDie` (non-AI part) | `npc/death.rs` | partial | `SCUnitDeathPacket`, `DeadTime`, loot / exp / kill hooks, spawner despawn. Aggro clearing (AI) and team tag XP (social) not done. |
| NPC interaction packets (CSStartInteraction, CSInteractNPC(End), CSSelectInteractionEx; SCNpcInteractionSkillList, SCAiAggro, SCNpcChatMessage, SCUnitModelPostureChanged, SCAggroTargetChanged) | `npc/packets.rs` | done | Byte-exact tests. Merchant / bank / auction UIs are economy's. |
| `NpcGameData` (npc_spawners, npc_spawner_npcs, member index, generated spawners) | `npc/spawner_data.rs` | done | |
| `NpcSpawner`, `NpcSpawnerNpc`, `DoodadSpawner`, `Spawner<T>` | `npc/spawner.rs`, `doodad/spawner.rs` | done | Schedules / time windows / population caches, respawn + despawn timing. Follow path / NPC (AI) TODO. |
| `SpawnManager` (one per instance) + `WorldManager.ActiveRegionTick` spawner part | `spawn_manager.rs` | done | main_world: 25 118 NPC entries → 38 166 spawner clones, 42 649 doodads, 69 transfers, 14 gimmicks, 5 slaves, 0 parse errors. Persistent doodads from MySQL. Transfer / slave spawning and housing are TODO (not ported by any subsystem yet). |
| `DoodadManager.Load` (templates, func groups, funcs, phase funcs, all 121 func template tables) | `doodad/data.rs`, `doodad/templates.rs` | done | 7 124 templates, 13 974 funcs, 24 465 phase funcs (counts match the DB). |
| `Doodad` phase machine (`Use`, `DoFunc`, `DoPhaseFuncs`, `DoChangePhase`, `InitDoodad`, `OnSkillHit`, `FuncGroupId` setter, ToD triggers), 42 phase funcs, func tasks, `DoodadManager` runtime (Create, CreatePlayerDoodad, ChangeDoodadData, coffers), persistence (`doodads` table) | `doodad/{phase,phase_funcs,tasks,manager,db}.rs` | done | Timers are tokio tasks cancelled through a generation counter (`DoodadPhaseState::func_task`). `Task.Run(InitDoodad)` in Create is a spawned task. |
| 79 non-phase funcs, 41 world interactions (`execute_interaction` for the skills InteractionEffect), coffers, bond / seats | `doodad/funcs/`, `doodad/interactions.rs`, `doodad/coffer.rs` | done / partial | Items, loot, skills, housing, quests through `npcs::hooks` bridges. Instance / slave / sticky-parent funcs log + TODO. |
| SCDoodad* packets, doodad C->S packets (CSChangeDoodadData/Phase, CSCreateDoodad, CSSetLogicDoodad, CSSaveDoodadUccString, CSUnbondDoodad, CSCofferInteraction) | `doodad/packets.rs`, `doodad/coffer.rs` | done | Byte-exact tests. |
| Heightmaps (`WorldCell` heightmap.dat, `GetHeight`, `GetReferenceHeight`), NPC reference height in `SCUnitStatePacket` | `heightmap.rs`, `world/unit_state.rs` | done | Lazy per cell (`PreLoadTerrain`), geodata path not ported. Median error 0.098 m vs 4 002 real spawn Z values. |
| `GimmickManager`, `GimmickGameData`, `GimmickSpawner`, movement handlers | `gimmick/` | done | |
| `AreaTriggerManager` (+ aoe_shapes, wi_group_wis), `SubZoneManager`, `SphereQuestManager` (world side) | `area_trigger.rs`, `subzone.rs`, `sphere_quest.rs` | done | Quest effects via `QuestsBridge`. Sub-zone index scan of the pak is slow in debug (~25 s). |
| `PortalManager` (recall, district return, summoned portals, portal book, teleports) | `portal/` | done | Trial court check and slave/mate handling on instance change TODO. |
| `SpecialtyManager`, `PublicFarmManager`, `FishSchoolManager`, `GameScheduleManager` (+ NCrontab subset) | `specialty.rs`, `public_farm.rs`, `fish_school.rs`, `schedule.rs` | done | |
| `TimeManager.OnTimeOfDayChange` (NPC postures, doodad ToD triggers) | `doodad/manager.rs` | done | 10 s poller started by the doodad part (TimeManager has no callback). |
| Cross-subsystem behaviour | `hooks.rs` | – | `SkillsBridge`, `EconomyBridge`, `QuestsBridge` (`server.systems.npcs.hooks`), default "not ported" bodies. |

### systems::economy

Layering: `models::item_task` (`ItemTask`, `ItemTaskType`, recorded effects `ItemFx`) →
`models::container` (container operations, pure, under the container locks) →
`systems::economy::inventory` (`ItemTxn`: economy operation lock + effect flush in C# order,
`Inv`, `Out` queue for character packets) → feature modules. Other subsystems plug in through
hook slots (`server.systems.economy.hooks.{quests,gear,progression,npcs,summons}`,
`loot.hooks`, `craft.hooks`, `mail.hooks.housing`, `cash_shop.{quests,db}`; internal slots are
connected by `systems::economy::wiring`).

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `Models/Game/Items/Actions/*` (every `ItemTask`, `ItemAction`, `ItemTaskType`), `SCItemTaskSuccessPacket`, `SCSyncItemLifespanPacket`, `SCUnitEquipmentsChangedPacket`, `SCInvenExpandedPacket`, `SCGamePointChangedPacket`, `SCCharacterLaborPowerChangedPacket` | `models/item_task.rs`, `systems/economy/item_packets.rs` | done | Tasks snapshot the item when created (the C# encodes on `SendPacket`). |
| `ItemContainer`, `EquipmentContainer`, `MateEquipmentContainer`, `CofferContainer` | `models/container.rs` | done | `AddOrMoveExistingItem` (new / moved / same container), `RemoveItem`, `ConsumeItem`, `AcquireDefaultItem(Ex)` (timers, charges, crafter), `SpaceLeftForItem`, `ApplyBindRules`, `Wipe`, `ReNumberSlots`, coffer `CanAccept`, `OnEnter/LeaveContainer` events. |
| `Inventory` (SplitOrMoveItem(Ex), 2H weapon handling, backpacks, ExpandSlot, coffers, ConsumeItem, counts), `Character` money / game points / labor / `ItemUse` / `SendErrorMessage`, `ErrorMessageType` | `systems/economy/inventory.rs`, `error_message.rs` | done | `ChangeLabor`'s `AddExp` is a hook (`ProgressionHooks`). |
| Core item C2G (`CSSwapItems`, `CSSplitBagItem`, `CSDestroyItem`, `CSExpandSlots`, `CSDeposit/WithdrawMoney`, `CSSplit/SwapCofferItem(s)`, `CSItem(Un)Secure`, `CSEquipments(Un)Secure`, `CSThisTimeUnpackItem`) | `systems/economy/item_handlers.rs` | done | |
| `ItemManager` (all templates / tables, user items, `Create`, timers, `UnwrapItem`, auction search helpers, loot convert fish), `ItemGameData`, `ItemConversionGameData`, `FishDetailsGameData`, item model stats (durability, repair cost, attributes, DPS, armor), procs | `data/items.rs`, `managers/item.rs`, `models/item.rs`, `systems/economy/{items_data,item_timers}.rs` | done | `ItemManager` lists are leaf locks (snapshot before locking containers). |
| `LootGameData`, `Models/Game/Items/Loots/**`, `LootingContainer`, loot packets | `systems/economy/{loot_data,loot,looting,loot_packets}.rs` | done | NPC death / despawn / team rules through `LootNpcHooks`, `LootTeamHooks`, `LootQuestHooks`. |
| Repair (`Character.DoRepair`, `CSRepairSingleEquipment`, `CSRepairAllEquipments`), durability loss | `systems/economy/repair.rs` | done | Combat calls `on_damage_taken` / `on_death`. |
| `CraftManager`, `Models/Game/Crafts`, `CharacterCraft`, `CraftTask`, `CSExecuteCraft`, `SCCraftFailed` | `systems/economy/{craft,craft_data}.rs` | done | Cast through `CraftHooks::start_craft_cast`; skills call `craft::end_craft`. |
| Stores (`CSBuyItems`, `CSSellItems`, buy-back, `CSListSoldItem`, `CSBuyPriestBuff`, `CSBuyCoinItem`, `CSSellBackpackGoods`), `SpecialtyManager`, `TaxationsManager`, `ExpandExpert`, item look conversion | `systems/economy/{store,store_data,specialty}.rs` | done | Merchant packs / NPC flags through `NpcEconomyHooks`. |
| `MailManager`, `Models/Game/Mails/**`, `CharacterMails`, mail packets | `systems/economy/{mail,mail_character,mail_kinds,mail_packets,mail_data}.rs` | done | Saved in `SaveStage::BeforeItems`; housing tax through `MailHousingHooks`. |
| `AuctionManager`, `Models/Game/Auction/**`, auction packets | `systems/economy/{auction,auction_data,auction_packets}.rs` | done | Saved in `SaveStage::AfterItems`; results mailed through `wiring`. |
| `TradeManager`, trade packets | `systems/economy/trade.rs` | done | |
| `CashShopManager`, `Models/Game/CashShop/**`, ICS / premium packets | `systems/economy/cash_shop.rs` | done | Gifts by `CommercialMail` (`wiring`); account credits SQL behind `CashShopDb`. |
| Item skill-effect bodies (grade enchant, socketing, conversion, cap scale, UCC imprint, ...), mate / slave equipment packets | `systems/economy/item_effects*.rs` | done | Called by the skills subsystem's effects. |

### systems::social

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `TeamManager`, `Models/Game/Team` | `systems/social/team.rs`, `packets/team.rs` | done | Invitations (single / area, 60 s timeout), create, AskRiskyTeam (leave / kick / dismiss + auto-disband), owner / role / marker / looting rule / ping / move, raid conversion, SetOffline / MemberRemoveFromTeam / UpdateAtLogin / UpdatePosition, 500 ms remote-member sync (delta cache, 5 s forced resync, upper-triangle pairs), zombie-team disband (5 min), loot winner helpers. Events (OnTeamJoin/Leave) are quest-side: not called. |
| `ExpeditionManager`, `Models/Game/Expeditions` | `systems/social/expedition.rs`, `packets/expedition.rs` (`managers::expedition` re-exports) | done | Load, create (party checks, cost, `SCItemTaskSuccessPacket` money-only form), invite / reply, leave, kick, member role, owner, role policy, disband (stays listed as `$deleted-guild-<id>`), `Expedition.Save` per change, offline leave for character deletion. Rename / sponsor are log-only / not in 1.2 as in C#. |
| `FamilyManager`, `Models/Game/Family` | `systems/social/family.rs`, `packets/family.rs` | done | Load, invite / reply / create, leave, kick (offline existence check), title, steward, login / logout, disband, save, offline leave for character deletion. |
| `FriendMananger`, `CharacterFriends`, `CharacterBlocked` | `systems/social/friend.rs`, `packets/friend.rs` (`managers::friend` re-exports) | done | Add / remove friends (online or by DB name lookup), block / unblock (online only), status changes incl. `InParty`. |
| `ChatManager`, `Models/Game/Chat` | `systems/social/chat.rs`, `session.rs` (`managers::chat` re-exports) | done | All channel kinds incl. party / raid / court, CleanUpChannels, party / raid / raid-leader / judge routing. User channels (`CSJoinUserChatChannel`) are log-only as in C#. |
| `FactionManager` relations, `SystemFaction.GetRelationState`, `Unit/Character.SetFaction` | `systems/social/faction.rs`, `packets/faction.rs` | done | Relation table validated against compact.sqlite3 (gated test). Faction CS packets are log-only as in C#; every SCFaction* struct ported. |
| `DuelManager`, `Models/Game/Duels`, duel tasks | `systems/social/duel.rs`, `packets/duel.rs` | done | Request / accept (flag via hook) / start / stop / cancel, distance + HP checks, end timer (tokio tasks; `set_delays` test hook). |
| `CrimeManager`, `TrialManager`, `Models/Game/Crime`, `TrialUpdateTask` | `systems/social/{crime,trial}.rs`, `packets/trial.rs` | done | Crime table load / save (`AfterItems`), report / evidence (hooks), bot reports, full trial / jury flow with 5 s tick, audience, bounty board (empty list as C#). |
| `DominionData`, `Models/Game/Achievement`, rename / chat bubble / NPC chat / spam packets | `systems/social/{dominion,achievement}.rs`, `packets/misc.rs` | done (data + packets) | No C# manager drives them in 1.2. |
| `RadarManager` | – | not social | Telescope / fish school registration from buff templates: skills / npcs. |

Cross-subsystem calls still to wire (API exists in social): npcs -> `trials.initialize_doodads` / `initialize_npc` after spawning `justice.*` spawners and install `SocialHooks` (spawn_doodad, doodad_phase_changed, terrain_height, create_player_doodad, evidence_func, doodad_change_phase, judge_obj_id); skills/combat -> `crimes.generate_evidence_from_damage/kill`, `trials.arrest_criminal`, `teams.are_team_members` / `team_by_obj_id` / `is_obj_member`, `duels` queries, `report_bot*`, and hooks `check_buff_tag` / `remove_buffs_by_tag` / `post_update_current_hp`; economy -> `teams.next_loot_winner` / `next_eligible_looter`, hook `update_owned_housing_faction`; instance / portal entry -> `trials.is_player_in_court`; mates -> hook `force_dismount`; teleports -> `session::on_moved` (C# `Character.SetPosition` -> `TeamManager.UpdatePosition`; plain movement does not call it in C# either).

### systems::housing

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `HousingGameData` (`item_housings`, `housings`, `housing_binding_doodads` + `Data/housing_bindings*.json`, `housing_build_steps`, `housing_decorations`, `item_housing_decorations`, `PostLoad`), `TaxationsManager` (`taxations`) | `systems/housing/data.rs` | done | Gated real-DB test `housing_real_compact_db` (row counts, bindings positioned). Duplicate build steps fail the load (C# `Dictionary.Add`). |
| `HousingManager` (all methods), `House` behaviour (`CurrentStep` setter + bound doodads, `AddBuildAction`, `Spawn` / `Delete` / visibility of `AttachedDoodads`, `AllowedToInteract`, `OnDeath`, `Save`), `HousingTaxTask`, `MailForTax` content | `systems/housing/{manager,ops,db}.rs` | done | Build (tax certificates or gold), permission, rename, recover toggle, demolish (+ `ReturnHouseItemsToOwner` mails), sale / sale markers / cancel / buy (+ mails, furniture owner), decoration, taxes (bills, untouchable / removal buffs, demolition timer every 10 s), death, `GetHouseAtLocation`, act-ability bonus. Items / money / mail go through `housing::hooks::HousingEconomyHooks` (economy installs it). |
| Housing packets (11 CS, 14 SC incl. `SCLoginCharInfoHouse`, `SCMyHouse`) | `systems/housing/packets.rs` | done | Byte-exact tests. `CSChangeHousePayPacket` has opcode 0xfff in 1.2 (not registered). `SCHouseOwnerNameChangedPacket` is social's (`packets/misc.rs`). |
| `CraftEffect` (`WorldInteractionGroup.Building`) | `systems/housing/mod.rs::apply_effect` | done | Routed first by `skills::ext::ExtHooks::route_effect`; quest `DoDoodadInteractionEvents` after it. |
| `SpawnManager.Load` / `SpawnPersistentDoodads` housing parts, `LoadAccount` / `CSListCharacter` / `CSSelectCharacter` / `Character.Load` / `DeleteCharacterAssets` housing parts | `npcs/spawn_manager.rs`, `packets/c2g.rs`, `world/session.rs`, `db_character.rs` (one call each) | done | `LoadPlayerHousing` + `SpawnAll` before the persistent doodads, house parenting + bound doodads, `ReconcileBoundDoodads` (`UsePersistentHouseDoodads`). |
| Placement validation | – | n/a | The 1.2 C# `Build` validates nothing but the design item (TODOs); the client checks the housing areas. Land checks for doodads (`GetHouseAtLocation` + `AllowedToInteract`) are ported and used by `CSCreateDoodad` / `CreatePlayerDoodad`. |

Hooks the economy subsystem must install (`server.systems.housing.hooks`): `inventory_item`, `item`, `inventory_item_count`, `bag_consume_item`, `subtract_money`, `move_item_to_system_container`, `destroy_item`, `take_coffer_items`, `send_mail`, `send_mail_with_new_items`, `delete_house_mails`, `house_mail_ids`, `update_tax_mail`. Housing API for others: `housing::{house_by_id, house_at_location, allowed_to_interact(_by_id), pay_weekly_tax (MailManager tax payment), act_ability_bonus_at (CharacterCraft), update_owned_housing_faction, demolish_by_player}`.

### systems::quests

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `QuestManager.Load` (contexts, components, base acts, all 65 `quest_act_*` detail tables, supplies, item / NPC groups, `UpdateQuestComponentActs`) | `systems/quests/data.rs`, `templates.rs` | done | Same table order (act order inside a component = objective indices), first-base-act linking, tutorial category skipped. Validated against compact.sqlite3 (`real_compact_db_quest_tables`: 4775 quests, 17720 components, 19047 acts, per-type counts). |
| `Models/Game/Quests` (`Quest`, `NewQuestCode`, `QuestStep`, `QuestComponent`, `QuestAct`, all `Acts/*` + `UnusedActs/*`) | `state.rs`, `engine.rs`, `acts.rs` | done | Live quests on `Character::quest_state` (handles + act paths instead of object references); `c.quests` rows re-derived after every operation. Step machine, objective status / percent, rewards, drop / cleanup, event subscriptions in invocation-list order (`-=` removes the last occurrence). |
| `CharacterQuests` (AddQuest*, DropQuest, completed flags, reset / daily reset, LetItDone, item destroy, `GetActiveActsWithUseItem`, Load) | `engine.rs`, `events.rs` | done | Save = existing `Character.Save` rows. |
| `QuestManagerEvents` + other C# entry points (OnItemUse, OnKill, cinema, mate level) | `events.rs` | done | Public event API; nested calls from hooks are queued. |
| `QuestManager` evaluation queue / timers / daily reset task | `mod.rs` | done | `QuestManagerRunQueueTask` as a 1 ms tokio task; timers as tokio tasks; daily reset at 00:00 UTC. |
| `UnitRequirementsGameData` (`CanComponentRun`, `CanTriggerSphere`) + `UnitReqs.Validate` | `unit_reqs.rs` | done | `ReqSubject` / `ReqEnv` traits (owner = locked character, target = snapshot). `CanUseSkill` is the skills subsystem's (can reuse `validate`). |
| `SphereQuestManager`, `SphereQuest*`, `SphereGameData` (spheres, sphere_quests, `IsInsideAreaSphere`, `GetSphereIdFromQuest`) | `spheres.rs`, `data.rs` | done | `quest_sign_sphere.g` of `main_world`; one manager per server (500 ms tick). Other `sphere_*` tables not loaded (unused by quests). |
| Quest packets (CS: StartQuestContext, CompleteQuestContext, DropQuestContext, QuestTalkMade, QuestStartWith, TryQuestCompleteAsLetItDone, RestartMainQuest, SaveTutorial, StartedCinema, CompletedCinema; SC: QuestContextStarted/Updated/Completed/Reset, QuestRewardedByMail, DoodadQuestAccept, TutorialSaved, AppellationGained) | `packets.rs` | done | Byte-exact tests. `SCQuestsPacket` now writes `Quest.Id` as `long`. |
| `QuestCommandUtil` / `QuestCmd` (GM) | – | todo | `Ctx::gm_set_step` is the `SetStep` helper. |

### systems::instances

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `IndunGameData` (actions, events, zones, rooms), `Models/Game/Indun/{IndunZone, IndunRoom}` | `systems/instances/data.rs` | done | Same six event queries / four action queries in C# order; `EnterCount` hack (1000 for group 49 / channel dungeons, else 3); localized names. Real DB: 104 actions, 70 events, 20 zones, 13 rooms. |
| `BattlefieldGameData` + `Data/battlefields.json`, `TowerDefGameData`, `Models/Game/{InstantGame, TowerDefs}` data | `data.rs` | done | 7 battlefields (last `game_rule_sets` row wins), 24 tower defs / 11 progs. Loader quirks mirrored (see bugs). |
| `WorldManager.CreateWorldInstance` / `RemoveWorld` / `CreateStaticInstances` (AutoCreate) / `WorldIdManager`, `WorldInstance.CleanupInstance` / `ChannelId` / `HasCharacter` / `GetCharacterCount` / `ListPlayerNames` | `worlds.rs`, `mod.rs` (start), `world/mod.rs` (`has_world`, `remove_world`, `leave_other_instance`, `WorldInstance::transfer_out`), `npcs::spawn_manager::SpawnManagers::remove` | done | Non-instanceable templates reuse their instance; fixed ids <= 100; per-instance `SpawnManager` created with the world and loaded by the dungeon loader / a background task. The cross-instance region switch happens on the player's next move (as the C# `AddVisibleObject` region change). |
| `Character.MainWorldPosition`, `Transform.CloneDetached` / `ApplyWorldSpawnPosition` / `MainWorldPosition.Clone()` | `worlds.rs`, `models/character.rs` (`main_world_position: Option<Transform>`), `db_character.rs` (save rule) | done | Saved inside an instance: the return point is written (C# `saveFromInstanceReturn`). |
| `IndunManager` (requests, requirements, entry history, system instances, deletion, leave, `DoIndunActions`, info tick) | `indun.rs` | done | |
| `Dungeon` + `DungeonLoaderTask` (queue, add / move, leave, team / disconnect handlers, `LeaveDungeonTick`, `AreaClearTick`, destroy) | `dungeon.rs` | done | `DestroyTeamDungeon` (never called in C#) not ported. |
| `Models/Game/Indun/Events/*`, `Actions/*`, `WorldInstance.Events` (`OnUnitSpawn`, `OnDoodadSpawn`, `OnUnitKilled`, `OnUnitCombatStart/End`, `OnAreaClear`), `CharacterEvents` (`OnTeamJoin/Leave/Kick`, `OnDungeonLeave`, `OnDisconnect`), `UnitEvents.OnKill` | `events.rs` | done | Raised from `npcs` spawners, `skills::combat` (`PostUpdateCurrentHp` / `DoDie`), `social::team`, `world::session::on_disconnect`. |
| `InstantGameManager`, `InstantGame` (+ `InstantGameEvents`, team members / results) | `instant_game.rs` | done | Queue / matchmaking / invite / join / enter / start / kills / scores / end / leave; `Character.SetFaction` override (housing hook, guild kick, party kick); `ResetAllSkillCooldowns`. |
| Packets: `CSInstanceLoaded`, `CSUnknownInstance`, `CSApplyToInstantGame`, `CSCancelInstantGame`, `CSJoinInstantGame`, `CSEnteredInstantGameWorld`, `CSLeaveInstantGame`; `SCProcessingInstance`, `SCAppliedToInstantGame`, `SCInviteToInstantGame`, `SCInstantGameJoined/Start/End/AddPoint/Kill/Killstreak`, `SCInstantGameRemainTime` (`SCInstantGameUnkPacket` opcode), `SCTowerDefList/Start/End/WaveStart`, `SCSkillCooldownReset` | `packets.rs` | done | Byte-exact tests. `SCLoadInstancePacket` is the npcs portal one. |
| Doodad funcs `EnterInstance`, `EnterSysInstance`, `ExitIndun`, `RemoveInstance`; skills hooks `leave_instance` (`ExitArchemall`) / `main_world_position` (`Return`); portal `UsePortal` court check | `npcs/doodad/funcs/world.rs`, `skills/ext.rs` (default bodies), `npcs/portal/mod.rs` | done | |
| Tower defense runtime, `towerdef` GM command, `TowerDefenseAttackerAiCharacter` | – | todo | The C# has no tower defense manager (only data + the GM command); commands and NPC AI are not ported. |

Hooks / TODOs left for other subsystems: mates `RemoveAndDespawnAllActiveOwnedMates` on dungeon entry (`dungeon::add_player`); slaves `GetActiveSlaveByOwnerObjId` in `PlayerCanEnter`; economy: the entry item goes through `npcs::portal::check_item_and_remove` (`EconomyBridge::consume_item`) and `SocialHooks::update_owned_housing_faction` on `SetFaction`. Instant games have no rewards in C#.
### systems::vehicles

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `MateGameData`, `SlaveGameData` (templates, unit modifiers, initial items / buffs, passive buffs, doodad / healing point / slave bindings, drop doodads, mount skills, repairable slaves, `slave_attach_points*.json`), `TransferGameData` (templates, bindings, binding doodads, paths, client `transfer_path.xml` roads + `GetOwnerPaths`), `ShipyardManager.Load`, `TaxationsManager` table, `ModelManager` vehicle / ship models | `systems/vehicles/data.rs` | done | Gated test `real_compact_db_vehicle_tables` checks the row counts; `real_transfer_follows_its_client_route` loads the roads from the game_pak. |
| `MateManager` (per instance), `CharacterMates` (load / save / spawn / despawn), `Mate` (`AddExp`, `MateXpUpdateTask`, `RegenTick`, `DoFallDamage`, `OnZoneChange`, stats) | `systems/vehicles/mates.rs`, `stats.rs` | done | Mount / dismount (`SCUnitAttached` / `SCUnitDetached`, rider becomes a transform child), rename, target, user state, mate equipment packet (item move through `VehicleItemHooks`), kill exp, stun / unmount skills, death despawn. |
| `SlaveManager` (per instance), `SlaveSpawner`, `SummonSlave` details, `Slave` (`DoDie`, `DoHpChangeTrigger` / repair points, `RegenTick`, `Save`, `OnZoneChange`, stats) | `systems/vehicles/slaves.rs` | done | Create (saved slave / persistent doodads / saved child slaves from MySQL, position, boats on the water surface, doodad and slave bindings at the model attach points), bind / unbind, delete with portal-time despawn, Rider's Escape, repair cooldown, `RepairSlaveEffect`, `SendMySlaveTask`, logout (+10 min) / disconnect despawn task, `slaves` table (`AfterCharacters`). |
| `TransferManager` (per instance, 100 ms tick), `TransferSpawner`, `Transfer` route following (`GoToPath`, `MoveTo`, `NextPoint`) | `systems/vehicles/transfers.rs` | done | Carriage motor + boarding part + bench doodads, `SCOneUnitMovementPacket` (transfer move type) for both parts. Spawned from `SpawnManager.SpawnAll`. |
| `ShipyardManager` runtime, `Shipyard` build steps, `CraftEffect` shipyard branches, `ShipyardTickTask`, `ShipyardCompleteTask` | `systems/vehicles/shipyards.rs`, `effects.rs` | done | Requirements via the skills item hooks + money (`SCItemTaskSuccess` money form). |
| `ShipHarpoonRopeController`, `CSUnhangPacket` | `systems/vehicles/harpoon.rs` | done | Tow forces are physics (`ShipHarpoonTowPhysics`): the per-tick entry points are exposed for the physics port. |
| `CSMoveUnitPacket` vehicle branches (ship request, vehicle, mate xp, riding, standing on objects) | `systems/vehicles/movement.rs`, `world/session.rs` | done | Sticky parents are not modelled. |
| Mate / slave / transfer / shipyard C2G and G2C packets | `systems/vehicles/packets.rs` | done | 18 C->S, 16 S->C (byte-exact tests). `CSChangeSlaveTargetPacket` has no 1.2 opcode (0xfff). |
| Hooks | `systems/vehicles/hooks.rs` | – | Installs wrappers over the skills `NpcSkillHooks` and the social `SocialHooks` (forwarding to the previously installed hooks); `VehiclePhysicsHooks` (AddShip / RemoveShip / water surface / IsWater) and `VehicleItemHooks` (mate equipment move, EndCraft) for the physics / economy owners. |
### systems::services

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `Services/WebApi/**` (`WebApiService`, `WebApiServer`, `WebApiSession`, `RouteMapper`, `RouteDefinition`, `WebApiGet/PostAttribute`, `IController`, controllers, models) | `systems/services/webapi/` | done | axum (HTTP/1.1) listener on `WebApiNetwork` (`*` = any); every request goes through `on_received_request` (`OnReceivedRequest`): unanchored, case-insensitive regex routes on the raw URL, `MatchCollection` with .NET group order, 404 "Not found", 500 HTML page. Responses use the Newtonsoft defaults (`json::JValue`), requests the System.Text.Json defaults (`json::Stj`). Status, World, Expedition, Character (MySQL), Command, Mail, Auction controllers. Ports of `RouteMapperTests` and `WebApiSessionTests` (+ controller tests). |
| `Services/DiscordBotService` | `systems/services/discord.rs` | done | twilight gateway + HTTP, unprivileged intents, "Playing with N other players" every 5 s, `/online`. Not started without `DiscordToken`. |
| `GameService`, `Program.UpTime`, `ManagerOrchestrator` | `systems/services/game_service.rs`, `main.rs` | done | Port of `GameServiceTests` and `ManagerOrchestratorTests`. |
| `TickManager` (+ `TickEventHandler`) | `systems/services/tick.rs` | done | Thread loop (20 ms), sync / async subscribers, busy skip. Port of `TickManagerTests`. |
| `TaskManager2`, `Models/Tasks/Task` | `systems/services/tasks.rs` | done | Delayed / repeating / cron tasks (NCrontab subset of `systems::npcs::schedule`), ids, cancel, `RemoveTasks`, `GetQueueCount`. Port of `TaskManagerTests`. |
| `AccountManager` remainder | `systems/services/accounts.rs`, `db.rs` | done | |
| `TimedRewardsManager`, `TimedRewardsTask` | `systems/services/timed_rewards.rs` | done | |
| `CharacterManager` remainder (`GetEffectiveAccessLevel`, `PlayerRoll` + `CSRollDicePacket` / `SCDiceValuePacket`, `StartOnlineTracking` / `CharacterOnlineTrackingTask`, `AddOfflineCrimePoints`, `ApplyBeautySalon` + `CSEnter/ExitBeautySalonPacket`, `CSBeautyshopDataPacket`, `SCCharacterGenderAndModelModifiedPacket`) | `systems/services/{character,beauty_salon}.rs` | done | Beauty salon item work through `BEAUTY_SALON_ITEMS` (economy). |
| Character packets: `CSActiveWeaponChangedPacket`, `CSBroadcastVisualOptionPacket`, `CSChangeAppellationPacket` (`CharacterAppellations.Change`), `CSIdleStatusPacket`, `CSRefreshInCharacterListPacket`, `CSRequestUIDataPacket`, `CSSaveUIDataPacket`, `CSUpdateActionSlotPacket`, `CSRestrictCheckPacket`, `CSSetLpManageCharacterPacket` and the log-only `CSCharDetailPacket`, `CSCleanupLogicLinkPacket`, `CSConsoleCmdUsedPacket`, `CSCriminalLockedPacket`, `CSEditorGameModePacket`, `CSRankCharacterPacket`, `CSRequestCharBriefPacket`, `CSRequestPermissionToPlayCinemaForDirectingMode`, `CSRequestSecondPasswordKeyTablesPacket`, `CSSetupSecondPassword`, `CSResturnAddrsPacket` (+ `SCActiveWeaponChangedPacket`, `SCUnitVisualOptionsPacket`, `SCAppellationChangedPacket`, `SCUnitIdleStatusPacket`, `SCRefreshInCharacterListPacket`, `SCResultRestrictCheckPacket`, `SCLpManagedPacket`) | `systems/services/character_packets.rs` | done | Byte-exact tests. `CSHangPacket` / `CSUnhangPacket` (sticky parents, harpoon) and `CSResurrectCharacterPacket` (resurrection state) are not ported. |
| `CharacterManager` `bag_expands` / `expand_expert_limits`, `Inventory.ExpandSlot`, `CharacterActability.Regrade` / `ExpandExpert`, `CSExpandSlotsPacket`, `CSExpandExpertPacket`, `CSUpgrade/DowngradeExpertLimitPacket` (+ `SCInvenExpandedPacket`, `SCExpertLimitModifiedPacket`, `SCExpertExpandedPacket`) | `systems/services/expands.rs` | done | Item checks / consumption through `CHARACTER_ITEM_OPS` (economy). |
| `CommandManager`, `AccessLevelManager`, `Models/Game/ICommand`, `Utils/Scripts/**` | `systems/services/commands/` | done (infra) | Ports of `SubCommandBaseTests`, `NumericSubCommandParameterTests`, `AccessLevelManagerTests`. AStar sub commands need `ASTAR_HOOKS` (AI path finding). |
| `ExpressTextManager`, `CSExpressEmotionPacket`, `SCEmotionExpressedPacket` | `systems/services/express_text.rs` | done | |
| `LocalizationManager` | `systems/services/localization.rs` | done | 263 635 rows / 8 locales on the real DB. |
| `ModelManager`, `Models/Game/Models/*` | `systems/services/models.rs` | done | Counts checked against compact.sqlite3 (gated test). |
| `MusicManager`, `SongData`, music packets (`CSSaveUserMusicNotesPacket`, `CSSendUserMusicPacket`, `CSRequestMusicNotesPacket`, `CSEndMusicPacket`, `SCUserNoteLoadedPacket`) | `systems/services/music.rs` | done | `CreateSheetMusic` item work through `SheetMusicInventory` (economy; its `ItemSkillHooks::midi_cache` / `create_sheet_music` call this manager). |
| `RadarManager`, `TelescopeRegistrationEntry`, telescope / fish finder packets | `systems/services/radar.rs` | done | Registered from `BuffTemplate.Start` / `Dispel` (skills), unregistered on leave. Ship lists through `RadarShipHooks` (vehicles). |
| `Models/Tasks/ShutdownTask`, `SCNoticeMessagePacket` | `systems/services/shutdown.rs` | done | Count-down notices, final save, process exit; held in `server.systems.services.shutdown_task` (`SaveManager.ShutdownTask`). Buy-back wipe through `BUYBACK_WIPE` (economy). |
| `SusManager` | `systems/services/sus.rs` | done | `audit_char_sus` log + speed analysis; the 5 s `FinalizeTransform` caller and the social / npcs log calls are not wired yet (see deviations). |
| `GameData/Framework` (`GameDataManager`, `IGameDataLoader`, `GameDataAttribute`) | `data/mod.rs`, `systems::SystemsData::load` | done (by design) | The reflection-driven loader list is the explicit `GameData::load` + per-subsystem `Data::load`; `Load` / `PostLoad` order is the call order there. |
| `Models/Tasks/SaveTask/SaveTickStartTask` | `managers/save.rs` | done (by design) | The periodic save is a Tokio interval instead of a `TaskManager` task (not counted in `GetQueueCount`). |
| `Utils/{MathUtil, NumericExtensions, Converters/*, DB/HoldablesSchemaCheck}` | `utils/` | done | `Utils/DB/SQLite*` = `data::sqlite`, `Utils/IdManager` = `managers::id`. `SphereCommandUtil` / `QuestCommandUtil`: scripts phase. |
### systems::ai

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `NpcManager.LoadAiParams` (NLua), `Models/Game/AI/v2/Params/**`, `AiGameData` (commands, command sets, chat bubbles, events), `ModelManager` (actor models + game stances, ship models) | `systems/ai/{lua,params,data}.rs` | done | A Lua 5.4 subset evaluator with `System.Convert` semantics; all 2 489 `npc_ai_params` rows produce the same values as NLua (differential harness, gated test `real_compact_db_params_match_nlua`; rows 443 / 835 / 1975 fail to parse in both). |
| `NpcAi` framework, `AiUtils.GetAiByType`, all `AiCharacters/**`, every `Behavior` (incl. Archer / BigMonster / Flytrap / WildBoar / AlmightyAttack / Talk / FollowPath / Return / Dead / Despawning / Spawning / Hold / Idle / Roaming / Alert / RunCommandSet / DoNothing) | `systems/ai/{framework,behaviors}.rs` | done | State machine, GoTo overrides, transitions, check intervals, skill queue / selection, aggression / alert / help, return, follow path, greetings (`SCNpcChatMessage` + `SCPlaySound`). |
| `AIManager` (100 ms tick, `AddAi`, region activity) | `systems/ai/manager.rs` | done | Per-AI mutex + queued calls (see deviations). |
| AI members of `Npc` / `Unit` (`MoveTowards`, `LookTowards`, `StopMovement`, `SetTarget`, `CurrentAggroTarget`, `CurrentGameStance`, `BaseMoveSpeed`, `OnDamageReceived`, `CheckIfEmptyAggroToReturn`, `ClearAllAggro`, `FindPath`), `NpcEvents` | `systems/ai/{npc,combat,skills,npc_events}.rs` | done | `SCOneUnitMovementPacket` movement, aggro tables through the skills API. |
| `AiPathsManager` (`Data/Path`), `AiPathHandler`, `Units/Route/Simulation` | `systems/ai/{path_handler,simulation}.rs` | done | Every `Data/Path/*.path` file loads (test). |
| `AiGeoDataManager`, CryEngine `.bai` readers (net / hide / areas / verts missions), A* `PathNode`, `GetHeight`, `ReducePath` | `systems/ai/geodata/` | done | `GeoDataMode` only. 35 828 pak `.bai` files parse; the areas reader matches the C# reader on every dumped file (differential harness); routes found on all 16 worlds with zone geodata. |
| `Gimmick.MoveTowards`, flytrap gimmick | `systems/ai/gimmick.rs` | done | |
| Hooks for npcs / skills (create AI by `ai_file_id`, spawn / engage / death / delete / faction, NPC control effect, spyglass) | `systems/ai/hooks.rs` | done | `AiNpcSkillHooks` wraps the `NpcSkillHooks` installed before `ai` starts. |
| `Physics/**`, `PhysicsManager` (ship hulls: Jitter2 world, `ShipController`, shore / ship–ship / doodad / cliff / static barrier contacts, buoyancy, whirlpool, storm lamps, harpoon tow, replication), `ShipSiegeAoEHit` geometry | `systems/ai/physics/` | done | The Jitter2 subset is bit-exact against Jitter2 2.8.8 for the integration (embedded reference test). Slaves call `add_ship` / `remove_ship` / `set_ship_request` / `set_ship_driver` / `ship_state` and may install `ShipPhysicsHooks` (harpoon rope state). |
| `WaterBodies`, `WaterBodyArea`, `Models/CryEngine/Objects` (`object.dat`) | `systems/ai/physics/{water,objects_file}.rs` | done | Water areas of all 1 294 client `object.dat` files equal the C# output bit for bit (differential harness, gated test `real_ai_object_dat_water_bodies`). Gimmick `FloatToSurface` uses them. |
| `ShipTuningDebug`, `HarpoonMechanicsDebug` logging, `ShipHarpoonRopeController` | – | not ported / slaves | Debug code is hard-wired off in the C#; the rope controller belongs to the slaves subsystem. |

## Known deviations from the C#

* `LGPlayerEnter` for an already registered connection id: C# `Dictionary.Add` throws inside the
  login handler, which has no catch (the exception escapes into the network client). Rust logs it,
  sends nothing (same as C#) and keeps the link up.
* `Character.Load` for a character with InfamyPoint >= 3000: the Wanted / Contemptuous buffs are
  added and the faction becomes Pirate as in C#; `SetFaction`'s side effects (house factions,
  `ExpeditionManager.Kick(this.Connection, ...)` — which throws on the null lobby connection and
  aborts `CSListCharacter` in C# — and the party kick) are not reproduced.
* `CheckForDeletedCharactersDeletion`: the rename / `deleted = 1` / packets are done;
  `DeleteCharacterAssets` (houses, guild, family, mail returns) is a later phase.
* `GameConnection.OnDisconnect` → `UpdateLoginTime(0)` for connections that never entered (inserts
  an `accounts` row with id 0 in C#) is skipped.
* Session ids are a counter, not `RemoteEndPoint.GetHashCode()`.
* Stats: `CalculateWithBonuses` is the identity (no buff / gear / passive bonuses) and equipment
  attribute contributions are 0, so MaxHp / MaxMp are the base unit formulas of the level. A missing
  unit formula evaluates to 0 (C# throws). `UpdateGearBonuses` is not ported.
* Buffs never expire on their own (no effect / dispel tasks), are never removed on move
  (`BuffRemoveOn.Move`) and have no effects; buff packets (`SCBuffCreated` etc.) are not sent.
  `Buffs.RemoveBuff` removes only the first matching effect, as in C# (phase 2 removed all).
* Phase 3a object model: C# virtual getters that read templates (`Npc.Scale`, `RaceGender`,
  `AnimActionId`, `MaxHp`, `Doodad.TimeLeft` funcs, `House.Template.*`) are stored fields that
  the owning subsystem keeps current. `DynamicBonus` copies `StartTime` / `Duration` of its
  source buff instead of referencing it. The NPC `SCUnitStatePacket` height adjustment
  (`GetReferenceHeight`) is not done (no heightmaps).
* `ChatIdManager` / `PrivateBookIdManager` now use the 1.2 C# ranges (`1..0xFFFE`,
  `0x1000..0xFFFFFF`); phase 2 had `1..0xFFFFFF` / `1..0xFFFFFF`.
  `save_rule_id` / `real_time` default to 0 / false when the `buffs` table lacks those columns.
* Quests: `Character.Load` reads the rows; `systems::quests::on_character_selected` builds the
  live quests from them (template check, `QuestInitialized` / `RequestEvaluation`, daily reset).
* Mail counts at select are always 0 — this matches the C#, whose `GetCurrentMailList` runs before
  the character is registered in the WorldManager and therefore never counts.
* `Character.OnZoneChange` into a closed or unknown zone: the system message is sent but the
  forced transport out (5 x `SCErrorMsg(ClosedZone)`, then `SCCharacterResurrected` at the closest
  return portal) is not done (needs PortalManager return portals / resurrection).
* Unknown faction ids: C# `Faction` is null (NRE in `Write` / `Faction.MotherId`); Rust writes the
  stored id and uses mother id 0 (-> the null chat channel).
* `CSMoveUnit`: only player characters exist, so slave / vehicle / mate / transfer moves are
  ignored like a non-matching `targetUnit` cast; standing-on / sticky parents, fall damage, Sus
  analysis and `LastPacketActivityTime` (inactivity kick) are later phases.
* `LeaveAllChannels` iterates channels in key order (C# `ConcurrentDictionary` order is
  unspecified). Trial channels do not exist.
* The C# keeps `Connection.Characters[id]` and `ActiveChar` as the same object; Rust copies the
  active character back into the lobby list when `LeaveWorldTask` runs.
* Combat / slave logout delays (x30, +10 min) are not applied (no combat state / slaves).
* systems::economy: item operations are serialised by the economy operation lock (`ItemTxn`) and
  their packets / quest events are flushed after the container locks are released, in the C#
  order; character packets sent during an operation go through the same queue (`Out`).
* systems::economy: `AddOrMoveExistingItem` with the item's own container as target relocates the
  item (the C# inserts the same object a second time into `Items`). `SplitOrMoveItemEx` looks up
  the source / target items in the two containers only (the C# `GetItemByItemId` searches every
  item; an item in a third container is treated as a phantom item). Where the C# throws
  (`KeyNotFoundException`, `NullReferenceException`, `AcquireDefaultItem` failure), the port logs
  and aborts the operation; `Wipe` drops an item `CanDestroy` refuses instead of looping forever.
* systems::economy: `GameStreamWorld.change_money` / `grant_ucc_item` (stream server) still change
  the money / bag without item task packets (`GameStreamWorld` has no `GameServer`; wiring it needs
  a `main.rs` change).
* systems::economy: trade items are kept by id (an item no longer in the giver's containers or put
  up twice is an error); a missing trade partner is ignored instead of half-applying the change.
* systems::economy: auction lots are enumerated in id order (`ConcurrentDictionary` order in C#),
  search / cheapest lot skip lots whose item or template is missing; mails are kept in a map that
  reproduces the C# `Dictionary` enumeration order; mail dates are whole unix seconds.
* systems::economy: `CSSetCraftingPayPacket`, `CSChangeItemLookPacket`, `CSSpecialtyRecordLoadPacket`,
  `CSBuySpecialtyItemPacket`, `CSReturnMailPacket` have no 1.2 opcode (0xfff): decoders exist, the
  1.2 client never sends them. The specialty / trade-pack mail delay use the C# defaults (no
  `Specialty` config section yet).
* systems::economy: the localized item names are loaded by four modules (items search, auction,
  mail, cash shop); a later cleanup can share one cache.

* systems::quests: rewards, items, money, exp, skills, NPC faction, teams, mails go through
  `quests::hooks::QuestHooks` (no-op defaults until the owning subsystems install theirs).
  World effects (component skills, NPC faction, team-shared events) run after the character
  lock is released instead of inline; item hooks that raise quest events are queued to the
  end of the running operation.
* systems::quests: model sizes are 0 (no model data) in `GetAround` / distance checks; buff tags
  are not loaded (`CheckBuffTag` false); `EquipShield` unit req is false; labor margin uses the
  non-premium cap 5000; debug messages use the character access level only.
* systems::quests: C# exceptions inside quest code are modelled where reachable with the 1.2
  data (`SetObjective` on a non-objective act, `MaxObjective` division by zero, the timer step
  switch): they abort the event invocation list / sphere tick. Out-of-range objective reads
  that would throw in RunAct / score sums read 0 (no 1.2 quest triggers them).
* systems::quests: `CSCompletedCinemaPacket` does not call `ResendVisibleObjectsToCharacter`.
* systems::quests: the daily reset runs at 00:00 UTC (C# cron in server local time).
* systems::social: saves of guilds / families run inline in the handler (C# synchronous); a
  failed guild save keeps the removed-member ids for the next attempt. `ExpeditionConfig`
  defaults to the shipped `Expedition.json` (C# throws without the section). Player names /
  positions in team packets are snapshots taken when the packet is built. `DeleteCharacterAssets`
  does not send the guild/family packets to the lobby character. Duel flag height uses the hook
  (no height maps). Crime record packets prefetch offline victims (C# `Character.Load` inside
  `Write`). `SCDoodadPhaseChangedPacket` is defined in `systems/social/packets/duel.rs` until the
  npcs subsystem's version can be used.
* systems::skills: supersedes the phase-2 bullets above on stats (`CalculateWithBonuses`,
  `UpdateGearBonuses` are ported), buffs (expiry, `BuffRemoveOn.Move`, effects, buff packets) and
  `CSMoveUnit` fall damage.
* systems::skills: `CompressedGamePackets` (DD04) are written with stored (uncompressed) deflate
  blocks; the client accepts any valid deflate stream.
* systems::skills: no heightmaps — fall-back Z / line-of-sight checks are skipped, the radar
  (`SkillTargetingUtil` radar scans) is not ported.
* systems::skills: `SpecialEffect` repeats are scheduled tasks instead of `Thread.Sleep` loops.
* systems::skills: labor is capped at 2000 (non-premium); premium accounts do not exist yet.
* systems::skills: exp loss on death is 0 (the C# formula reads data that is not loaded).
* systems::skills: skill controllers and NPC displacements (knock back, impulse, blink) do not
  snap to the ground (no `GeoData.GetHeight`; the npcs branch has `systems/npcs/heightmap.rs` to
  wire at merge); the wandering (fear) speed is the 7 m/s fallback (actor model stances not
  loaded); `Npc.StopMovement` writes the C# default stance / alertness; `OnMovement` is not raised.
* systems::skills: `BubbleEffect` does not sleep the effect thread for the bubble's read time;
  account updates of special effects run as async tasks.
* systems::skills: `saveFactions` KeyNotFound (C# throws) is logged as an error.
* `systems::npcs`: doodad func timers are tokio tasks (cancel = generation bump); `DoodadManager.Create`'s `Task.Run(InitDoodad)` is a spawned task; `TimeManager.OnTimeOfDayChange` runs from a 10 s poller. `GetNpcSpawner` template rewrites do not propagate to already loaded spawners; the "first" concurrent-dictionary entry is the lowest key; spawn files are processed in sorted name order (C# `Directory.GetFiles` order is unspecified). Portal book deletions are written at the next periodic save. Debug messages check only the character's access level. Coffer slot counting stops at 255 (C# loops forever). Where the C# would throw on a null / failed cast the Rust code returns quietly. Geodata (navmesh heights), physics, water, sticky parents, AI, transfers / slaves spawning and housing are not ported.
* systems::housing: dates are stored with second precision (unix seconds); `DateTime.MinValue`
  rows load as 0, so a manually inserted house with both dates at MinValue gets 1970-01-15 as
  protection end (C#: 0001-01-15; both are "expired"). `Connection.Houses` is not snapshotted:
  `SCLoginCharInfoHouse` / `SCMyHouse` read the account's houses when they are sent. A house row
  with an unknown template is skipped (C# NRE aborts the load). The non-persistent bound doodads
  of a deleted house are removed from the world's object table (the C# leaks them). Sale signs
  fall back to the house Z when no heightmaps are loaded. The house `Expedition` (owner's guild)
  is stored on ownership changes instead of being read live. Where the C# throws inside an
  operation (null decoration design / doodad template / taxation, missing furniture item), the
  operation is aborted and logged.

* systems::instances: the dungeon loader awaits the spawn tasks before registering the indun events again (C# does not await `Task.WhenAll`, a race) and reads the spawn files there instead of in `CreateWorldInstance`; `DestroyInstantGame` cleans the instance up and releases its id right away (C#: the finalizer, later); `Environment.TickCount` is a process-relative monotonic clock; the `World.Template.SpawnPosition` mutation in `MoveCharacterTo*` is a local value; paths that throw in C# (missing action / room / zone / rule set, `_members[x]` of a non-member, a destroyed dungeon's `World`) log and return, keeping the side effects done before the throw; `EndGame().Start()` throwing after a victory is modelled as "the rest of `OnKill` (respawn) does not run"; the cross-instance region switch removes the object from every old neighbour once (no double notification of its own sector).

* systems::vehicles: supersedes the phase-2 `CSMoveUnit` and `LeaveWorldTask` bullets above:
  slave / vehicle / mate moves, riding, standing on objects, the mate / slave logout handling
  and the +10 minute logout delay with an active vehicle are ported (combat x30: skills).
* systems::vehicles: the transfer motor is not a transform child of its boarding part (the C#
  sets and immediately clears that parent); the boarding part is moved explicitly by the motor's
  tick, as the C# does for its packets. Sticky parents (ladders, boarding) are not modelled, so
  `CSUnhangPacket` always reports target 0 and breaks no rope.
* systems::vehicles: `Transform.Parent = null` keeps the world position (`detach_child`); the C#
  keeps the local values until the next position update.
* systems::vehicles: `Thread.Sleep(50)` between `SCMateSpawnedPacket` and the mate spawn is not
  reproduced. Mate / slave gear bonuses (`UpdateGearBonuses`) and slave weapon dps are 0
  (economy). Ship physics, water bodies and the weather storm-ship skill block are hooks /
  not ported.
* systems::vehicles: a stale (already deleted) mate left in the active list by the logout bug
  below is despawned again without releasing its obj / tl ids a second time.
* systems::vehicles: DB failures of the saved-slave lookups behave like "no saved slave"; the
  slave / mate saves outside `DoSave` are fire-and-forget tasks.
* systems::vehicles: a missing unit formula evaluates to 0 (C# NullReferenceException); index
  errors of `NextPoint` / `ShipyardSteps[-1]` are logged and end the call (C# exceptions).

* systems::services: the WebApi 500 page shows the Rust error chain instead of a .NET stack
  trace, and System.Text.Json / NotSupportedException messages are close paraphrases. Character
  `CreatedAt` of characters created in the running session is written without the `Z` of a
  `DateTimeKind.Utc` value. The mail / auction controllers answer "not found / not sent" until
  the economy installs `WebApiEconomyHooks`.
* systems::services: `TaskManager.GetQueueCount` (WebApi `/status`) only counts the tasks of
  this queue; the other subsystems run their timers as Tokio tasks.
* systems::services: `TimedRewardsManager.DoTick` skips connections without an account (C#
  `GetAccountDetails(0)` inserts an `accounts` row 0); the payment of a connection reached from
  the tick is the default `AccountPayment` (it is never changed in C# either).
* systems::services: `AppConfiguration.Instance` mutations (World set commands,
  `ReloadConfiguration`) replace the `ConfigCell` snapshot; subsystems that copied values at
  start (skills config, doodad growth rate) are updated by the World set commands only.
* systems::services: `SusManager` analysis runs only where callers pass positions (no
  `FinalizeTransform`); `systems::social::crime::sus_log` and the npcs coffer check still only
  log (switch them to `server.systems.services.sus.log_activity_player`).
* systems::services: `QuestCommandUtil` / `SphereCommandUtil` and every `Scripts/Commands`
  command are not ported (scripts phase); `scripts reload force` runs the installed loader.
* systems::services: the Discord bot runs in the background (the C# `StartAsync` never returns
  when a token is set, blocking the host start).
* systems::ai: calls into an NPC's AI from outside its tick (skills, aggro, npcs hooks) are queued and run right after the current AI step instead of re-entering it (`manager::post`); the AI tick runs on a blocking thread every ~100 ms; behaviour instances are kept per AI and accessed by kind. `UpdateAggroHelp` reaching a helper NPC without an AI logs and continues (the C# throws a `NullReferenceException` that cuts the whole behaviour tick short). `Npc.OnDamageReceived` on an AI-less NPC does nothing (C# NRE).
* systems::ai physics: Jitter's narrow phase (`MprEpa`) and contact solver are replaced by a box-vs-heightmap-triangle SAT with one contact per triangle and sequential impulses (Jitter's default friction / bias); the integration itself is exact. One physics world per instance is created with the first ship (C# starts a thread per instance at boot). Add / remove / helm / driver changes go through the pending queue (next tick); hull damage, movement packets and transform writes are applied after the tick; readers use per-tick snapshots. Water is kept per world template and a cell's `object.dat` is ingested when the physics or a water query sees the cell loaded. A height lookup error counts as height 0 (the C# exception skips the rest of that ship's tick). A ship whose game object is gone is skipped. Ship–ship harpoon tow resolves an explicit hook basis among the ships simulated this tick.

## C# bugs noticed (mirrored, not fixed)

* systems::social: `AskRiskyTeam` makes the acting unit (the kicker) leave its chat channels;
  any member may dismiss a team; a member joining an existing team never joins the party / raid
  chat (but can still talk into it); `IsMarked` compares mark object ids with character ids;
  `ExpeditionManager.ChangeOwner` does not update `OwnerName`; `ReplyInvite` does not check an
  existing guild; `ChangeExpeditionRolePolicy` requires the Expel right; disbanded guilds stay in
  the list; family invitations always carry family id 1; family title / steward changes are not
  saved; hard disconnects keep the family member's character set; `Character.AddCrime` wraps on
  overflow; `GetTrialChat` throws when the faction has no court and the player is in no trial;
  `CSAddFriend` accepts yourself.

* `WorldManager.RemoveVisibleObject` calls `RemoveFromCharacters` on the object's own region and
  then again for every neighbour, which includes that region: players in the same sector receive
  `SCUnitsRemovedPacket` (or `SCDoodadRemovedPacket`, ...) twice (and the leaving player twice the
  list of its sector).
* `WorldManager.AddVisibleObject` on a first placement shows the children through
  `GameObject.AddVisibleObject` and then places each region-less child itself, whose own
  `AddToCharacters` shows it again: a child is sent twice.
* `Region.RemoveFromCharacters` for a character: gimmicks are units, so their ids are in both
  `SCUnitsRemovedPacket` and `SCGimmicksRemovedPacket`.
* `House.RemoveVisibleObject`: the `SCDoodadsRemovedPacket` chunk offset is
  `i * MaxCountPerPacket` with `i` already stepping by `MaxCountPerPacket`, so with more than 400
  attached doodads the second chunk throws; only the first chunk is sent (an error is logged).
* `Region.AddObject` / `RemoveObject` swap-remove: an object that leaves a sector moves the last
  entry into its slot, which changes the order other players later receive the sector in.
* `new SCFactionListPacket()` / `new SCFactionRelationListPacket()` (empty lists) use the
  hard-coded type ids 0x007 / 0x008, which are `SCTrionConfigPacket` / `SCFactionListPacket` in
  the 1.2 table.
* `Character.Save` binds `@reported_as_bot_count` but the column is missing from the REPLACE list:
  the column is reset to its default on every save. The rotation columns are written crosswise
  (`roll` <- Rotation.Z, `yaw` <- Rotation.X), consistently with `Load`, so the round trip works.
* `CharacterSkills.Load` re-learns stored skills through `AddSkill(id)`, re-checking the ability
  tree and skill points and recomputing the level (the stored level is ignored).
* `CharacterManager.Load`: `character_supplies` creates `_abilityItems` entries that
  `character_equip_packs` then `Add`s again (throws for an ability with both).
* `FriendMananger.Load` keys all friends by `id` although the table key is `(id, owner)`.
* `ItemManager.LoadUserItems`: a stacked item (merged into an existing stack while loading) keeps
  its own DB row, which is loaded again next time.
* `SCUnitStatePacket.Write` adds buffs 8000011 / 8000012 to the character ("Wrong place"), and an
  exception inside any `Write` (e.g. no `VisualOptions.Stp`) truncates the packet, because
  `PacketStream.Write(PacketMarshaler)` swallows it. Both reproduced.
* `SCUnitMovementsPacket` writes no move-type byte per entry (unlike `SCOneUnitMovementPacket`).
* Hard disconnects do not leave chat channels (only `LeaveWorldTask` does).
* systems::quests: `QuestActConReportDoodad.FinalizeQuest` subscribes again (`+=`) instead of
  unsubscribing; `QuestActCheckSphere.FinalizeAction` removes sphere triggers by the quest's DB
  id instead of its template id; `QuestActCheckSphere` is not an objective, so its sphere
  handlers throw on `Objectives[255]` (aborting the sphere tick).
* systems::quests: `QuestActCheckTimer.OnQuestStepChanged` does not check `args.QuestId`: any
  quest reaching Fail / Ready / Drop / Reward cancels every running quest timer of the owner;
  `OnTimerExpired` fails its own quest whatever quest's timer expired.
* systems::quests: `QuestActObjAggro` reads `OnKillArgs.Target`, which the C# only fills with
  the attacker itself: aggro quests never progress for players.
* systems::quests: `QuestActObjZoneMonsterHunt` (non-score) needs `count > Count` but the
  objective is capped at `Count`: never completes.
* systems::quests: `AddQuest(forcibly)` drops a completed quest with `DropQuest(id, true)` (not
  forcibly): the completed flag stays set. `AddQuest` removes a running timer entry without
  cancelling its task. A quest that fails `StartQuest` leaks its quest id.
* systems::quests: a completed quest is removed through `DropQuest`, so `QuestCleanup` and
  `QuestDropped` both consume its supply items.
* systems::quests: `QuestActConReportNpc.RunAct` is true while the giver NPC is targeted, so a
  quest accepted at an NPC is turned in as soon as it is Ready while still targeting it.
* systems::quests: the shared `_questStartingLastPositionChecks` is updated per starter
  sphere, so only the first starter near a player can trigger; `SphereQuestManager.Tick`
  adds a trigger even when it already exists (the duplicate check's `break` is a no-op).
* systems::quests: `PacketStream.ReadDateTime` returns `DateTime.MaxValue` for any stored
  quest time above 59 s (`LeftTime` then saturates to `int.MaxValue`).
* systems::quests: `SphereGameData` reads `spheres.enter_or_leave` / `or_unit_reqs` with
  `GetBoolean(name)` (int conversion of 't'): always false.
* systems::skills: `Buffs.RemoveBuff` path sends `SCBuffRemoved` twice (Exit + Dispel).
* systems::skills: a target-charged buff uses the caster template's `ChargedBuffId`.
* systems::skills: `UnitRequirements` with `OrUnitReqs` pass as soon as the first requirement is
  evaluated (the C# loop returns early).
* systems::skills: `GetDistanceTo` radius precedence (`a - b + c` instead of `a - (b + c)`).
* systems::skills: buff triggers subscribe to the caster's events, not the owner's.
* systems::skills: `knockback_immune` is read with `GetBoolean` on a `'t'/'f'` column (always false).
* systems::skills: special `Charge` creates the buff with the target as owner but adds it to the
  caster, and its final charge only reaches a newly added buff (a refresh keeps the first value).
* systems::skills: special `SpawnDoodad` moves along `target yaw + 90°` and passes degrees to
  `SetPosition` (which takes radians).
* systems::skills: special `Escape` in an instance sends the spawn position's roll as the yaw.
* systems::skills: special `Return` sends `SCLoadInstance` for instance 1 while the transform uses
  instance 0, converts an already-radian `MainWorldPosition` rotation with `DegToRad` again, and
  only sends `SCTeleportUnit` when a return point was found.
* systems::skills: special `RemoveDoodad` broadcasts `SCDoodadRemoved` after deleting the doodad
  (it reaches nobody).
* systems::skills: special `AddBreath` adds `uint + int` as `long` and truncates back to `uint`.
* `systems::npcs`: a generated (fake) spawner's first member never spawns; the "spawn all" schedule check is inverted; `DoRandomSpawn` leaks an obj id and spawns twice; a doodad despawn may release its obj id twice; gimmick spawn rotations are not converted from degrees; scheduled doodad spawns stack cron tasks; the player-in-radius check scans every player; NPCs are not despawned when players leave.
* `systems::npcs`: dated game schedules compare `StartTime` / `EndTime` (usually 0) instead of the start / end hours (a one-day event counts as ended at 00:00); the cron builder tests the captured day of week instead of its parameter, and undated schedules produce day / month 0 (rejected by the cron parser); `VendorExist` reads the `id` column; deleting a portal sends no packet; `NotifySubZone` re-sends the portal book once per recall portal of the sub zone; the exit portal's yaw is the book rotation in degrees.
* `systems::npcs`: `DoodadFuncLootItem` with coins (item 500) and `DoodadFuncBuyFish` add the money twice; a trade pack whose backpack take-off fails still counts as looted; `CheckGrowth` runs funcs with `nextPhase = 0`; some `doodad_func_*` loaders store templates in the phase dictionary (e.g. `DoodadFuncBuyFishItem`), mirrored; `DoodadPhaseFunc.PulseTriggered` is state shared by every doodad of the group.
* systems::economy: `AddOrMoveExistingItem` merging an item from another container into a stack
  removes it from the source without releasing it (the DB row stays); it is kept as a loose item.
  The mail branch of the acquire / consume events reads `item.SlotType` after it was overwritten,
  so moving an item into a mail container raises no quest event.
* systems::economy: `SplitOrMoveItemEx` gives the split item owner 0, assigns the *source* owner to
  the moved source item and passes `fromSlot` as the previous slot of the target item's leave
  event; `CSWithdrawMoneyPacket` reports `ItemTaskType.DepositMoney`; `ExpandSlot` never sends its
  `MoneyChange` task.
* systems::economy: `BindType.HasFlag(...)` on the plain `ItemBindType` enum is a bit test
  (`BindOnUnpack` matches 4..7, `BindOnEquip` matches 3 and 7); mirrored.
* systems::economy: `CSBuyItems` refuses only when money, honor *and* vocation are all short;
  `ExpandExpert` adds the vocation cost; craft / backpack / weapon booleans read as numbers are
  false for `'t'` texts; `Specialty.VendorExist` reads the `id` column.
* systems::economy: loot dice pick the lowest positive roll; looting allocates a new item id that
  is never released; `ItemProc` `ignoreRoll` never procs; `HDps` uses the magic DPS grade value;
  fish conversion only uses the first pack.
* systems::economy: auction — anyone can cancel a lot without bids, cancel mails a new copy while
  the listed item stays in the auction container, posting fees / bids do not check the balance,
  the "ExpireDate" sort uses `PostDate`, a buyout sends no `SCAuctionBidPacket`.
* systems::economy: mail — no ownership check on read / take / delete by id (only
  `CSTakeAttachmentSequentially` checks), opening the mailbox re-sends a "got mail" per unread mail,
  `ReturnMail` fails on mails with fewer than 10 attachments, `FinalizeForBidFail` uses
  `AucOffFail`, receiver lookup is case-sensitive.
* systems::economy: trade — `OkTrade` proceeds when only one side locked, the put-up amount is only
  checked (the whole item changes hands), negative money put-ups are accepted, trade ids are never
  released, the owner's `ItemRemove` in `SCTradeMade` names the receiver's slot.
* systems::economy: cash shop — loyalty is charged at the undiscounted price, the max-level check
  sends the low-level error, page 0 behaves like page 1.
* `systems::instances`: `RegisterIndunEvents` runs in the `Dungeon` constructor and again in the loader, so every indun event handler is subscribed twice; a dungeon kill raises `OnUnitKilled` (and the victim's `OnDeath`) in `PostUpdateCurrentHp` and again in `DoDie` — NPC-killed action chains run four times; `IndunEventDoodadSpawneds.UnSubscribe` adds the handler (`+=`); `AreaClearTick` stops at the first cleared room (`return`); a new dungeon verifies the requirements twice (the entry item is consumed twice) and the creator gets `SCProcessingInstancePacket` twice; `Transform.CloneDetached(character)` / `MainWorldPosition.Clone()` fire `OnZoneChange(0, zone)` (the instance zone chat is never left); `ApplyWorldSpawnPosition` stores the spawn rotation in degrees; leaving an instance sends the (radian) return rotation through `DegToRad` again and `SCLoadInstancePacket` names `MainWorldPosition.WorldId`; `DestroyDungeon` removes the players from the instance without moving them out; `InstantGame.RemovePlayer` looks the corps lists up by 0 / 1 instead of the faction ids (never cleaned); `AddScore` always sends `Corps1` / `score2 = 0`; the kill packet's victim count is the victim corps' kills; entering a battlefield sets the corps faction, so `Character.SetFaction` kicks guild members of another mother faction from their guild and party; offline queue applicants are never pruned (`CharObj` is never null); `RemoveGame` leaves the game in the queue list; `TowerDefGameData` / battlefields.json loops stop (`return`) at the first orphan row.
* systems::housing: `CalculateBuildingTaxInfo` returns before computing anything when the
  account has no house yet, so the first house is free (no tax, no deposit) and the
  construction tax window shows 0; demolition returns the deposit anyway. `CSSellHousePacket`
  does not check that the seller owns the house, and looks the buyer name up without
  normalizing it. `Family` / `Guild` permission falls through to "public" for players without a
  family / guild. `SCHouseTaxInfoPacket.isAlreadyPaid` is true when payment is *required*.
  The demolition money (deposit) is only mailed when at least one item is returned.
  `CraftEffect` on a finished house throws (`BuildSteps[-1]`). The `BaseAction` sum is not
  reset when the step goes back to 0 / -1. `SetForSale` on a house already for sale adds a second
  set of sale signs. `RemoveDeadHouse` queues the house id for deletion a second time.
* systems::vehicles: `MateManager.RemoveAndDespawnAllActiveOwnedMates` removes the active-list
  entries by `OwnerObjId` while the list is keyed by the character id, so the deleted mates stay
  listed; after a relog the first whistle use only "despawns" the stale mate.
* systems::vehicles: `ShipyardCompleted` builds a `SkillItem` with only `ItemId`, so
  `SlaveManager.Create` returns on its `ItemTemplateId == 0` check: the finished ship is never
  summoned (the owner's active vehicle is still removed).
* systems::vehicles: a slave's `TlId` is never released; the transfer spawner's `Despawn` and
  the spawn manager's despawn tick both release the transfer's obj id.
* systems::vehicles: `SummonSlave.ReadDetails` always fails on `RepairStartTime` (so the repair
  cooldown does not survive a restart); `DoodadFuncRecoverItem` looks the owning slave up by its
  DB id as an obj id; `GetActiveMates(...).FirstOrDefault()` decides the `Raid` relation mate.
* systems::vehicles: `Unit.DespawnMate` on a mate's own death only despawns when its owner is
  dead; `UnitMoveType` without the standing flag detaches a driver from its vehicle.
* systems::vehicles: `SpawnEffect` (slave) writes `PosDistance = 2` back into the template and
  `Rotate(Rotation with { Z = .. })` doubles the roll / pitch; an owner-less boat throws in
  `Create` after spawning (`GetWorld(owner.Transform.InstanceId)`).
* systems::vehicles: `CSChangeMateUserStatePacket` / `ChangeStateMate` sends nothing (the packet
  is commented out in C#); `RenameMount` broadcasts the un-normalized name.
* systems::services: `RemoveDeadConnections` shuts down every account connection that has
  not sent a `PingPacket` within 30 s, including connections that never pinged (`LastPing =
  DateTime.MinValue`).
* systems::services: `WorldSetLootrateSubCommand` reports "Set GrowthRate {value}";
  `SaveManager.SetAutoSaveInterval` only stores the delay (the running save task keeps its
  interval); `AStarPathFindingSubCommand` reads its optional `templateId` with `parameters[...]`
  (throws when missing); the AStar point commands look for an "ObjId" parameter that is never
  declared.
* systems::services: `MathUtil.GetDistance(.., point3d: true)` adds the Y difference twice;
  `RotatePoint3D` builds its quaternions with the (vector, scalar) constructor and discards the
  conjugate; `NumericExtensions.ToCellOffset` / `ToPathsOffset` subtract the cell index.
* systems::services: `MailController` clamps the request's attachment counts in place (the
  clamped value carries over to the next recipient); `AuctionController.SearchAuctionItems`
  throws on lots without bidder when filtering by `BidderName`; `CommandController` passes the
  raw regex group (query string included) as the command name.
* systems::services: `Inventory.ExpandSlot` deducts the price without sending the `MoneyChange` item
  task; `CharacterActability.ExpandExpert` adds the vocation cost (`ChangeGamePoints(Vocation,
  +LifePoint)`) instead of paying it; `CSBroadcastVisualOptionPacket` keeps the members the new
  flag does not mention.
* systems::services: `CSExpressEmotionPacket` passes the animation id as the emotion id to
  `DoOnExpressFireEvents`; `SCUserNotesLoadedPacket` writes the UTF-16 length of the song.
* systems::ai: AI params are shared mutable objects (`RestorationOnReturn` / `GoReturnState` changes leak between NPCs of the same params row); `CheckIfEmptyAggroToReturn` inspects the abuser instead of the NPC; `IdleBehavior` enters combat and then still runs the talk greeting; the archer's delay uses `CastingTime` as seconds; the big monster's hovering phase rises 15 m per tick; `FindPath` sends debug chat lines; `ExtraLink` shapes are added twice; `UsedAreaNames` is global across worlds; `Gimmick.CurrentTarget` is never read.
* systems::ai physics: the heightmap collider's bounds use the sample count as metres, so only the first half of each world axis has terrain contacts; `SendUpdatedMovementData` applies the Jitter quaternion to the transform without the Y/Z swap of `SyncTransformWithRigidBody`, so between ticks a ship's yaw sits in the pitch slot of its transform; the dry-land "roll correction torque" is added as a force and the beached "anti-gravity" force is not mass-scaled; ships are only simulated while `Transform.WorldId` equals the instance id; the buoyancy sample points are computed but unused.
