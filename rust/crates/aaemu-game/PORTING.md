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
                                             SCCharacterList x ceil(n/2), (SCLoginCharInfoHouse per house: todo)
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
| `Core/Managers/ItemManager` (templates + user items) | `data/items.rs`, `managers/item.rs`, `models/item.rs`, `models/container.rs` | partial | Item templates of every class (durability / slot / bind / stack data), grades, item config; LoadUserItems (all containers and items in memory), GetItemContainerForCharacter, Create (subclass ctor incl. MaxDurability and the plain-Item fallback), ReleaseId, Save (dirty containers/items, deleted rows). Loot, enchanting, sets, procs, timers, item tasks todo. |
| `Models/Game/Items/Containers` (ItemContainer, EquipmentContainer) | `models/container.rs` | partial | Slots, GetUnusedSlot, AddOrMoveExistingItem for new items (stacking, equipment slot rules, CanAccept), AcquireDefaultItem, ApplyBindRules, GetSlottedItemsList. Moving between containers, consuming, item task packets todo. |
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
| `Core/Managers/HousingManager`, `Models/Game/Housing` | – | todo | Needed for `Connection.Houses` / `SCLoginCharInfoHouse` / `SCMyHouse`. |
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

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| Item actions / tasks, craft, loot, mail, auction, trade, cash shop | `systems/economy/` | todo | |

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
| `CommandManager`, `AccessLevelManager`, `Models/Game/ICommand`, `Utils/Scripts/**` | `systems/services/commands/` | done (infra) | Ports of `SubCommandBaseTests`, `NumericSubCommandParameterTests`, `AccessLevelManagerTests`. AStar sub commands need `ASTAR_HOOKS` (AI path finding). |
| `ExpressTextManager`, `CSExpressEmotionPacket`, `SCEmotionExpressedPacket` | `systems/services/express_text.rs` | done | |
| `LocalizationManager` | `systems/services/localization.rs` | done | 263 635 rows / 8 locales on the real DB. |
| `ModelManager`, `Models/Game/Models/*` | `systems/services/models.rs` | done | Counts checked against compact.sqlite3 (gated test). |
| `MusicManager`, `SongData`, music packets (`CSSaveUserMusicNotesPacket`, `CSSendUserMusicPacket`, `CSRequestMusicNotesPacket`, `CSEndMusicPacket`, `SCUserNoteLoadedPacket`) | `systems/services/music.rs` | done | `CreateSheetMusic` item work through `SheetMusicInventory` (economy; its `ItemSkillHooks::midi_cache` / `create_sheet_music` call this manager). |
| `RadarManager`, `TelescopeRegistrationEntry`, telescope / fish finder packets | `systems/services/radar.rs` | done | Registered from `BuffTemplate.Start` / `Dispel` (skills), unregistered on leave. Ship lists through `RadarShipHooks` (vehicles). |
| `Models/Tasks/ShutdownTask`, `SCNoticeMessagePacket` | `systems/services/shutdown.rs` | done | Count-down notices, final save, process exit; held in `server.systems.services.shutdown_task` (`SaveManager.ShutdownTask`). Buy-back wipe through `BUYBACK_WIPE` (economy). |
| `SusManager` | `systems/services/sus.rs` | done | `audit_char_sus` log + speed analysis; the 5 s `FinalizeTransform` caller and the social / npcs log calls are not wired yet (see deviations). |
| `Utils/{MathUtil, NumericExtensions, Converters/*, DB/HoldablesSchemaCheck}` | `utils/` | done | `Utils/DB/SQLite*` = `data::sqlite`, `Utils/IdManager` = `managers::id`. `SphereCommandUtil` / `QuestCommandUtil`: scripts phase. |

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
* `ItemContainer.ApplyBindRules` / `OnEnterContainer` packets (`SCItemTaskSuccess`,
  `SCUnitEquipmentsChanged`) are not sent; in this phase they only happen without a connection or
  outside the world, where the C# sends nothing either.
* The C# keeps `Connection.Characters[id]` and `ActiveChar` as the same object; Rust copies the
  active character back into the lobby list when `LeaveWorldTask` runs.
* Combat / slave logout delays (x30, +10 min) are not applied (no combat state / slaves).

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
* systems::services: `CSExpressEmotionPacket` passes the animation id as the emotion id to
  `DoOnExpressFireEvents`; `SCUserNotesLoadedPacket` writes the UTF-16 length of the song.
