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
| `Core/Network/Stream` + `Packets/C2S`, `Packets/S2C` (stream server) | `stream/` | done | StreamNetwork listener on `StreamNetwork.Host:Port`, `[u16 len][u16 op][body]` framing, connection table, all 17 CT and 15 TC packets (byte-exact). World lookups go through `stream::world::StreamWorld`, implemented by `stream_world::GameStreamWorld` (names, items/UCC ids, ActiveChar, doodads of a cell via `WorldInstance::objects_in_cell`). Truncated packet bodies close the connection (C# reads zeros). |
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
| `Models/Game/World/GameObject`, `Models/Game/Units/{BaseUnit,Unit,IBaseUnit,IUnit,BaseUnitType,UnitTypeFlag,ModelPostureType}` | `models/unit.rs`, `world/object.rs` | partial | All data members (`UnitBase`), `Hpp`, `IsDead`, `NeedsRegen`, trigger counts, `CalculateWithBonuses`, `UnitData` / `BaseUnitData` traits, handles (`Entity<D>`, `WorldObject`). Behaviour (`ReduceCurrentHp`, `DoDie`, `CanAttack`, regen, aggro, `UseSkill`, `SetFaction`, `UpdateGearBonuses`, the `SetPosition` underwater state) lives in the phase-3 subsystems (`systems::skills`). |
| `Models/Game/Units/{UnitCooldowns,CombatBuffs,UnitAttribute,UnitModifierType}`, `Models/Game/Skills/{Bonus,DynamicBonus,SkillModifier(s),BuffModifier(s)}`, `Skills/Static/{SkillAttribute,BuffAttribute}`, `Templates/{BonusTemplate,DynamicBonusTemplate,LinearFuncTemplate}` | `models/unit.rs` | done / partial | Data model + pure logic (`ApplyModifiers`, `Evaluate`, cooldown checks). The DB loaders for the rows and `TriggerCombatBuffs` are the skills subsystem's. |
| `Models/Game/World/Transform` (`Transform`, `PositionAndRotation`) | `models/transform.rs`, `world/mod.rs` | partial | Local position/rotation, `ToRollPitchYawSBytes/Shorts`, quaternions, `GetWorldPosition` parent composition (`WorldInstance::world_pos_rot`), `GetRootObj`, attach / detach children, sticky parents (`ObjectCore::sticky_parent`, `WorldInstance::set_sticky_parent`: the C# never fills `StickyChildren`, so a sticky parent is a marker plus a real parent). `FinalizeTransform`'s 5 s Sus analysis runs from the move handler (`SusManager::finalize_transform`). Debug trackers are not ported (debug only). |
| `Models/Game/NPChar/Npc`, `Units/Portal`, `DoodadObj/Doodad`, `Units/{Slave,Mate,Transfer}`, `Housing/House`, `Shipyard/{Shipyard,ShipyardData}`, `Gimmicks/Gimmick` | `models/{npc,doodad,slave,mate,transfer,house,shipyard,gimmick}.rs` | partial | Identity / state fields the visibility packets read, `Doodad.Write`, `House.Write`, `ShipyardData.Write`, `Gimmick.Write`, `TimeLeft`. Templates, funcs, spawners, AI, physics, persistence: phase-3 subsystems. |
| `Core/Packets/G2C` object visibility (`SCUnitStatePacket` all kinds, `SCDoodadCreated/sCreated/Removed/sRemoved`, `SCGimmicksCreated/Removed`, `SCGimmickJointsBroken`, `SCHouseState`, `SCShipyardState`, `SCMateState`, `SCSlaveState`, `SCTargetChanged`, `SCUnitAttached`) | `packets/world_objects.rs`, `world/unit_state.rs`, `world/visibility.rs` | done | Byte-exact tests (`world/unit_state_tests.rs`, `packets/world_objects.rs`, `object_flow_tests.rs`). The NPC reference-height adjust in `SCUnitStatePacket` needs heightmaps (todo). |
| Phase-3 subsystem framework | `systems/` | done | Per-subsystem data / managers / packets / start / save stages / hooks; packet registration per module with duplicate detection. |
| `Core/Managers/SkillManager` | `data/skills.rs` | partial | skills (learning fields), default_skills, passive_buffs, buffs (subset), start-ability / common skill lists. Effects, plots, tags, modifiers todo. |
| `Core/Managers/FormulaManager` + Jace engine | `data/formulas.rs`, `models/stats.rs` | done / partial | Formula tables + a Jace-compatible evaluator (differentially tested against Jace 1.0.0). Character Str..Fai / MaxHp / MaxMp from the unit formulas; bonuses and gear stats are approximated (see deviations). |
| `Core/Managers/World/WorldManager` (+ `WorldInstance`, `Region`) | `world/mod.rs`, `world/visibility.rs`, `data/world_templates.rs` | partial | World templates from `game/worlds/*/world.xml` (directory or game_pak) + world_spawns.json, GetZoneId, main_world instance, region grid, AddVisibleObject / RemoveVisibleObject / AddToCharacters / RemoveFromCharacters (all object kinds, region batches, children, the non-player removal filter), Show / Hide / Spawn / Delete, AddObject / RemoveObject / Get* lookups, GetAround, GetInCell, GetRegion / GetRootObj, server/instance character lists, UsedCharacterObjIds, OnPlayerJoin. Heightmaps, water, physics, `ResendVisibleObjectsToCharacter` todo. |
| `IO` (ClientFileManager, AAPak) | `data/client_files.rs`, `data/pak.rs` | done | Read-only pak (types A/B/F) and directory sources, file listing semantics of both. |
| `Core/Managers/World/ZoneManager`, `ZoneConflict`, `ZoneStateChangeTask` | `data/world_data.rs`, `systems/social/zone_conflict.rs` | done | zones, zone_groups, conflict_zones (+ return points / tower ids); the conflict runtime (`AddZoneKill`, `SetState`, `ForceNextState`, timers, `SCConflictZoneStatePacket` broadcasts) is `server.systems.social.conflicts`. |
| `Core/Managers/World/FactionManager`, `LocalizationManager` | `data/world_data.rs`, `systems/services/localization.rs` | partial / done | system factions (+ localized names), relations, SendFactions / SendRelations (relation checks: `systems::social`). The complete `LocalizationManager` (all translations, item-name cache, `MatchItemName`) is `systems::services::localization`. |
| `Core/Managers/ExpeditionManager`, `Models/Game/Expeditions` | `managers/expedition.rs` | partial | Load, GetExpedition, SendExpeditions, SendExpeditionInfo, member refresh / login / logout status packets. Guild management todo. |
| `Core/Managers/FriendMananger` | `managers/friend.rs`, `db_character.rs` | partial | Load, SendStatusChange, GetFriendInfo, FormatFriend. Add/remove todo. |
| `Core/Managers/PortalManager` | `data/world_data.rs` | partial | recalls.json + district_return_points (GetRecallBySubZoneId, GetDistrictReturnPoint). Teleports, world gates, respawns todo. |
| `Core/Managers/ChatManager`, `Models/Game/Chat` | `managers/chat.rs`, `world/session.rs` | partial | Faction / nation / zone / guild / family channels, join/leave/send; CSSendChatMessage for every chat type (party/raid -> "not in party/raid" errors as there are no teams). Trial channels, commands todo. |
| `Core/Managers/TimeManager` (+ `TimeOfDayObserver`) | `managers/time.rs` | done | `Get` / `Set` / `Stop` added (phase 4). Storm-ship time override (`ResolveClientTimeOfDayHours`) per observer (`subscribe_with`). |
| `Core/Managers/SaveManager` | `managers/save.rs` | partial | Periodic + final save of items and online characters; the subsystems' rows through `systems::save_stage` (`BeforeItems`: houses, mails; `AfterItems`: auction, crimes; `AfterCharacters`: slaves). |
| `Core/Managers/World/StreamManager` | `managers/stream.rs`, `stream/cells.rs` | done | AddToken/RemoveToken/Login (wrong account removes the token, as C#), RequestCell/ContinueCell incl. the C# `next - 1` copy off-by-one. |
| `Core/Managers/Stream/UccManager`, `Models/Stream`, `Core/Managers/Id/UccIdManager` | `stream/ucc.rs`, `stream/models.rs` | partial | Load/Save (`uccs` table, data in the MEDIUMBLOB as the default C# build), upload (simple + custom), download, checks, id allocation. `CreateStamp`/`ApplyStamp` (item-side, used by skill effects) todo. `ConfirmDefaultUcc` charges the money and creates the Crest Ink before the save, as in C#. |
| `Core/Managers/TimedRewardsManager` | `systems/services/timed_rewards.rs` | done | `TimedRewardsTask` every minute, daily login reward, offline labor. |
| `Core/Managers/HousingManager`, `Models/Game/Housing` | `systems/housing/` | done | See "Phase 3 subsystems". |
| `Core/Managers/CrimeManager` | `db_character.rs` | partial | CheckWantedThreshold thresholds only. |
| Other `Core/Managers/*` | – | see the subsystems | AccessLevel, Command, ExpressText, Model, Music, Radar, Sus, Task/Tick, TimedRewards: `systems::services`; Animation, EffectTask, ManaRegen, Plot: `systems::skills`; FishSchool, Gimmick, PublicFarm, SubZone, Spawn, AreaTrigger, Specialty, SphereQuest, Portal: `systems::npcs`; Family, Duel, Team, Trial, Crime: `systems::social`; Quest: `systems::quests`; Auction, CashShop, Craft, Mail, Trade, Taxations: `systems::economy`; Housing: `systems::housing`; Mate, Slave, Shipyard, Transfer: `systems::vehicles`; Indun, InstantGame: `systems::instances`; AI / AiPaths / AiGeodata, World/Physics: `systems::ai`. |
| `Models/Game/Units/UnitCustomModelParams` | `models/unit_model.rs` | done | |
| `Models/Game/Units/Movements` (MoveType hierarchy) + `MathUtil` direction helpers | `models/movement.rs` | done | Default / Unit / Vehicle / Ship / ShipRequest / Transfer, byte-exact read/write (all actor-flag combinations tested). |
| `Models/AccountPayment`, `Models/Account` | `net/connection.rs`, `db.rs` | done | |
| `Models/AppConfiguration`, `Models/Game/Configurations` + `Program` config loading | `config.rs` | done | Every member (incl. WebApiNetwork, DiscordToken, Dungeons, AccessLevel, LaborOffline, Specialty, Scripts, full World), Config.json + Configurations/** + Config.Local.json + user secrets + environment (`Section__Key`) + command line, enums by name / value, `ConfigCell` (= `AppConfiguration.Instance`, replaceable at runtime). |
| Other `Models/Game/*` (AI, Auction, Crafts, Crime, DoodadObj, Mails, Mate, NPChar, Quests logic, Slaves, Team, ...) | – | see the subsystems | Ported with their managers (phase 3 / 4 subsystems). `Achievement` has no 1.2 logic. |
| `Scripts/Commands/**`, `Scripts/SubCommands/**`, `Utils/Scripts`, `Utils/{Quest,Sphere}CommandUtil` | `systems/services/commands/` (+ `scripts/`) | done | Infrastructure (`CommandManager` with async command execution, `AccessLevelManager`, `ICommand`, `IMessageOutput`, `SubCommandBase` + parameters) and every GM command: one module per `Scripts/Commands/*.cs` file in `scripts/`, the `SubCommandBase` roots in `scripts/roots.rs` / `scripts/sub/*` (Crimes, Doodads, Gimmick, Gold, Items, Npcs, Slaves sub commands). `scripts::load` registers them in the C# type order (`ScriptReflector`), so `GoldCmd` owns `gold` (and `AddGold`'s aliases) and `CrimeCmd` registers `crime` with `/doodad` as call prefix. Tests: a harness test per command (`scripts/harness.rs`), the registered set vs the C# sources (`scripts/tests.rs`). |
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
| `Models/Game/Units/Buffs` behaviour, `Skills/Buff`, `Skills/Effects/{Buff,Damage,Heal,Dispel,RestoreMana,ManaBurn,Aggro,FlyingStateChange,SkillController,...}`, `CombatBuffs`, `BuffTriggers` | `systems/skills/{buffs,effects/*}.rs` | done | Start / dispel / tick tasks, tolerance, stack rules, charges, `TriggerRemoveOn`, tag removal, absorption, triggers, area ticks. Explicit dispel emits Dispelled without Timeout; natural expiry retains Timeout. This corrects the mirrored C# removal path, which invoked the FISH escape trigger when catch plot 792 dispelled FISH before lethal damage. Queued CREATED handling and existing removal packet order are retained. Refreshing permanent non-ticking buffs invalidates old timers without treating GetTimeLeft=-1 as expiry; finite expiration and permanent tick cadence remain scheduled. This corrects the mirrored C# refresh timer path. Special effects routed through `ExtHooks::route_special` where they belong to another subsystem. |
| `Units/Unit` combat (`ReduceCurrentHp`, `DoDie`, aggro / tagging, `SetInBattle`, criminal / force attack, invisibility, `DoFallDamage`), `BaseUnit.CanAttack` / relations, regen, `UpdateGearBonuses`, all `Character` stat getters | `systems/skills/{combat,relation,stats,regen}.rs` | done | MaxHp / MaxMp etc. with buffs, passives and gear (`GearStats` hook). NPC death side effects through `NpcSkillHooks`. |
| Learning / progression (`LearnSkill`, `LearnBuff`, `ResetSkills`, `SwapAbility`, `AddExp`, labor, game points) | `systems/skills/progress.rs` | done | Labor accrual uses zero-based actability steps into ordered `expert_limits`, matching inventory / regrade; step 4 caps at 50,000 (row id 5), avoiding the former row-id lookup that reduced it to 40,000. Capped gains and packet deltas have regression coverage. |
| `Skills/Effects/SpecialEffects/*` (141 named `SpecialType` actions), `BubbleEffect`, `ImpulseEffect`, `PhysicalExplosionEffect` | `systems/skills/effects/{special/*,unit_fx}.rs`, `packets_special.rs` | partial | Ported actions and subsystem hooks are present, but historical logging placeholders do not establish gameplay completeness. Buff stealing, component-selective aggro copy/reset, target loss, NPC despawn, ongoing-buff cancellation and pet recovery now execute. RedeemBuff and ExplodeBuff still need verified formulas. Client visuals use the existing skill/plot notifications. 13 extra S->C packets, byte-exact tests. |
| `Skills/SkillControllers/{Floating,Wandering,Leap,Dash,Rotate}` | `systems/skills/controllers.rs` | partial | Rotate uses native constructor/tick timing (`0x3924E1D0` / `0x392315C0`) with 1.2 controller7678 regression coverage. Generic Anchor and Flowgraph remain unverified; native 10.x Anchor parameter kinds1001..1039 differ from the 1.2 data kinds1..3. Harpoon ropes have a separate vehicles implementation. |
| Movement hooks (`CSMoveUnit.RemoveEffects`, `Character.SetPosition` Move removal, fall damage) | `systems/skills/mod.rs` (`on_move_request`, `on_moved`) | done | Called from `world::session::move_unit`. |

### systems::npcs

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `NpcManager.Load` (customs, body parts, templates, equip packs, postures, unit modifiers, initial buffs, merchants / goods) | `npc/data.rs` | done | 13 382 templates on the real DB. Creatures.xml names, `LoadAiParams` not ported. |
| `NpcManager.Create`, `LoadCustom` (.NET `Random(123456789)` parity), `Npc` stats (Str..KillExp from the unit formulas + template bonuses), `InitializeSpawnBuffs`, `AnimActionId` | `npc/manager.rs`, `npc/stats.rs` | done | Every real template creates without error. The AI is created through `server.systems.hooks.ai` (`systems::ai`). |
| `Npc.DoDie` | `npc/death.rs` | done | Kill exp (team eligibility, `PlayerExpModifier`, mate exp, TagShare contributors), quest kill events, loot (`LootManager.GenerateLoot` through the economy), aggro / tagger clean-up, spawner despawn, `Ai.GoToDead()` (`systems::ai`). |
| NPC interaction packets (CSStartInteraction, CSInteractNPC(End), CSSelectInteractionEx; SCNpcInteractionSkillList, SCAiAggro, SCNpcChatMessage, SCUnitModelPostureChanged, SCAggroTargetChanged) | `npc/packets.rs` | done | Byte-exact tests. Merchant / bank / auction UIs are economy's. |
| `NpcGameData` (npc_spawners, npc_spawner_npcs, member index, generated spawners) | `npc/spawner_data.rs` | done | |
| `NpcSpawner`, `NpcSpawnerNpc`, `DoodadSpawner`, `Spawner<T>` | `npc/spawner.rs`, `doodad/spawner.rs` | done | Schedules / time windows / population caches, respawn + despawn timing. Ordinary populations keep the authored `Count` / `min_population` baseline (default 1) and per-slot respawn deadlines (see deviations). AI home position / follow path / simulation through `server.systems.hooks.ai` (`systems::ai`). |
| `SpawnManager` (one per instance) + `WorldManager.ActiveRegionTick` spawner part | `spawn_manager.rs` | done | main_world: 25 118 NPC entries -> 24 047 ordinary placements / 38 174 spawner clones (1 011 duplicates and 60 event entries filtered, 0 missing templates), 42 649 doodads, 69 transfers, 14 gimmicks, 5 slaves, 0 parse errors. Persistent doodads from MySQL. Transfers / slaves: `systems::vehicles`; houses and their doodads: `systems::housing`; trial court doodads / NPCs linked after the spawns (`TrialManager.Initialize*`). |
| `DoodadManager.Load` (templates, func groups, funcs, phase funcs, all 121 func template tables) | `doodad/data.rs`, `doodad/templates.rs` | done | 7 124 templates, 13 974 funcs, 24 465 phase funcs (counts match the DB). |
| `Doodad` phase machine (`Use`, `DoFunc`, `DoPhaseFuncs`, `DoChangePhase`, `InitDoodad`, `OnSkillHit`, `FuncGroupId` setter, ToD triggers), 42 phase funcs, func tasks, `DoodadManager` runtime (Create, CreatePlayerDoodad, ChangeDoodadData, coffers), persistence (`doodads` table) | `doodad/{phase,phase_funcs,tasks,manager,db,logic}.rs` | partial | Serialized interactions, per-object counters, generation and handle guards for timers, ToD transitions, ordered MySQL persistence. Authored immediate logic/family pulses, spawn management groups and moving gimmicks are implemented. FlowGraph runs in the client. Logic delay descriptors 2/3 lack timing semantics; spawn groups 73–76 lack authored membership in shipped data. |
| 79 non-phase funcs, 41 world interactions (`execute_interaction` for the skills InteractionEffect), coffers, bond / seats | `doodad/funcs/`, `doodad/interactions.rs`, `doodad/coffer.rs` | done | Items, loot, coffers through the economy `EconomyBridge`; skills, quests, housing through the bridges; instance funcs (`systems::instances`), `BindSlave` (`systems::vehicles`). Sticky parents are not modelled. |
| SCDoodad* packets, doodad C->S packets (CSChangeDoodadData/Phase, CSCreateDoodad, CSSetLogicDoodad, CSSaveDoodadUccString, CSUnbondDoodad, CSCofferInteraction) | `doodad/packets.rs`, `doodad/coffer.rs` | done | Byte-exact tests. |
| Heightmaps (`WorldCell` heightmap.dat, `GetHeight`, `GetReferenceHeight`), NPC reference height in `SCUnitStatePacket` | `heightmap.rs`, `world/unit_state.rs` | done | Lazy per cell (`PreLoadTerrain`); with `GeoDataMode` the navmesh height of `systems::ai::geodata` comes first. Median error 0.098 m vs 4 002 real spawn Z values. Used by the skill controllers / displacements, plots and NPC spawns. |
| `GimmickManager`, `GimmickGameData`, `GimmickSpawner`, movement handlers | `gimmick/` | done | |
| `AreaTriggerManager` (+ aoe_shapes, wi_group_wis), `SubZoneManager`, `SphereQuestManager` (world side) | `area_trigger.rs`, `subzone.rs`, `sphere_quest.rs` | done | Quest effects via `QuestsBridge`. Area membership compares entity identity, so reconnecting with a reused object ID leaves the retained old session and enters the new one; this corrects the mirrored C# ID-only comparison and restores active school auras. Unchanged entities and overlapping aura counts are retained. Sub-zone index scan of the pak is slow in debug (~25 s). |
| `PortalManager` (recall, district return, summoned portals, portal book, teleports) | `portal/` | done | Trial court check (`systems::social`), mates / slaves on instance change (`systems::vehicles`). |
| `SpecialtyManager`, `PublicFarmManager`, `FishSchoolManager`, `GameScheduleManager` (+ NCrontab subset) | `specialty.rs`, `public_farm.rs`, `fish_school.rs`, `schedule.rs` | done | |
| `TimeManager.OnTimeOfDayChange` (NPC postures, doodad ToD triggers) | `doodad/manager.rs` | done | 10 s poller started by the doodad part (TimeManager has no callback). |
| Cross-subsystem behaviour | `hooks.rs`, `bridges.rs` | done | `SkillsBridge` (skills), `EconomyBridge` (economy), `QuestsBridge` (quests) in `server.systems.npcs.hooks`; this subsystem implements `NpcSkillHooks`, `NpcEconomyHooks`, `LootNpcHooks`, `TreasureMapSource` (`bridges.rs`). See "Phase 3b wiring". |

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
| Stores (`CSBuyItems`, `CSSellItems`, buy-back, `CSListSoldItem`, `CSBuyPriestBuff`, `CSBuyCoinItem`, `CSSellBackpackGoods`), `SpecialtyManager`, `TaxationsManager`, `ExpandExpert`, item look conversion | `systems/economy/{store,store_data,specialty}.rs` | done | Merchant packs / NPC flags through `NpcEconomyHooks`. Checkout validates currency, checked totals and each balance, then stages complete goods / buy-back delivery before committing payment. Vocation charges the configured price displayed by the 1.2 shop, without earnings bonuses. Rejected checkout preserves items and balances. Doodad catalog authorization remains separate. |
| `MailManager`, `Models/Game/Mails/**`, `CharacterMails`, mail packets | `systems/economy/{mail,mail_character,mail_kinds,mail_packets,mail_data}.rs` | done | Saved in `SaveStage::BeforeItems`; housing tax through `MailHousingHooks`. |
| `AuctionManager`, `Models/Game/Auction/**`, auction packets | `systems/economy/{auction,auction_data,auction_packets}.rs` | done | Saved in `SaveStage::AfterItems`; results mailed through `wiring`. |
| `TradeManager`, trade packets | `systems/economy/trade.rs` | done | |
| `CashShopManager`, `Models/Game/CashShop/**`, ICS / premium packets | `systems/economy/cash_shop.rs` | done | Gifts by `CommercialMail` (`wiring`); account credits SQL behind `CashShopDb`. |
| Item skill-effect bodies (grade enchant, socketing, conversion, cap scale, UCC imprint, ...), mate / slave equipment packets | `systems/economy/item_effects*.rs` | done | Called by the skills subsystem's effects. UCC imprint / cleanup require owned source and target items; client UCC casts require the actual source item's configured use skill, preserving the unrelated legacy bound-item portal exception. Dyeing checks the loaded `dyeable_items` allowlist. Invalid UCC / dye effects cancel deferred reagent consumption. |

### systems::social

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `TeamManager`, `Models/Game/Team` | `systems/social/team.rs`, `packets/team.rs` | done | Invitations (single / area, 60 s timeout), create, AskRiskyTeam (leave / kick / dismiss + auto-disband), owner / role / marker / looting rule / ping / move, raid conversion, SetOffline / MemberRemoveFromTeam / UpdateAtLogin / UpdatePosition, 500 ms remote-member sync (delta cache, 5 s forced resync, upper-triangle pairs), zombie-team disband (5 min), loot winner helpers. Events (OnTeamJoin/Leave) are quest-side: not called. |
| `ExpeditionManager`, `Models/Game/Expeditions` | `systems/social/expedition.rs`, `packets/expedition.rs` (`managers::expedition` re-exports) | done | Load, create (party checks, cost, `SCItemTaskSuccessPacket` money-only form), invite / reply, leave, kick, member role, owner, role policy, disband (stays listed as `$deleted-guild-<id>`), `Expedition.Save` per change, offline leave for character deletion. Rename / sponsor are log-only / not in 1.2 as in C#. |
| `FamilyManager`, `Models/Game/Family` | `systems/social/family.rs`, `packets/family.rs` | done | Load, invite / reply / create, leave, kick (offline existence check), title, steward, login / logout, disband, save, offline leave for character deletion. |
| `FriendMananger`, `CharacterFriends`, `CharacterBlocked` | `systems/social/friend.rs`, `packets/friend.rs` (`managers::friend` re-exports) | done | Add / remove friends (online or by DB name lookup), block / unblock (online only), status changes incl. `InParty`. |
| `ChatManager`, `Models/Game/Chat` | `systems/social/chat.rs`, `session.rs` (`managers::chat` re-exports) | done | All channel kinds incl. party / raid / court, CleanUpChannels, party / raid / raid-leader / judge routing. User channels (`CSJoinUserChatChannel`) are log-only as in C#. |
| `FactionManager` relations, `SystemFaction.GetRelationState`, `Unit/Character.SetFaction` | `systems/social/faction.rs`, `packets/faction.rs` | done | Relation table validated against compact.sqlite3 (gated test). Faction CS packets are log-only as in C#; every SCFaction* struct ported. |
| `GameData/AchievementGameData` (`achievements`, `achievement_objectives`, `pre_completed_achievements`, `char_records`), `Models/Game/Achievement` | `systems/social/achievement.rs` (`server.game_data.systems.social.achievements`) | done | Loader + port of `AchievementGameDataTests`; gated real-DB test `achievement_game_data_real_compact_db`. The C# has no accessors / manager, so nothing reads the tables yet. |
| `DuelManager`, `Models/Game/Duels`, duel tasks | `systems/social/duel.rs`, `packets/duel.rs` | done | Request / accept (flag via hook) / start / stop / cancel, distance + HP checks, end timer (tokio tasks; `set_delays` test hook). |
| `CrimeManager`, `TrialManager`, `Models/Game/Crime`, `TrialUpdateTask` | `systems/social/{crime,trial}.rs`, `packets/trial.rs` | done | Crime table load / save (`AfterItems`), report / evidence (hooks), bot reports, trial / jury flow with 5 s tick, audience, bounty board (empty list as C#). Arrests, authenticated jury choices, guilty pleas, acquittals, court cleanup and reconnect jail enforcement have packet-flow regressions; prisoner buffs persist remaining online time. |
| `DominionData`, `Models/Game/Achievement`, rename / chat bubble / NPC chat / spam packets | `systems/social/{dominion,achievement}.rs`, `packets/misc.rs` | done (data + packets) | No C# manager drives them in 1.2. |
| `RadarManager` | – | not social | Telescope / fish school registration from buff templates: skills / npcs. |

Cross-subsystem calls: wired in phase 3b (see "Phase 3b wiring": trial court doodads / NPCs after the spawns, `SocialHooks` by social itself, crime evidence / arrests / team and duel queries from skills, loot rules from economy, court checks in portals and instances, `force_dismount` through vehicles). Still open: teleports -> `session::on_moved` (C# `Character.SetPosition` -> `TeamManager.UpdatePosition`; plain movement does not call it in C# either).

### systems::housing

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `HousingGameData` (`item_housings`, `housings`, `housing_binding_doodads` + `Data/housing_bindings*.json`, `housing_build_steps`, `housing_decorations`, `item_housing_decorations`, `PostLoad`), `TaxationsManager` (`taxations`) | `systems/housing/data.rs` | done | Gated real-DB test `housing_real_compact_db` (row counts, bindings positioned). Duplicate build steps fail the load (C# `Dictionary.Add`). |
| `HousingManager` (all methods), `House` behaviour (`CurrentStep` setter + bound doodads, `AddBuildAction`, `Spawn` / `Delete` / visibility of `AttachedDoodads`, `AllowedToInteract`, `OnDeath`, `Save`), `HousingTaxTask`, `MailForTax` content | `systems/housing/{manager,ops,db}.rs` | done | Build (tax certificates or gold), permission, rename, recover toggle, demolish (+ `ReturnHouseItemsToOwner` mails), sale / sale markers / cancel / buy (+ mails, furniture owner), decoration, taxes (bills, untouchable / removal buffs, demolition timer every 10 s), death, `GetHouseAtLocation`, act-ability bonus. Items / money / mail go through `housing::hooks::HousingEconomyHooks` (economy installs it). |
| Housing packets (11 CS, 14 SC incl. `SCLoginCharInfoHouse`, `SCMyHouse`) | `systems/housing/packets.rs` | done | Byte-exact tests. `CSChangeHousePayPacket` has opcode 0xfff in 1.2 (not registered). `SCHouseOwnerNameChangedPacket` is social's (`packets/misc.rs`). |
| `CraftEffect` (`WorldInteractionGroup.Building`) | `systems/housing/mod.rs::apply_effect` | done | Routed first by `skills::ext::ExtHooks::route_effect`; quest `DoDoodadInteractionEvents` after it. |
| `SpawnManager.Load` / `SpawnPersistentDoodads` housing parts, `LoadAccount` / `CSListCharacter` / `CSSelectCharacter` / `Character.Load` / `DeleteCharacterAssets` housing parts | `npcs/spawn_manager.rs`, `packets/c2g.rs`, `world/session.rs`, `db_character.rs` (one call each) | done | `LoadPlayerHousing` + `SpawnAll` before the persistent doodads, house parenting + bound doodads, `ReconcileBoundDoodads` (`UsePersistentHouseDoodads`). |
| Placement validation | `systems/housing/placement.rs`, `systems/npcs/subzone.rs` | done | Original 1.2 housing XML `value1` tags resolve `housing_areas`/groups/categories; finite world positions, garden overlap, account eligibility, and nonrenewable beginner plots are checked server-side. World-scoped land checks also guard planting and housing crafting benches. Model-specific slope/height/footprint checks remain client-side. |

Economy hooks (`server.systems.housing.hooks`): installed by `economy::bridges::EconomyForHousing`. Housing API for others: `housing::{house_by_id, house_at_location, allowed_to_interact(_by_id), pay_weekly_tax (MailManager tax payment), act_ability_bonus_at (CharacterCraft), update_owned_housing_faction, demolish_by_player}`.

### systems::quests

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `QuestManager.Load` (contexts, components, base acts, all 64 `quest_act_*` detail tables, supplies, item / NPC groups, `UpdateQuestComponentActs`) | `systems/quests/data.rs`, `templates.rs` | done | Same table order (act order inside a component = objective indices), first-base-act linking, tutorial category skipped. Validated against compact.sqlite3 (`real_compact_db_quest_tables`: 4775 quests, 17720 components, 19047 acts, per-type counts). Exhaustive record accounting and outstanding gameplay failures: `Docs/wiki/Quest-Audit.md`, `rust/tools/quest_audit/audit.py`; port coverage does not certify every quest as playable. |
| `Models/Game/Quests` (`Quest`, `NewQuestCode`, `QuestStep`, `QuestComponent`, `QuestAct`, all `Acts/*` + `UnusedActs/*`) | `state.rs`, `engine.rs`, `acts.rs` | done | Live quests on `Character::quest_state` (handles + act paths instead of object references); `c.quests` rows re-derived after every operation. Step machine, objective status / percent, rewards, drop / cleanup, event subscriptions in invocation-list order (`-=` removes the last occurrence). |
| `CharacterQuests` (AddQuest*, DropQuest, completed flags, reset / daily reset, LetItDone, item destroy, `GetActiveActsWithUseItem`, Load) | `engine.rs`, `events.rs` | done | Save = existing `Character.Save` rows. |
| `QuestManagerEvents` + other C# entry points (OnItemUse, OnKill, cinema, mate level) | `events.rs` | done | Public event API; nested calls from hooks are queued. |
| `QuestManager` evaluation queue / timers / daily reset task | `mod.rs` | done | `QuestManagerRunQueueTask` as a 1 ms tokio task; timers as tokio tasks; daily reset at 00:00 UTC. |
| `UnitRequirementsGameData` (`CanComponentRun`, `CanTriggerSphere`) + `UnitReqs.Validate` | `unit_reqs.rs` | done | `ReqSubject` / `ReqEnv` traits (owner = locked character, target = snapshot). `CanUseSkill` is the skills subsystem's (can reuse `validate`). |
| `SphereQuestManager`, `SphereQuest*`, `SphereGameData` (spheres, sphere_quests, `IsInsideAreaSphere`, `GetSphereIdFromQuest`) | `spheres.rs`, `data.rs` | done | `quest_sign_sphere.g` of `main_world`; one manager per server (500 ms tick). Other `sphere_*` tables not loaded (unused by quests). |
| Quest packets (CS: StartQuestContext, CompleteQuestContext, DropQuestContext, QuestTalkMade, QuestStartWith, TryQuestCompleteAsLetItDone, RestartMainQuest, SaveTutorial, StartedCinema, CompletedCinema; SC: QuestContextStarted/Updated/Completed/Reset, QuestRewardedByMail, DoodadQuestAccept, TutorialSaved, AppellationGained) | `packets.rs` | done | Byte-exact tests. `SCQuestsPacket` now writes `Quest.Id` as `long`. |
| `QuestCommandUtil` / `QuestCmd` (GM) | `systems/services/commands/scripts/quest_cmd.rs` | done | `quests::events::send_quests` / `send_completed_quests` added. |

### systems::instances

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `IndunGameData` (actions, events, zones, rooms), `Models/Game/Indun/{IndunZone, IndunRoom}` | `systems/instances/data.rs` | done | Same six event queries / four action queries in C# order; `EnterCount` hack (1000 for group 49 / channel dungeons, else 3); localized names. Real DB: 104 actions, 70 events, 20 zones, 13 rooms. |
| `BattlefieldGameData` + `Data/battlefields.json`, `TowerDefGameData`, `Models/Game/{InstantGame, TowerDefs}` data | `data.rs` | done | 7 battlefields (last `game_rule_sets` row wins), 24 tower defs / 89 valid progs. Tower defense skips orphan parents and orders waves by id. |
| `WorldManager.CreateWorldInstance` / `RemoveWorld` / `CreateStaticInstances` (AutoCreate) / `WorldIdManager`, `WorldInstance.CleanupInstance` / `ChannelId` / `HasCharacter` / `GetCharacterCount` / `ListPlayerNames` | `worlds.rs`, `mod.rs` (start), `world/mod.rs` (`has_world`, `remove_world`, `leave_other_instance`, `WorldInstance::transfer_out`), `npcs::spawn_manager::SpawnManagers::remove` | done | Non-instanceable templates reuse their instance; fixed ids <= 100; per-instance `SpawnManager` created with the world and loaded by the dungeon loader / a background task. The cross-instance region switch happens on the player's next move (as the C# `AddVisibleObject` region change). |
| `Character.MainWorldPosition`, `Transform.CloneDetached` / `ApplyWorldSpawnPosition` / `MainWorldPosition.Clone()` | `worlds.rs`, `models/character.rs` (`main_world_position: Option<Transform>`), `db_character.rs` (save rule) | done | Saved inside an instance: the return point is written (C# `saveFromInstanceReturn`). |
| `IndunManager` (requests, requirements, entry history, system instances, deletion, leave, `DoIndunActions`, info tick) | `indun.rs` | done | |
| `Dungeon` + `DungeonLoaderTask` (queue, add / move, leave, team / disconnect handlers, `LeaveDungeonTick`, `AreaClearTick`, destroy) | `dungeon.rs` | done | `DestroyTeamDungeon` (never called in C#) not ported. |
| Party dungeon admission/reset and 1.2 mentor/mentee sealed chest quests | `indun.rs`, `dungeon.rs`, `mentoring.rs`, `scripts/dungeon.rs` | extended | Serialized run selection, pending-entry capacity, one charge per character/run, owner/leader-only empty resets, safe destruction; four paired daily quests use actual chest phases/seal items and existing inventory/quest rewards. See `Docs/wiki/Dungeons.md`. |
| `Models/Game/Indun/Events/*`, `Actions/*`, `WorldInstance.Events` (`OnUnitSpawn`, `OnDoodadSpawn`, `OnUnitKilled`, `OnUnitCombatStart/End`, `OnAreaClear`), `CharacterEvents` (`OnTeamJoin/Leave/Kick`, `OnDungeonLeave`, `OnDisconnect`), `UnitEvents.OnKill` | `events.rs` | done | Raised from `npcs` spawners, `skills::combat` (`PostUpdateCurrentHp` / `DoDie`), `social::team`, `world::session::on_disconnect`. |
| `InstantGameManager`, `InstantGame` (+ `InstantGameEvents`, team members / results) | `instant_game.rs` | done | Queue / matchmaking / invite / join / enter / start / kills / scores / end / leave; `Character.SetFaction` override (housing hook, guild kick, party kick); `ResetAllSkillCooldowns`. |
| Packets: `CSInstanceLoaded`, `CSUnknownInstance`, `CSApplyToInstantGame`, `CSCancelInstantGame`, `CSJoinInstantGame`, `CSEnteredInstantGameWorld`, `CSLeaveInstantGame`; `SCProcessingInstance`, `SCAppliedToInstantGame`, `SCInviteToInstantGame`, `SCInstantGameJoined/Start/End/AddPoint/Kill/Killstreak`, `SCInstantGameRemainTime` (`SCInstantGameUnkPacket` opcode), `SCTowerDefList/Start/End/WaveStart`, `SCSkillCooldownReset` | `packets.rs` | done | Byte-exact tests. `SCLoadInstancePacket` is the npcs portal one. |
| Doodad funcs `EnterInstance`, `EnterSysInstance`, `ExitIndun`, `RemoveInstance`; skills hooks `leave_instance` (`ExitArchemall`) / `main_world_position` (`Return`); portal `UsePortal` court check | `npcs/doodad/funcs/world.rs`, `skills/ext.rs` (default bodies), `npcs/portal/mod.rs` | done | |
| Tower defense runtime, `TowerDefenseAttackerAiCharacter` | `tower_def.rs`, `config.rs`, `scripts/tower_def.rs`, `ai/framework.rs` | extended | Rust adds event start/end, first-wave delay, timed/kill AND/OR progression, NPC controller/summon cleanup preserving normal corpse timers, client lists, GM controls and opt-in game-time/war schedules. The attacker's C# behavior graph already matches `AiKind::TowerDefenseAttacker`. Unsupported target kinds and absent spawn positions fail preflight; rewards/victory event chains are not added. |

Cross-subsystem: mates are despawned on dungeon entry and `PlayerCanEnter` checks the summoned vehicle (`systems::vehicles`); dungeon entry items use the economy's checked `EconomyBridge::bag_consume_item`; `SetFaction` updates the owned houses (`SocialHooks::update_owned_housing_faction`, housing). Instant games have no rewards in C#.

### systems::vehicles

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `MateGameData`, `SlaveGameData` (templates, unit modifiers, initial items / buffs, passive buffs, doodad / healing point / slave bindings, drop doodads, mount skills, repairable slaves, `slave_attach_points*.json`), `TransferGameData` (templates, bindings, binding doodads, paths, client `transfer_path.xml` roads + `GetOwnerPaths`), `ShipyardManager.Load`, `TaxationsManager` table, `ModelManager` vehicle / ship models | `systems/vehicles/data.rs` | done | Gated test `real_compact_db_vehicle_tables` checks the row counts; `real_transfer_follows_its_client_route` loads the roads from the game_pak. |
| `MateManager` (per instance), `CharacterMates` (load / save / spawn / despawn), `Mate` (`AddExp`, `MateXpUpdateTask`, `RegenTick`, `DoFallDamage`, `OnZoneChange`, stats) | `systems/vehicles/mates.rs`, `stats.rs` | done | Mount / dismount (`SCUnitAttached` / `SCUnitDetached`, rider becomes a transform child), rename, target, user state, mate equipment packet (item move through `VehicleItemHooks`), kill exp, stun / unmount skills, death despawn. |
| `SlaveManager` (per instance), `SlaveSpawner`, `SummonSlave` details, `Slave` (`DoDie`, `DoHpChangeTrigger` / repair points, `RegenTick`, `Save`, `OnZoneChange`, stats) | `systems/vehicles/slaves.rs` | done | Create (saved slave / persistent doodads / saved child slaves from MySQL, position, boats on the water surface, doodad and slave bindings at the model attach points), bind / unbind, delete with portal-time despawn, Rider's Escape, repair cooldown, `RepairSlaveEffect`, `SendMySlaveTask`, logout (+10 min) / disconnect despawn task, `slaves` table (`AfterCharacters`). |
| `TransferManager` (per instance, 100 ms tick), `TransferSpawner`, `Transfer` route following (`GoToPath`, `MoveTo`, `NextPoint`) | `systems/vehicles/transfers.rs` | done | Carriage motor + boarding part + bench doodads, `SCOneUnitMovementPacket` (transfer move type) for both parts. Spawned from `SpawnManager.SpawnAll`. |
| `ShipyardManager` runtime, `Shipyard` build steps, `CraftEffect` shipyard branches, `ShipyardTickTask`, `ShipyardCompleteTask` | `systems/vehicles/shipyards.rs`, `effects.rs` | done | Requirements via the skills item hooks + money (`SCItemTaskSuccess` money form). |
| `ShipHarpoonRopeController`, `CSUnhangPacket` | `systems/vehicles/harpoon.rs` | done | Tow forces are physics (`ShipHarpoonTowPhysics`): the per-tick entry points are exposed for the physics port. |
| `CSMoveUnitPacket` vehicle branches (ship request, vehicle, mate xp, riding, standing on objects) | `systems/vehicles/movement.rs`, `world/session.rs` | done | Ship-deck support accepts the actor standing flag independently of the movement header, preserving valid GC-relative positions and the current-parent sentinel during controller updates. This corrects the mirrored C# header-only attachment test; clearing both support flags still disembarks. A unit no longer hanging (`HangingFromObject` unset) loses its sticky parent unless it is boarded on a transfer. |
| Mate / slave / transfer / shipyard C2G and G2C packets | `systems/vehicles/packets.rs` | done | 18 C->S, 16 S->C (byte-exact tests). `CSChangeSlaveTargetPacket` has no 1.2 opcode (0xfff). |
| Hooks / physics | `systems/vehicles/{hooks,ship_physics}.rs` | done | `VehicleItemHooks` (`EndCraft`, economy). Ship physics and water call `systems::ai::physics` directly (`AddShip` / `RemoveShip`, helm request, driver seat, `GetWaterSurface` / `IsWater`); `ShipPhysicsHooks` (harpoon lines / tension / lifetime, `Throttle` / `Steering` after a move) and the radar ship lists are installed here. The npcs / social hooks call the mate / slave / dismount functions directly. |
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
| Character packets: `CSActiveWeaponChangedPacket`, `CSBroadcastVisualOptionPacket`, `CSChangeAppellationPacket` (`CharacterAppellations.Change`), `CSIdleStatusPacket`, `CSRefreshInCharacterListPacket`, `CSRequestUIDataPacket`, `CSSaveUIDataPacket`, `CSUpdateActionSlotPacket`, `CSRestrictCheckPacket`, `CSSetLpManageCharacterPacket` and the log-only `CSCharDetailPacket`, `CSCleanupLogicLinkPacket`, `CSConsoleCmdUsedPacket`, `CSCriminalLockedPacket`, `CSEditorGameModePacket`, `CSRankCharacterPacket`, `CSRequestCharBriefPacket`, `CSRequestPermissionToPlayCinemaForDirectingMode`, `CSRequestSecondPasswordKeyTablesPacket`, `CSSetupSecondPassword`, `CSResturnAddrsPacket` (+ `SCActiveWeaponChangedPacket`, `SCUnitVisualOptionsPacket`, `SCAppellationChangedPacket`, `SCUnitIdleStatusPacket`, `SCRefreshInCharacterListPacket`, `SCResultRestrictCheckPacket`, `SCLpManagedPacket`) | `systems/services/character_packets.rs` | done | Byte-exact tests. `CSHangPacket` / `CSUnhangPacket` are `systems/vehicles/harpoon.rs` (sticky parents, rope break); `CSResurrectCharacterPacket` is `systems/skills/resurrect.rs`. |
| `CharacterManager` `bag_expands` / `expand_expert_limits`, `Inventory.ExpandSlot`, `CharacterActability.Regrade` / `ExpandExpert`, `CSExpandSlotsPacket`, `CSExpandExpertPacket`, `CSUpgrade/DowngradeExpertLimitPacket` (+ `SCInvenExpandedPacket`, `SCExpertLimitModifiedPacket`, `SCExpertExpandedPacket`) | `systems/services/expands.rs` | done | Item checks / consumption through `CHARACTER_ITEM_OPS` (economy). |
| `CommandManager`, `AccessLevelManager`, `Models/Game/ICommand`, `Utils/Scripts/**` | `systems/services/commands/` | done (infra) | Ports of `SubCommandBaseTests`, `NumericSubCommandParameterTests`, `AccessLevelManagerTests`, `DoodadChainSubCommandTests`. AStar sub commands need `ASTAR_HOOKS` (AI path finding). `Scripts/SubCommands/Doodads/DoodadSetAgeCommand` is ported (`commands/doodads.rs`, port of `DoodadSetAgeCommandTests` through a test root with the `DoodadCmd` aliases); the `DoodadCmd` root itself is the scripts phase. |
| `ExpressTextManager`, `CSExpressEmotionPacket`, `SCEmotionExpressedPacket` | `systems/services/express_text.rs` | done | |
| `LocalizationManager` | `systems/services/localization.rs` | done | 263 635 rows / 8 locales on the real DB. |
| `ModelManager`, `Models/Game/Models/*` | `systems/services/models.rs` | done | Counts checked against compact.sqlite3 (gated test). |
| `MusicManager`, `SongData`, music packets (`CSSaveUserMusicNotesPacket`, `CSSendUserMusicPacket`, `CSRequestMusicNotesPacket`, `CSEndMusicPacket`, `SCUserNoteLoadedPacket`) | `systems/services/music.rs` | done | `CreateSheetMusic` item work through `SheetMusicInventory` (economy; its `ItemSkillHooks::midi_cache` / `create_sheet_music` call this manager). |
| `RadarManager`, `TelescopeRegistrationEntry`, telescope / fish finder packets | `systems/services/radar.rs` | done | Registered from `BuffTemplate.Start` / `Dispel` (skills), unregistered on leave. Ship lists through `RadarShipHooks` (vehicles). |
| `Models/Tasks/ShutdownTask`, `SCNoticeMessagePacket` | `systems/services/shutdown.rs` | done | Count-down notices, final save, process exit; held in `server.systems.services.shutdown_task` (`SaveManager.ShutdownTask`). Buy-back wipe through `BUYBACK_WIPE` (economy). |
| `SusManager` | `systems/services/sus.rs` | done | `audit_char_sus` log + speed analysis; the 5 s `FinalizeTransform` caller and the social / npcs log calls are not wired yet (see deviations). |
| `GameData/Framework` (`GameDataManager`, `IGameDataLoader`, `GameDataAttribute`) | `data/mod.rs`, `systems::SystemsData::load` | done (by design) | The reflection-driven loader list is the explicit `GameData::load` + per-subsystem `Data::load`; `Load` / `PostLoad` order is the call order there. |
| `Models/Tasks/SaveTask/SaveTickStartTask` | `managers/save.rs` | done (by design) | The periodic save is a Tokio interval instead of a `TaskManager` task (not counted in `GetQueueCount`). |
| `Utils/{MathUtil, NumericExtensions, Converters/*, DB/HoldablesSchemaCheck}` | `utils/` | done | `Utils/DB/SQLite*` = `data::sqlite`, `Utils/IdManager` = `managers::id`. `SphereCommandUtil` / `QuestCommandUtil`: `services::commands::scripts::{sphere, quest_cmd}`. |

### systems::ai

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| `NpcManager.LoadAiParams` (NLua), `Models/Game/AI/v2/Params/**`, `AiGameData` (commands, command sets, chat bubbles, events), `ModelManager` (actor models + game stances, ship models) | `systems/ai/{lua,params,data}.rs` | done | A Lua 5.4 subset evaluator with `System.Convert` semantics; all 2 489 `npc_ai_params` rows produce the same values as NLua (differential harness, gated test `real_compact_db_params_match_nlua`; rows 443 / 835 / 1975 fail to parse in both). |
| `NpcAi` framework, `AiUtils.GetAiByType`, all `AiCharacters/**`, every `Behavior` (incl. Archer / BigMonster / Flytrap / WildBoar / AlmightyAttack / Talk / FollowPath / Return / Dead / Despawning / Spawning / Hold / Idle / Roaming / Alert / RunCommandSet / DoNothing) | `systems/ai/{framework,behaviors}.rs` | done | State machine, GoTo overrides, transitions, check intervals, skill queue / selection, aggression / alert / help, return, follow path, greetings (`SCNpcChatMessage` + `SCPlaySound`). |
| `AIManager` (100 ms tick, `AddAi`, region activity) | `systems/ai/manager.rs` | done | Per-AI mutex + queued calls (see deviations). |
| AI members of `Npc` / `Unit` (`MoveTowards`, `LookTowards`, `StopMovement`, `SetTarget`, `CurrentAggroTarget`, `CurrentGameStance`, `BaseMoveSpeed`, `OnDamageReceived`, `CheckIfEmptyAggroToReturn`, `ClearAllAggro`, `FindPath`), `NpcEvents` | `systems/ai/{npc,combat,skills,npc_events}.rs` | done | `SCOneUnitMovementPacket` movement, aggro tables through the skills API. |
| `AiPathsManager` (`Data/Path`), `AiPathHandler`, `Units/Route/Simulation` | `systems/ai/{path_handler,simulation}.rs` | done | Every `Data/Path/*.path` file loads (test). |
| `AiGeoDataManager`, CryEngine `.bai` readers (net / areas / verts missions), A* `PathNode`, `GetHeight`, `ReducePath` | `systems/ai/geodata/` | done | `GeoDataMode` only. Engine-faithful readers and a merged cross-sector graph (see "geodata deviations" below). 1.2 pak: 9 447 net (2 299 of them v54) + 9 447 verts (2 299 v1) + 3 265 areas files parse with 0 trailing bytes; 279 path areas files below v21 are rejected by version like the engine (they do not fit the v21 layout either), the 7 399 `hidemission` files are not read. The areas reader matched the C# reader on every dumped file (differential harness; path areas < v21 now excluded on purpose). Gated tests: `real_ai_*`, `real_nav_*` (`AAEMU_GAME_PAK`, `AAEMU_BAI_SAMPLES=D:\aa-re\zone\agent_geom\data`). |
| `Gimmick.MoveTowards`, flytrap gimmick | `systems/ai/gimmick.rs` | done | |
| Hooks for npcs / skills (create AI by `ai_file_id`, spawn / engage / death / delete / faction, NPC control effect, spyglass), AStar GM commands | `systems/ai/hooks.rs` | done | `AiHooks` in `server.systems.hooks.ai`; the npcs `NpcSkills` calls the AI parts of `NpcSkillHooks` directly (no decorator); `AiAStarHooks` installed into the services `ASTAR_HOOKS`. |
| `Physics/**`, `PhysicsManager` (ship hulls: Jitter2 world, `ShipController`, shore / ship–ship / doodad / cliff / static barrier contacts, buoyancy, whirlpool, storm lamps, harpoon tow, replication), `ShipSiegeAoEHit` geometry | `systems/ai/physics/` | done | The Jitter2 subset is bit-exact against Jitter2 2.8.8 for the integration (embedded reference test). Slaves call `add_ship` / `remove_ship` / `set_ship_request` / `set_ship_driver` / `ship_state` and may install `ShipPhysicsHooks` (harpoon rope state). |
| `WaterBodies`, `WaterBodyArea`, `Models/CryEngine/Objects` (`object.dat`) | `systems/ai/physics/{water,objects_file}.rs` | done | Water areas of all 1 294 client `object.dat` files equal the C# output bit for bit (differential harness, gated test `real_ai_object_dat_water_bodies`). Gimmick `FloatToSurface` uses them. |
| `ShipTuningDebug`, `HarpoonMechanicsDebug` logging, `ShipHarpoonRopeController` | – | not ported / slaves | Debug code is hard-wired off in the C#; the rope controller belongs to the slaves subsystem. |
| (Rust only) the 10.x zone server's collision world: terrain heightfield, `brush.dat` / `vegetation.dat` statics from CGF `MeshPhysicsData`, doodad colliders, ocean / water-volume buoyancy areas, living-entity proxies of units, ray / sweep query service, `GetBlinkPosition`, ZWUnitFell, ZWReportMoveHack, ZWUnitCollision, CryPhysics hulls | `systems/ai/collision/`, `systems/ai/physics/cry.rs` | done | On the CryPhysics port (`aaemu-physics`), `Configurations/Physics.json`. See "systems::ai collision" below. |

#### systems::ai collision (the zone's physics, Rust only)

**Vehicle test fixes (2026-10-11):** the physics thread measures elapsed time after sleeping and
advances its previous tick to that instant. The former pre-sleep timestamp produced alternating
waited/back-to-back iterations and repeated elapsed time; at 25 TPS with 3 ms of work it stepped
about twice as often. Clock tests now verify 25 ticks integrate exactly 1 s, including fractional
milliseconds and late wake-ups. Empty physics instances skip simulation/environment queries
after removing their final CryPhysics hull; the removal test checks both cleanup and idle queries.
`CSDespawnSlavePacket` copies the instance id before calling vehicle cleanup so its temporary
player data lock is released. A live scroll dismissal previously deadlocked there; the item-skill
flow test now casts an owned item, binds the driver and dismisses through this packet with a
bounded completion check. Cars retain the 1.2 client's authoritative `VehicleMove` simulation;
these changes affect server ship timing and vehicle cleanup.
Ships now load the normal prefab reference from `ship_models` and prefer the authored `buoy*`
brushes, preserving the CGF physics geometry and local position/quaternion/scale. They replace
the fallback tube rather than adding a second displaced volume. The geometries share the
collision model cache and are loaded on summon, not each tick. The real 1.2 clipper prefab has
two such brushes; the rowboat has none and retains its explicit tube. A gated real-data test
checks both placements, settled draft and driven motion at two headings.
The server's ship transform now uses the same visual quaternion as its movement packet. This
fixes the mirrored C# yaw-in-pitch bug for attached passengers and parts without changing the
wire order; a placement test compares the transform and quantized packet quaternion.
Standing-on-object movement preserves the current parent for `GcId == 1`. Captured 1.2
`gcFlags=1` packets carry a primary local position and an auxiliary world position; a late
packet whose supporting object has disappeared uses the world backup. A packet-flow test
checks a rotated ship, the current-parent sentinel, walking off with a stale header flag and
the missing-parent backup. This fixes false origin jumps when leaving a ship.
Seated slave passengers ignore late unit walking updates until the ordinary discard clears
their seat membership. A steering request that restores a missing parent converts the driver's
world transform to the ship's local frame. The live client exposed the old detach/re-attach
sequence placing the driver's world coordinates under a ship parent; the new flow test covers
boarding, a stale walking packet, recovery and discard. Missing-zone debug logs include the
object id, parent and both coordinate frames to diagnose future attachment problems.
During live-stack validation, the comment/trailing-comma JSON reader was found recursively
stripping the remainder after each commented comma in `Scripts/Commands/kits.json`. It now scans
only to the next token, retaining Newtonsoft-compatible comments and trailing commas without
exponential startup work. The shipped kit file and quoted/commented tokens have regression tests.

References: `rust/docs/physics/ZONE-PHYSICS-USAGE.md`, `D:\aa-re\zone\agent_geom\{sub_cgf,sub_x2game}.md`,
the x2game / cry3dengine decompiles (addresses below). Config `Physics` (`Configurations/Physics.json`):
`Enable` (true), `Geometry` (`Terrain` / `Brushes` / `Full`), `LoadRadiusCells` (1), `LoadOnQuery`
(true), `UnloadAfterSeconds` (600), `SyncPeriodMs` (1000), `DoodadColliders` (true),
`PreloadAllCells` (false), `MovementVerifyEnable` (false = `movement_verify_enable 0`),
`FallDamageZoneCap` (true), `CryPhysicsShips` (true). The service is off without client data
(`HeightMapsEnable`); every caller then keeps its previous (C# / heightmap) answer.

* **World per instance** (`collision::world::PhysWorld`): entity grid over the world (at most 1024
  cells per axis, z origin −256), terrain heightfield from the `heightmap.dat` samples (2 m step;
  fn-pointer callback slots, `collision::terrain`), the ocean as the global buoyancy area at
  `oceanLevel` (density / resistance 1000), `SetWaterMat` index 1 *(inferred)*, `gEnv+289` set (no
  DrawRayOnGrid limit).
* **Cells** load lazily (`collision::residency`): around players and ships every `SyncPeriodMs`, by
  queries that touch a cell (at most 16 cells per query); idle cells are unloaded. The zone loads
  its zone's cells at start (`SeamlessSystem::LoadZone` 0x365704D0).
* **Brushes** (`CBrush::Physicalize` 0x30111970): static entity at the brush matrix (scale =
  |column 0|), parts 0x1000 (0x18FFF, 0x10FFF with a no-collide geometry, `| 0xA` with render flag
  0x400000), 0x1001 (0x8000), 0x1002 (0x4000), entity flags `|= 0x8000`. The foreign-data class bits
  of render flags 0x40 / 0x80 / 0x2000 are not modelled. **Vegetation** (`CVegetation::Physicalize`
  0x30103DC0, matrix 0x30102840): static entity, density 800, flags 0x04018FFF, 0x1002 when present;
  groups whose model only has a no-collide geometry (most grass / foliage) get no entity, like the
  server (0x1001 needs the CVar ≥ 2). Not applied: the terrain alignment of `bAlignToTerrain`
  groups. The sector order of `brush.dat` / `vegetation.dat` is x-major (fixed from the data: 99.2 %
  of the trees sit on the terrain with it, 23 % with y-major).
* **Models** (`collision::{cgf,models}`): every `MeshPhysicsData` slot of a node's mesh through
  `LoadPhysGeometry` (blobs that share the render mesh get its positions / u16 indices / subset
  material ids); node transforms are not applied (`AddPhysGeom` 0x300400D0 does not). Material
  mappings / surface types are not loaded (hits report surface −1 / 0).
* **Doodads** (`collision::objects`): model class by prefix (`sub_39451CE0`: `cgf://`,
  `vegetation://`, `cga://`, `prefab://` = the Brush objects of the prefab library; else no geometry)
  from `doodad_func_groups.model` (read by `collision::data`; the C# discards the column), flags by
  `_UpdateCollisionParams` (`sub_39451190`: `collide_vehicle` 0x08, `collide_ship` 0x80,
  `no_collision` → 0x8000 only). Synced from the doodads of the loaded cells every residency tick
  (`sync_doodad` for immediate updates). Houses: the 1.2 DB has no house model paths (`prefab_models`
  has ids only), so there are no house colliders. Gimmicks: not built.
* **Units** (`collision::living`): inactive `CLivingEntity` proxies (P5) from
  `actor_models.physics_living_*` + radius / height, placed on every client move (players, mounts)
  and every residency tick (NPCs). The client stays authoritative; the zone's per-frame actor
  simulation is not run.
* **Queries** (`collision::{query,mod}`): `terrain_height` = `GetTerrainElevation` (`sub_300D4E00`:
  triangle split on `dx + dy = 1`, 0 when a corner is 0 or below the first sample), `ground_height` =
  first static / terrain hit (0x101, rays 0x8F) from z + 2 m down, `raycast` (any mask),
  `line_of_sight` (0x101), `sweep_capsule` (PWI), `water_level` / `is_water` (the C# water bodies),
  `surface_type` (0x11F). `displacement::blink_position` = `GetBlinkPosition` (`sub_3925E670` /
  `sub_3925D990`).

**Behaviour changes (with the service on):**

* Blink (`WZBlinkUnit` 0x39369B10, terrain only) ends where the unit's collider stops, 0.1 m before a
  contact, following 30° slopes; leap / dash end points and NPC knockback / impulse / explosion
  pushes stop at static / rigid geometry (0x107) and land on the ground; teleport-to-unit uses the
  ground at the end point; in-place resurrect snaps to the ground (`GetResurrectablePosition`
  `sub_3925D3B0`).
* Ground-targeted plot positions, doodad spawns / respawns, backpack drops, land slave summons and
  transfer parts use the physics ground (static + terrain) instead of the bilinear heightmap; ship
  summons read the sea floor with `GetTerrainElevation`.
* Walking NPCs (not flying / holding / idling) stand on the physics ground (`reference_height_in`);
  NPC spawner points snap to it within 1 m (`npc_spawn_on_terrain`). Real data: 1 487 of the 1 524
  NPC spawns of the 4 busiest main_world cells are within 1 m of the physics ground (1 407 of the
  terrain elevation alone; 178 stand on brushes).
  **Visibility deviation (systems::npcs / world::unit_state, 2026-10-11):** NPC unit-state packets
  preserve the spawn/movement transform instead of the C# serializer's `GetReferenceHeight`
  mutation. That mutation could replace a collision floor with a coarse navmesh plane or reset
  an idling NPC to its original spawn Z after movement. In those 1 487 grounded spawns, 324
  navigation heights were over 1 m above ground (maximum upward difference 10.008 m), and 126
  were over 1 m below. The real collision test checks encoded positions, including static floors;
  byte-exact unit-state tests cover AI-less, walking, idle and flying NPCs and repeated visibility.
  Height queries now stay out of visibility serialization and its world/object locks. Flying and
  elevated authored spawns keep their existing positions; the 1 m spawn snap policy is unchanged.
* Fall damage (`collision::movement`, ZWUnitFell 0x3935C850): the zone's tracker (`sub_392D7600`:
  start z, max downward speed, landing on the physics ground) gates and caps the client's
  `fallVel`: no damage when `min(v, √(40·Δh)) < 20 m/s`, else the client value capped at that
  speed. The client value is converted with 32000 / 63.25 units per m/s *(inferred from the C#
  curve)*. Swimming / hanging / attached units keep the client value (the movement path
  `sub_39232570` forwards it). `FallDamageZoneCap = false` restores the C#.
* Movement verification (off by default): reasons 0, 11, 12, 15–17 and 19 are logged (no consumer).
  Deck-relative movement is composed through the live parent before collision proxy, terrain,
  water and verification queries. A vanished supporting object uses the packet's auxiliary
  world position. Supported actors reset fall tracking; the reported client fall velocity is
  still forwarded, and ordinary on-foot fall caps are unchanged.
* Ships (`physics::cry`, `CryPhysicsShips`): hull entities per `ShipUnitModel::InitPhysicalEntity`
  (`sub_39286C50`) from `ship_models`; engine buoyancy (ocean + water-volume boxes) and collisions
  (terrain, brushes, vegetation, `collide_ship` doodads, other hulls) replace the Jitter2 step, the
  C# buoyancy force and the shore / ship–ship / doodad / barrier / steep-terrain depenetration.
  Hull damage comes from the ZWUnitCollision aggregation: closing speeds ≤ 2 m/s are ignored (the C#
  dealt 1 % on any overlap); the pair maximum per 500 ms (clamped to 10 m/s) gives the C#'s
  speed-scaled percentage (nose-cone rule kept). The 1.2 data has neither the expression constants
  (12 / 13 / 21) nor the `physics_collision_*_damage_mul` columns. The keel width (0.2 m) and the
  default tube offset (half beam, the zone's `bbox.x / 2`) are inferred / taken literally. Prefab
  buoy brushes take precedence over that fallback, as in the engine.
* Water volumes (`collision::water_areas`): world slot 83 (2-D polygon areas) is not ported, so
  polygons are bounding boxes and rivers per-segment oriented boxes.

**Measured** (release, 1.2 pak, `tests/collision_measure.rs`): the 3×3 cells (9..11, 13..15): 7 849
statics, 675 models, 13 MB of blobs, 37 MB heap, 146 ms per cell, 3.5 µs per vertical ray. The whole
main_world (1 330 cell slots): 152 059 brush + 68 809 vegetation entities, 4 295 models, 90 MB of
blobs, 1.0 GB heap of which about 600 MB are the heightmap cells, 8 s; 1.0 µs per ray.

#### systems::ai geodata deviations (engine behaviour instead of the C#)

* **Combat chase (2026-10-13)**: routes are retained for stationary targets and regenerated
  after target movement exceeds the actor radius (minimum 0.5 m), correcting the reversed
  C# comparison. The cached destination is the victim's world position rather than its
  enclosing graph node. Route endpoints use actor positions, avoiding detours through the
  start/goal triangle centers; reached corners are consumed in the same tick with a 0.25 m
  tolerance, independently of weapon range. Grounded actors reach corners in XY and use the
  collision floor; flyers and underwater creatures retain 3D movement. An arrival keeps the
  travelled heading instead of evaluating `atan2(0, 0)`. References: 10.x
  `cryaisystem_dedicate` direct route `0x31062728`, `COPStick::RegeneratePath` `0x310F18E8`
  and target-movement regeneration in `COPStick::Execute` `0x310F24D8`. These are adaptations
  of the existing 1.2 chase and wire format, not a complete native goal-pipe implementation.

Reference: the 10.0.2.13 `cryaisystem_dedicate.dll` (image base 0x31000000), notes in
`D:\aa-re\zone\agent_geom\sub_navmesh.md` (§2 formats, §5 gaps) and
`rust/docs/physics/ZONE-PHYSICS-USAGE.md` §2.5. The C# (`AiGeoDataManager`, `*MissionReader`,
`PathNode`) is no longer mirrored on these points:

* **Areas version** (sector reader 0x31149764, whole-level reader 0x3107AB58): path sector
  files must be v21 (1.2) or v22 (10.x; same layout). The C# rejected `> 21`, so every 10.x
  areas file failed (no forbidden areas / boundaries / nav modifiers). Path files below v21 are
  rejected like the engine's sector reader (279 files of the 1.2 pak, v19 / v20; 272 of them
  do not parse with the v21 layout either); zone
  folder files stay accepted up to v22 (the whole-level reader still has the old-version
  branches).
* **Net / verts versions**: the 10.x engine wants net == 55 (`CGraph::ReadNodes`, 0x311030A8)
  and verts == 2 (0x311A2AC4); the C# accepted anything `<=`. Rust accepts net 55 and 54 and
  verts 2 and 1 and rejects every other version. **Deliberate deviation from the 10.x engine
  (1.2 data compatibility):** 2 299 path sectors of the 1.2 pak are net v54 + verts v1 (the same
  sectors), with the v55 node / link and v2 vertex layouts: all of them parse with 0 trailing
  bytes (gated test `real_nav_pak_12_old_sector_versions_parse`).
* **`hidemission*.bai` is not loaded** (no engine reader references it; stale v54 graphs). The
  C# read them into `HideMissionReaders` and never used them.
* **Nav-modifier header** (0x31149764): `u64 type; u64 waypointConnections; u8 altered` (17
  bytes), not `i32, i32, u8, f32, f32`; `altered` was always true (it read the low byte of the
  second u64 = 2). The `ver <= 16` (extra u32) / `ver >= 18` (light level byte) branches now
  use the file's version instead of the constant 21.
* **Links** (0x310FB7E8): byte +42 bit 0 is the engine link flag 0x800000
  (`LinkDescriptor.is_pure_triangular_link`); the C# tested `== 1`, false for every 10.x link
  (9 / 8 / 10 / 165). Byte +43 (`simple_passability_check` in the C#) is not read by the engine
  and is kept raw as `unused_43`; the floats at +28..+36 (C# length / maxWaterDepth /
  minWaterDepth) are not read by the engine and are kept as `unread_28`.
* **Merged cross-sector graph** (`geodata/graph.rs`): every folder that loads is merged into
  one `NavGraph` per world template like the engine's `CGraph`: vertices de-duplicated by XY
  (< 1e-4 m, 0x311A1D14), nodes by XY (< 1e-4 m) + equal navType (0x31101C68), links
  re-attached (0x310FB7E8; a repeated link overwrites the earlier one's attributes, radius
  stored as i16 centimetres like `SetRadius` 0x31083160, self links skipped, links to unknown
  ids dropped). Positions are compared in exact f64 world coordinates (the engine uses f32
  origin-relative ones). The engine streams a 3x3-sector window around one focus; Rust keeps
  its lazy per-cell loading (the 4x4 sectors of a cell on first use, never unloaded) and merges
  incrementally. A* runs on the graph, so routes cross sector and cell borders (a whole 1.2 cell
  merges 151 border nodes / 193 vertices); before expanding a node the cells its triangle
  touches (+0.5 m) are loaded so the neighbour cell's copy of a border triangle is merged.
* **Pass radius**: links with `maxPassRadius` below the agent radius, and blocked links
  (negative radius; -1 on ~17 % of the links), are not traversed. `PathNode::find_path` (all NPC
  callers) uses `graph::DEFAULT_AGENT_RADIUS` = 0.25 m, the X2 walkability probe radius
  (`ai_DefaultWalkability` 1, constants at 0x311FF070); `find_path_with_radius` takes the
  caller's.
* **Start / goal node and `GetHeight`** (`GetEnclosing`): the triangle node whose XY triangle
  (its 3 corners in the verts file) contains the point; the height is interpolated on the
  triangle's plane. The C# nearest-node behaviour is kept when no triangle encloses the point,
  and when the C# nearest node of the position's bai is not a triangle node: the zone instances
  navigate indoors on human waypoints (navType 4) under a few huge placeholder triangles (the
  engine picks the waypoint graph through the indoor nav modifiers, which are not modelled).
  With several enclosing triangles the one whose plane height is closest to the query z wins.
  Data caveat: the node positions of the real files lie inside their triangles but ~0.2 m above
  the plane (median; p90 ~1 m, up to ~20 m on large triangles over uneven ground, and the huge
  sparse coastal triangles can be off by 100+ m), so plane heights differ from the old
  nearest-node heights by that much.
* **Zone-geodata routes**: on the 1.2 instances only 5 of 16 synthetic test routes (dummy node 1
  at the origin to the middle node) succeed; the others' goals are not linked to the start or
  only through blocked links
  (raw-file BFS agrees); the old "routes" were presumably the C# position-based neighbour lookup jumping
  between unconnected nodes and the iteration cut-off.
* Kept on purpose: the A* step cost `G` is the distance from the link's source to the goal
  (C# quirk, `sub_navmesh.md` §5.11), the linear open-set scan and the iteration limit;
  `FindClosestToTheCurrent`, `ReducePath`, `CheckImpossibleWalk` are unchanged. Not done: link
  exposure costs, node flags, nav modifiers in the search, physics walkability
  (`CheckWalkability` 0x3100B214), the engine's 3x3 streaming window.

## Phase 3b wiring (cross-subsystem hooks)

Every subsystem installs its implementations of the other subsystems' hook traits from its
`System::start` (`install_hooks` / `bind`; tests call the same functions). Where both sides are
ported, the implementations call the owner directly (no wrapper chains); a hook slot only remains
where the owner is a later / optional subsystem (`server.systems.hooks.ai`, the services
statics) or the call crosses a lock boundary.

| Hook (slot) | Implemented by | Notes |
|---|---|---|
| `skills::ext::NpcSkillHooks` (`skills.ext.npcs`) | `npcs::bridges::NpcSkills` | NPC stats, `Npc.DoDie` (before / after, respawn time), doodad hits / spawns, gimmicks, portals, mates / slaves (direct `systems::vehicles` calls), the npc effects (`Interaction`, `Spawn`, spawner (de)spawn, `KillNpcWithoutCorpse`, `NpcControl` -> `systems::ai`, `SpawnGimmick`, `OpenPortal`, `ScopedF`, `MoveToRezPoint`), the AI parts of `OnDamageReceived` / `OnNoAggroTarget` / `CheckIfEmptyAggroToReturn` / move state. `MainWorldPosition` / `LeaveInstance` use the trait defaults (`systems::instances`). |
| `skills::ext::ItemSkillHooks` (`skills.ext.items`) | `economy::bridges::EconomyForSkills` | Gear stats / holdables / counts / consumes, `UpdateGearBonuses` buffs (equip effects, weapon wield, armor grade, item sets), the item `SpecialEffect`s through `economy::item_effects` (GainItem, emblem imprint, conversion, fishing loot, grade enchant, look convert, socketing, tempering), dyeing, skinize, sheet music (services `MusicManager`), the item effects (`GainLootPackItem`, `ImprintUcc`, `CleanupUcc`, `PutDownBackpack`, `RecoverExp`, `Conversion`, collect / craft `CraftEffect` -> `Craft.EndCraft` + `DoDoodadInteractionEvents`). |
| `skills::ext::QuestSkillHooks`, `SocialSkillHooks` | `quests::bridges::QuestsForSkills`, `social::bridges::SocialForSkills` | Item use / aggro / engage quests, `AcceptQuest` / `Cinema` effects; teams, crime evidence, `ReportCrime`, bot reports, dominion, appellations. |
| `systems::UnitHooks` (`hooks.unit`) | `skills::SkillUnitHooks` | Buff events, HP / combat side effects. |
| `npcs::hooks::{SkillsBridge, EconomyBridge, QuestsBridge}` | `skills::bridges::SkillsForNpcs`, `economy::bridges::EconomyForNpcs`, `quests::bridges::QuestsForNpcs` | NPC loot = `LootManager.GenerateLoot`; doodad func items, coffers, fish trophies, loot packs, money / labor; family / guild lookups (social / expeditions). |
| `quests::hooks::QuestHooks` (`quests.hooks`, `quests.economy_hooks`) | `quests::bridges::GameQuestHooks` -> `economy::bridges::EconomyForQuests` for the item / money / mail part | Locked hooks: the character lock is held; world work is deferred with `quests::events::after_unlock`. |
| `social::hooks::SocialHooks` | `social::bridges::GameSocialHooks` | Doodads, heights, evidence funcs, PvP enemy HP, judge, buffs, dismount (vehicles), housing faction (default = housing). |
| economy `QuestItemHooks`, `LootQuestHooks`, `CashShopQuestHooks` | `quests::bridges::QuestsForEconomy` | |
| economy `GearHooks`, `ProgressionHooks` | `skills::bridges::SkillsForEconomy` | |
| economy `NpcEconomyHooks`, `LootNpcHooks`, `TreasureMapSource` | `npcs::bridges::NpcsForEconomy` | |
| economy `LootTeamHooks` | `social::bridges::SocialForEconomy` | |
| economy `CraftHooks`, `ItemEffectHooks`, `MailHousingHooks` | `economy::bridges::EconomyComposite` | Skills cast / cancel, doodad func permission, housing zones / bonus / tax, quests `OnCraft`, public farm, house at location, backpack doodad spawn. |
| economy `AuctionMailHooks`, `CashShopMailHooks`, `StoreHooks`, `SummonItemHooks` | `economy::wiring`, `economy::mate_equipment` | Economy-internal. |
| housing `HousingEconomyHooks` | `economy::bridges::EconomyForHousing` | Items, money, coffers, housing mails, tax mail updates. |
| vehicles `VehicleItemHooks` | `economy::bridges::EconomyForVehicles` | `EndCraft`. |
| ai `ShipPhysicsHooks` (`hooks.ship_physics`), services `RadarShipHooks` | `vehicles::ship_physics::VehicleShipPhysics`, `vehicles::RadarShips` | Harpoon lines / tension / lifetime, physics throttle / steering; radar ship lists. |
| ai `AiHooks` (`hooks.ai`), services `ASTAR_HOOKS` | `ai::hooks::{AiSubsystemHooks, AiAStarHooks}` | |
| services `WebApiEconomyHooks`, `CHARACTER_ITEM_OPS`, `SheetMusicInventory`, `BUYBACK_WIPE` | `economy::bridges::{EconomyForServices, EconomySheetMusic}` | |

Integration tests: `systems/integration_tests.rs` (skill kill -> hunt quest + exp, doodad
interaction quest, force-attack crime evidence, party-tagged kill exp sharing, TagShare credit,
quest item hooks under the character lock, skills item hooks on the inventory, death exp loss).

Deviations of the wiring:

* Quest "locked hooks" run under the character lock: their world parts (level-up broadcast,
  item task packets, quest item events, quest reward mails) run once the lock is released
  (`after_unlock`), after the rest of the quest step; a failed reward mail `Send()` is reported
  then (`MailUnknownFailure`) instead of being counted by the quest step.
* The item operations of the quest hooks use an `ItemTxn` without the economy operation lock
  (taking it under a character lock could deadlock with an operation waiting for that
  character).
* `AcceptQuestEffect` approximates the C# `ImplId` lookup by the effect's quest id.
* Item procs of equip item set bonuses are not registered: `UnitProcs.RollProcsForKind` skips
  every proc whose cooldown has elapsed and `LastProc` starts at `DateTime.MinValue`, so no proc
  ever fires in the C#.
* `ItemSkillHooks::create_sheet_music` and `TimedRewardsManager.DoDailyAccountLogin` run their DB
  work as tasks (the C# blocks).
* AStar GM commands read the NPC's `PathNode` only when its AI is not in the middle of a step
  (`manager::post`); otherwise they see the defaults.
* The client time-of-day override in storms (`TimeOfDayObserver` + `StormShipLogic`) resolves
  the character through a weak reference held by its time observer.

C# bugs mirrored by the wiring: a fall death generates crime evidence against the victim
itself (the faller is its own "killer"); the killing blow's aggro / tagger is added to the dead
NPC after `DoDie` cleared them (`OnDamageReceived` runs after `ReduceCurrentHp`).

## Known deviations from the C#

* Logging (`aaemu_commons::logging`, login and game): `Logs/` lives under the application
  directory instead of the executable's directory, so servers built into one target directory no
  longer share (and delete) each other's files. NLog's `deleteOldFileOnStartup` became "move the
  previous run's `Server.log` / `Error.log` to `Logs/Previous/`" (newest 9 kept). Additionally,
  `aaemu_commons::crash` logs every panic with a backtrace and appends it to `Logs/Crash.log`.
  On Windows x64 it also catches fatal native exceptions (stack overflow, access violation) and
  writes the unwound, symbolized stack and a minidump (`Logs/Crash/`). Release builds carry line
  tables (`[profile.release] debug = "line-tables-only"`) for those traces.
  The default levels are `Info` for the console and `Server.log` (NLog: `Debug` / `Trace`), since
  the per-packet `Debug` / `Trace` lines dominated the send path with many players in view
  (`RUST_LOG=info,aaemu_game::net=trace` restores the packet log); the log files are written by
  a background thread.
* net (game connections): sending never blocks and C# had no limit; here a client with more than
  16 MiB queued, or one that accepts no data for 20 s, is disconnected (`net::outbox`,
  `net::listener`). Broadcasts encode a packet once for all recipients.
* systems::skills (character creation): the chosen ability's first starting spell occupies
  hotbar slot 1. The shared action originally assigned there moves to the starter's free slot,
  retaining the other authored utility and supply shortcuts. Further starting spells skip
  occupied slots. This replaces the C# layout that puts all shared defaults ahead of the
  chosen starter; saved hotbars are only loaded, not rebuilt.
* `LGPlayerEnter` for an already registered connection id: C# `Dictionary.Add` throws inside the
  login handler, which has no catch (the exception escapes into the network client, the world is
  shown inactive afterwards). Rust replaces the stale entry and replies 0 (see "Live testing").
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
  unit formula evaluates to 0 (C# throws). (Superseded: see systems::skills below.)
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
  ignored like a non-matching `targetUnit` cast (superseded: vehicles, sticky parents, fall
  damage and the Sus analysis are ported); `LastPacketActivityTime` is `systems::services`.
* `LeaveAllChannels` iterates channels in key order (C# `ConcurrentDictionary` order is
  unspecified). Trial channels do not exist.
* The C# keeps `Connection.Characters[id]` and `ActiveChar` as the same object; Rust copies the
  active character back into the lobby list when `LeaveWorldTask` runs.
* Combat / slave logout delays (x30, +10 min) are not applied (no combat state / slaves).
* systems::economy: item operations are serialised by the economy operation lock (`ItemTxn`) and
  their packets / quest events are flushed after the container locks are released, in the C#
  order; character packets sent during an operation go through the same queue (`Out`).
* systems::economy: `CSRepairPetItemsPacket` repairs owned mate equipment containers at the
  current nearby blacksmith (the native 10.x button at `0x399093D0` supplies its NPC id).
  Repair bills cannot exceed available money; repairing destroyed equipped gear restores its
  bonuses. Mate gear changes and item unpacking require the authenticated owner. Received-mail
  read, attachment and delete operations require the receiver; sent-mail reads require the sender.
  Auction cancellation requires the seller. Coffer opens and moves reuse the existing chest
  permission rules and require a nearby live coffer; moves require the caller's open session.
  Ordinary inventory packets resolve item ids only inside the caller's inventory containers.
* systems::economy: `AddOrMoveExistingItem` with the item's own container as target relocates the
  item (the C# inserts the same object a second time into `Items`). `SplitOrMoveItemEx` looks up
  the source / target items in the two containers only (the C# `GetItemByItemId` searches every
  item; an item in a third container is treated as a phantom item). Where the C# throws
  (`KeyNotFoundException`, `NullReferenceException`, `AcquireDefaultItem` failure), the port logs
  and aborts the operation; `Wipe` drops an item `CanDestroy` refuses instead of looping forever.
* systems::economy: trade items are kept by id (an item no longer in the giver's containers or put
  up twice is an error); a missing trade partner is ignored instead of half-applying the change.
  Leave-world and hard disconnect cancel the departing actor's trade and prune its invitations
  before world removal, notifying the peer through the existing cancellation packet. Cleanup
  is idempotent and rejects an old player handle when the object id belongs to a new session.
* systems::economy: auction lots are enumerated in id order (`ConcurrentDictionary` order in C#),
  search / cheapest lot skip lots whose item or template is missing; mails are kept in a map that
  reproduces the C# `Dictionary` enumeration order; mail dates are whole unix seconds.
* systems::economy: `CSSetCraftingPayPacket`, `CSChangeItemLookPacket`, `CSSpecialtyRecordLoadPacket`,
  `CSBuySpecialtyItemPacket`, `CSReturnMailPacket` have no 1.2 opcode (0xfff): decoders exist, the
  1.2 client never sends them. The specialty / trade-pack mail delay use the C# defaults (no
  `Specialty` config section yet).
* systems::economy: the localized item names are loaded by four modules (items search, auction,
  mail, cash shop); a later cleanup can share one cache.
* systems::services: authenticated `CSRequestCharBriefPacket` and `CSCharDetailPacket` requests
  return online character profiles through the existing 1.2 outbound formats (the C# inbound
  handlers only logged). Offline, absent and unauthenticated requests receive no reply; offline
  lookup and failure-response semantics remain unresolved.
* systems::social: family title and steward requests require targets in the caller's own family.
  Authorized packet changes call the existing family snapshot save immediately; the C# handlers
  only changed memory. Team risky actions require membership and known actions, with owner
  authorization for kick/dismiss and a self target for leave before any side effects.
* systems::social: `SCChatMessagePacket` writes the packed 64-bit chat identity `(type, sub type,
  faction)` in its first eight bytes (direct Say / whisper / system text uses `(type, 0, 0)`); the
  sender faction stays in the sender block. Channel copies substitute the actual `ChatChannel` sub
  type / faction while keeping the requested chat type (zone Shout / Trade / LFG and raid /
  raid-leader included).

* systems::social: justice repairs intentionally differ from C#: crime/infamy accumulation and
  sentence durations saturate instead of wrapping; a live evidence doodad can be reported once.
  Arrests cannot create duplicate cases, and the holding-cell offender buff is applied. Jury
  acceptance requires an actual invitation, current eligibility and the court's faction; only
  the occupied seat's player can confirm records or submit a valid, final vote. Repeated pleas
  and terminal-case cancellation cannot resentence the defendant. Justice teleports update
  server transforms, instance membership and visibility, and yaw is consistently in radians.
  Every exit path cleans jurors and removes trial buffs; returns respect `AllowJuryEscape`,
  and cleanup releases only its own court.
  Disconnects vacate jury seats or close the defendant's abandoned case while retaining the
  pending sentence (minimum one stored minute) and court region. Login enforces it independently of jury
  eligibility. The normal-save bad buffs 631/2028 persist even their final seconds; their
  non-real-time duration resumes on reconnect. The existing 1.2 plurality sentencing choices
  and first-seat tie ordering remain. Tests cover all six verdicts, default sentencing,
  invalid/duplicate votes, occupied courts, disconnects and real compact prisoner definitions.

* systems::quests: rewards, items, money, exp, skills, NPC faction, teams, mails go through
  `quests::hooks::QuestHooks` (`GameQuestHooks`; the item / money / mail part is the economy's).
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
* systems::quests: the daily reset runs at 00:00 UTC (C# cron in server local time).
* systems::quests: the October 2026 quest audit corrects timer event isolation, explicit NPC
  reporting, per-quest NPC/doodad reporting, doodad reward selection/finalization, and OR
  requirement evaluation. Timed saved deadlines, aggro credit, CheckSphere, and authored
  content gaps remain tracked in `Docs/wiki/Quest-Audit.md`.
* systems::social: saves of guilds / families run inline in the handler (C# synchronous); a
  failed guild save keeps the removed-member ids for the next attempt. `ExpeditionConfig`
  defaults to the shipped `Expedition.json` (C# throws without the section). Player names /
  positions in team packets are snapshots taken when the packet is built. `DeleteCharacterAssets`
  does not send the guild/family packets to the lobby character. The duel flag height comes from
  the npcs heightmaps (`SocialHooks::terrain_height`). Crime record packets prefetch offline
  victims (C# `Character.Load` inside `Write`).
* systems::skills: supersedes the phase-2 bullets above on stats (`CalculateWithBonuses`,
  `UpdateGearBonuses` are ported), buffs (expiry, `BuffRemoveOn.Move`, effects, buff packets) and
  `CSMoveUnit` fall damage.
* systems::skills: `CompressedGamePackets` (DD04) are written with stored (uncompressed) deflate
  blocks; the client accepts any valid deflate stream.
* systems::skills: the radar scans of `SkillTargetingUtil` are not ported.
* systems::skills: `SpecialEffect` repeats are scheduled tasks instead of `Thread.Sleep` loops.
* systems::skills (2026-10-12 audit): OR requirements now require one success and use the resolved
  packet target, including doodads. Equipped-item and NPC-group requirements check their actual
  membership; crime-point limits and overnight time windows are enforced. Source/target life,
  swimming, stun, walking, angle, range and optional physics
  obstruction checks run before GCD/buff/resource changes and again at cast completion. Missing
  mana preserves ongoing buffs and cast timing, including mana drained during a cast. Labor and
  reagent availability are checked before starting and firing, including alternative reagents.
  Active direct casts cannot be overwritten; a
  new cast either stops a channel under its configured policy or is rejected.
* systems::skills (2026-10-12 audit): channel buffs have distinct caster/target owners. Direct
  channel ticks spend mana and apply effects; channels without ticks apply their effects once
  at completion. Cancellation invalidates timers, cleans up both buffs and emits one end
  notification without clearing a newer task. Repeated stops cannot complete a timeline twice;
  canceled delayed effects are suppressed, and a reused skill resets its per-cast state.
  Repeated special actions carry their cast generation, so canceled or reused casts cannot
  act through old callbacks after their timeline ID has been released.
  Delayed effects and repeated actions require the captured world handles to remain live;
  despawned or recycled objects are skipped, and an abandoned delayed cast releases its timeline.
  Channels also stop on despawn; target buff cleanup compares the original handle identity
  before removing anything from a reused object ID.
  Delayed SkillUse actions retain both entity handles and their originating cast generation;
  canceled or reused casts cannot launch a child skill. Delayed disturbance, buff timers and
  controllers also reject removed or recycled entity handles.
  Auto-attack callbacks require the current skill and cancellation token, so a stale loop
  cannot attack through a replacement character or clear a newer auto-attack slot.
* systems::skills (2026-10-12 audit): direct AoE respects cone angles; individual guaranteed-hit
  effects bypass miss results without changing other effects' cached rolls. Magic miss/resist
  joins the miss set. Percentage rolls use the exact 0/100 endpoints, retaining SkillUse's
  explicit 0 sentinel. Buff unit events follow the owner; Landing, Attacked, ChannelingCancel,
  RemoveOnMove and RemoveOnDamage execute, including short jump landings without fall damage.
  NPC AI faces a non-self target before cast angle validation; turning only after a successful
  cast prevented sideways melee attacks. Self-targeted skills retain their heading.
* systems::skills (2026-10-12 audit): Create Mana/resource conversions, percentage health/mana
  restoration, server cooldown resets, direct-cast interrupts, spawner despawn and Escape's
  MoveToRezPoint timeout now execute. Charges accumulate before refresh/notification; target
  charges use their own buff and multiplier; both actor radii scale. Explicit buff removal
  dispels once, knockback immunity reads text booleans, doodad summons keep radians and the
  selected frame, Return keeps radians in its fallback and sends both notifications, and
  negative breath changes clamp at zero. FakeDeath clears incoming aggro without killing the
  target (native WZFakeDeath handler 0x3936F060). PlayLogEffect is recognized as telemetry.
* systems::skills (2026-10-12 audit): the extracted 1.2 client database and server database have
  the same 15126 skill IDs and 9816 buff IDs. Both lack 33 unique buff templates referenced by 35
  BuffEffect definitions. The loader warns with both IDs; replacement templates are not
  fabricated. Historical RedeemBuff/ExplodeBuff formulas, recipe/account/cinema placeholders,
  several other special actions and generic Anchor/Flowgraph still need authoritative behavior
  or protocol evidence. Passing covered tests does not certify every data-defined skill.
* systems::skills: the labor margin requirement uses `GetMaxLabor(Payment.PremiumState)`; every
  connection has the default C# `AccountPayment` (premium until 2030), so the cap is 5000.
* systems::skills: `Character.DoDie` is complete (exp / durability loss, escalating respawn timer,
  PvP honor / assists / kill counts, War-zone honor loss, `DropTradePackToFloor`). The PvP
  damage / heal histories are lists of (character id, unix ms) in `skill_char`.
* systems::skills: skill controllers and NPC displacements snap to the ground through the npcs
  heightmaps (`geo_data_height`); the wandering (fear) speed is the 7 m/s fallback (actor model
  stances not loaded); `Npc.StopMovement` writes the C# default stance / alertness; `OnMovement`
  is not raised.
* systems::skills: `BubbleEffect` does not sleep the effect thread for the bubble's read time;
  account updates of special effects run as async tasks.
* systems::skills: `saveFactions` KeyNotFound (C# throws) is logged as an error.
* systems::skills (surfaced by the `AAEmu.UnitTests` ports): a Rust `Skill` always has a
  template — the C# `new Skill()` / `new Skill(null, null)` (null template) is a skill of the
  default id-0 template; `Unit.Name` is a `String`, so the C# `null` name is the empty string;
  `ObjId` lives on the world handle (`Entity<D>::obj_id`) and is fixed at creation (no setter);
  buff / cooldown time checks take the clock as an explicit argument (unix ms) instead of
  reading `DateTime.UtcNow`; `local_impulse_to_world_displacement` takes horizontal (x, y)
  positions only, because the C# zeroes the direction's Z so the vertical offset never
  contributes.
* systems::npcs (2026-10-13): corpse timers read the current despawn field, so fully looting
  an NPC applies the existing shorter deadline instead of retaining the five-minute loot
  extension captured at death. Only living NPCs count towards spawner population limits;
  corpses remain available for loot/event cleanup without blocking the authored respawn
  delay. Native `Npc::OnDeath` (`x2game-dev_dedicate` `0x393452D0` -> `0x39345220` ->
  `0x39344AA0`) releases its population slot at death. This intentionally replaces the
  C# `HasCorpse` gate; spawn scheduling and corpse expiry remain separate timers.
* systems::npcs (2026-10-13): generated fallback spawners are kept for the entry that created
  them, so an NPC without authored spawner IDs retains its first placement; this intentionally
  fixes the mirrored C# omission that never spawned a generated spawner's first member.
  Duplicate filtering compares the authored position including Z (< 2 m on each coordinate),
  so identical entries collapse while cave / building floors stay distinct. Real main_world
  recount: 25 118 raw entries, 1 011 true duplicates, 0 missing templates, 24 047 ordinary
  retained placements, 38 174 ordinary spawner clones and 60 event entries (six placements /
  eight clones more than the earlier 38 166). Ordinary populations target the authored
  `Count` / `min_population` baseline (default 1), grow with nearby players and stop at
  `max_population` or a positive `suspend_spawn_count`; capped member selection uses the
  authored weights. Living populations exclude corpses and include the NPCs of alternate
  spawners sharing the caps; each pending respawn slot keeps its own authored deadline, so
  staggered deaths respawn independently. Back-end tests cover the limits and deadlines
  (including a 55 s authored respawn); a freshly built release server and a real 1.2 client
  entered the Bleakbreath Cave interior (GM teleports to the lower cave and upper passage,
  no spawn commands): screenshots show the Mistfiend and Wisp, and telemetry reported all
  six `npc_fogmonster` Mistfiend entities at cave heights below the terrain, plus a Mist
  Skeleton. This does not establish retail population density or validate every cave.
* `systems::npcs`: doodad func timers are tokio tasks (cancel = generation bump); `DoodadManager.Create`'s `Task.Run(InitDoodad)` is a spawned task; `TimeManager.OnTimeOfDayChange` runs from a 10 s poller. `GetNpcSpawner` template rewrites do not propagate to already loaded spawners; the "first" concurrent-dictionary entry is the lowest key; spawn files are processed in sorted name order (C# `Directory.GetFiles` order is unspecified). Portal book deletions are written at the next periodic save. Debug messages check only the character's access level. Coffer slot counting stops at 255 (C# loops forever). Where the C# would throw on a null / failed cast the Rust code returns quietly. Sticky parents are not modelled.
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
* systems::instances (tower defense): the C# command only sent packets to its caller and hard-coded zone group 5. The Rust runtime reserves controller positions from the existing NPC JSON by compact spawner membership; these controllers no longer spawn permanently. Manual controls affect the caller's instance/group. Automatic schedules are disabled by default and validate every selected event at startup. Orphan waves/targets no longer truncate later valid rows. Kill notifications use only `DoDie`, deduplicate the NPC handle and retain id-reuse semantics. Timers use monotonic real seconds; `tod` uses the accelerated game clock and day intervals count from scheduler startup. Spawn effects carry explicit event ownership for cleanup even when Unit.OwnerId is zero. Unsupported `DoodadAlmighty` targets, NPC groups without authored controller positions and reward/victory chains remain unavailable rather than being synthesized.
* systems::instances (dungeons/mentoring): replaces the mirrored double entry-item charge and unsafe dungeon destruction below. Each character pays once per run; re-entry is free, capacity counts pending admissions, and creation obeys `World.MaxInstances` including concurrent requests. Empty owner/leader resets remove the whole run binding; destruction cancels its loader and returns occupants before removing their world. Party join grants access without inserting a player into another world's object table. Mentor eligibility is captured at boss death for nearby living party members with complementary active quests, rechecked during interactions, and bound to selected chest participants. Four explicit 1.2 quest/chest bindings are verified by a gated real-data test. Missing reward chests use the authored templates at the boss position (two metres apart for the two-chest dungeons); this placement fallback and qualification policy are emulator extensions, not claimed native-server execution parity. Authored phases grant seal items through existing inventory/quest hooks and daily persistence; Gilda is not issued directly by mentoring.

* systems::vehicles: supersedes the phase-2 `CSMoveUnit` and `LeaveWorldTask` bullets above:
  slave / vehicle / mate moves, riding, standing on objects, the mate / slave logout handling
  and the +10 minute logout delay with an active vehicle are ported (combat x30: skills).
* systems::vehicles: the transfer motor is not a transform child of its boarding part (the C#
  sets and immediately clears that parent); the boarding part is moved explicitly by the motor's
  tick, as the C# does for its packets. The transfer's own sticky link to its motor is not
  recorded (only `IsBoardedOnTransfer` reads sticky parents of transfers, and the C#
  `AttachedCharacters` list is never filled).
* systems::vehicles: `Transform.Parent = null` keeps the world position (`detach_child`); the C#
  keeps the local values until the next position update.
* systems::vehicles: `Thread.Sleep(50)` between `SCMateSpawnedPacket` and the mate spawn is not
  reproduced. Slave weapon dps is 0 (mate / slave `UpdateGearBonuses` is ported).
  Ship physics, water and the storm cannon block are `systems::ai` (`ship_physics`).
* systems::vehicles: a stale (already deleted) mate left in the active list by the logout bug
  below is despawned again without releasing its obj / tl ids a second time.
* systems::vehicles: DB failures of the saved-slave lookups behave like "no saved slave"; the
  slave / mate saves outside `DoSave` are fire-and-forget tasks.
* systems::vehicles: a missing unit formula evaluates to 0 (C# NullReferenceException); index
  errors of `NextPoint` / `ShipyardSteps[-1]` are logged and end the call (C# exceptions).

* systems::services: the WebApi 500 page shows the Rust error chain instead of a .NET stack
  trace, and System.Text.Json / NotSupportedException messages are close paraphrases. Character
  `CreatedAt` of characters created in the running session is written without the `Z` of a
  `DateTimeKind.Utc` value. The auction controllers serialise the lot's `Item` with its stored fields only
  (Id, TemplateId, Count, Grade, OwnerId, SlotType, Slot).
* systems::services: `TaskManager.GetQueueCount` (WebApi `/status`) only counts the tasks of
  this queue; the other subsystems run their timers as Tokio tasks.
* systems::services: `TimedRewardsManager.DoTick` skips connections without an account (C#
  `GetAccountDetails(0)` inserts an `accounts` row 0); the payment of a connection reached from
  the tick is the default `AccountPayment` (it is never changed in C# either).
* systems::services: `AppConfiguration.Instance` mutations (World set commands,
  `ReloadConfiguration`) replace the `ConfigCell` snapshot; subsystems that copied values at
  start (skills config, doodad growth rate) are updated by the World set commands only.
* systems::services: `SusManager` analysis runs only where callers pass positions (no
  `FinalizeTransform`); the social and coffer `LogActivity` calls insert their row as a task.
* systems::services (commands): a command whose C# body is synchronous but calls an async Rust API
  (saves, reloads, DB, mails) runs through `execute_async`; `ReloadConfigs` replaces the whole
  configuration (C# `Bind` keeps runtime values of keys absent from the files) and refreshes the
  copies the subsystems keep; `Announce` only knows the web color names (no system colors);
  `DisconnectMe` uses `ConnCommand::DetachAndClose`; `scripts reloadslavepoints` keeps the
  reloaded table in `vehicles::System` (game data is immutable); `ChangeLevel` help shows the
  level caps until the experience table is reachable; the `Fly` cache is per command instance;
  `Kit` sorts kit names case-insensitively (culture sort approximation); `ShowInventory`
  "Container Error" compares with the listed container; `CrimeFakeTrial` awaits `ReportCrime`;
  `QuestCmd` truncates acceptor type / status to bytes; the sphere list uses the global sphere
  manager; `Help` sorts ordinally (same order as the culture sort for lower-case names and `_`).
  The harness gives players fixed obj ids (`0x100 + id`) that can collide with ids from
  `server.ids.object` in tests that spawn many objects.
* systems::services: the Discord bot runs in the background (the C# `StartAsync` never returns
  when a token is set, blocking the host start).
* systems::ai: calls into an NPC's AI from outside its tick (skills, aggro, npcs hooks) are queued and run right after the current AI step instead of re-entering it (`manager::post`); the AI tick runs on a blocking thread every ~100 ms; behaviour instances are kept per AI and accessed by kind. `UpdateAggroHelp` reaching a helper NPC without an AI logs and continues (the C# throws a `NullReferenceException` that cuts the whole behaviour tick short). `Npc.OnDamageReceived` on an AI-less NPC does nothing (C# NRE).
* systems::ai: deletion marks an AI removed without waiting for its state mutex; the current holder detaches its owner and discards queued callbacks. This permits an NPC skill to delete its own caster and avoids the spawn-lock / AI-lock inversion during despawn. The global tick skips an AI whose state mutex is busy and retries on the next tick, so one external AI call cannot freeze movement and combat for every NPC.
* systems::ai physics: Jitter's narrow phase (`MprEpa`) and contact solver are replaced by a box-vs-heightmap-triangle SAT with one contact per triangle and sequential impulses (Jitter's default friction / bias); the integration itself is exact. One physics world per instance is created with the first ship (C# starts a thread per instance at boot). Add / remove / helm / driver changes go through the pending queue (next tick); hull damage, movement packets and transform writes are applied after the tick; readers use per-tick snapshots. Water is kept per world template and a cell's `object.dat` is ingested when the physics or a water query sees the cell loaded. A height lookup error counts as height 0 (the C# exception skips the rest of that ship's tick). A ship whose game object is gone is skipped. Ship–ship harpoon tow resolves an explicit hook basis among the ships simulated this tick.
* `WorldManager.Initialize` (`ActiveRegionTick`, 1 s, `useAsync`) and `IndunManager.Initialize`
  (`IndunInfoTick`, 30 s, `useAsync`) do not subscribe to `TickManager.OnTick`: they run as 1 s
  Tokio interval tasks started once per server (`npcs::spawn_manager::start_active_region_tick`,
  `instances::start_ticks`), which never overlap themselves (the async subscriber's busy skip).
  The C# `WorldManager` query API is split by data owner: template / zone-key lookups are on
  `game_data.worlds` (`WorldTemplates`), `GetAreaShapeById` / `GetWorldInteractionGroup` on
  `game_data.systems.npcs.area_trigger`, the rest on `world::WorldManager` (`GetTargetOrSelf` is
  `target_or_self`, `None` = the C# `null` name). Unit tests: `world/world_manager_tests.rs`.
* systems::npcs: `Doodad.PlantTime` is unix seconds and the guard-time checks (`IsProtectedByPublicFarm`, `IsPublicPropertyByAge`) take `now` in unix milliseconds, so their deadlines resolve to the millisecond instead of the 100 ns `DateTime` tick (the comparisons themselves are the C# ones).
* systems::ai: flying NPCs (`CanFly`) keep the interpolated 3D movement step of `MoveTowards` instead of being pinned to the spawner's Z, and skill min / max range is a 3D check for them (ground NPCs keep the 2D range; the melee skill-2 four-meter guard and the skill selection use the same 3D distance), so a flyer cannot cast at a victim far below before closing in; `ReturnState` completes on the 3D distance to the idle position (arrival at `MoveTowards`'s 1 m tolerance; the timeout teleport counts vertical displacement), so a flyer that fought below its spawn altitude climbs back to it instead of stopping at the home XY.
* systems::ai (2026-10-13): walking floor queries (`reference_height_in`) start at the walker's
  current level and accept only a collision floor within the 2 m upward allowance; a valid
  Z = 0 floor is accepted, terrain above a cave roof is not, and the current height is kept
  when no reachable floor exists. Flying / holding / idle NPCs keep the documented height
  behavior. Real 1.2 data: the six Mistfiend (NPC 4227) points in Bleakbreath Cave have
  terrain 91–145 m above the authored positions, no downward probe reaches a floor and the
  corrected walking height stays at the authored cavity level (556.25–593 m).

## Live testing (phase 5): fixes and deliberate deviations

Found by running the servers against MySQL, the real `compact.sqlite3` / `game_pak` and the
`aaemu-testclient` bot, and by diffing the S->C traffic of the same scenarios against the C#
servers (see `rust/LIVE-TESTING.md`). Fixed porting bugs are in the git history (commit
"live-test porting fixes"); these are the remaining, deliberate differences:

* Game DB sessions run at `READ COMMITTED` (`after_connect`). Under the server default
  (`REPEATABLE READ`) concurrent character saves (`DELETE FROM character_active_buffs WHERE
  character_id = ?` + `INSERT`) deadlock on next-key gap locks (seen with 50 bots logging out at
  once; the C# has the same pattern but rarely overlaps since its saves are slower).
* Periodic world ticks (active-region spawning, regen, area triggers, gimmicks, quest spheres,
  instances, vehicles) run on the blocking pool (`systems::run_blocking`), one run at a time per
  ticker, instead of on the async workers.
* The heightmap cell of an NPC is loaded before `WorldInstance::spawn` takes the grid lock
  (the C# loads it lazily while writing `SCUnitStatePacket`); same result, no multi-second
  stall of the whole world grid on the first visit to an area.
* Positions are saved with full `f32` precision. The C# (MySql.Data float parameters) stores
  them with ~6 significant digits (`15578.041` -> `15578.0`), so a re-login spawns a few cm away
  from where the C# would put it.
* `X2EnterWorldResponsePacket.token` (= game session id) is a counter (see above), so stream
  `CTJoin` tokens are small integers rather than `RemoteEndPoint.GetHashCode()` values.
* A character created in this session keeps its in-memory state until it is selected (as C#
  keeps the `Character` object in `Connection.Characters`): its gear buffs (indices 2..8) exist
  before the first select and are not announced again. A character loaded from the DB on a
  later session gets fresh buffs on select in both servers.
* `UpdateGearBonuses` is ported for characters, NPCs, mates and slaves (the "Stats" bullet
  above predates it).
* A client that takes its world cookie (`CAEnterWorld`) but never connects to the game server
  leaves `EnterWorldManager._accounts[connectionId]` behind, and the login server leases the
  same (lowest free) connection id to the next client. C# then throws in `AddAccount` and stops
  answering the login server (every later enter-world is denied, the world shows as inactive).
  Rust replaces the stale entry and answers.

Parity status of the scripted scenarios: `login`, `lobby` and `full` (fresh character) produce
the same S->C traffic as the C# servers after id / time normalisation (0 findings); a second
`full` round with the existing character only differs in the saved position precision above
and in NPC wandering / buff remaining times.

## C# bugs noticed (mirrored, not fixed)

* systems::social: `AchievementGameData` reads the `'t'` / `'f'` text booleans of compact.sqlite3 with
  `GetBoolean(column)` (`GetInt64 != 0`), so `is_active`, `is_hidden`, `complete_or` and
  `or_unit_reqs` always load as `false`.
* systems::social: `AskRiskyTeam` makes the acting unit (the kicker) leave its chat channels;
  a member joining an existing team never joins the party / raid
  chat (but can still talk into it); `IsMarked` compares mark object ids with character ids;
  `ExpeditionManager.ChangeOwner` does not update `OwnerName`; `ReplyInvite` does not check an
  existing guild; `ChangeExpeditionRolePolicy` requires the Expel right; disbanded guilds stay in
  the list; family invitations always carry family id 1; hard disconnects keep the family member's
  character set; `GetTrialChat` throws when the faction has no court and the player is in no trial;
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
* `Character.Save` writes the rotation columns crosswise (`roll` <- Rotation.Z,
  `yaw` <- Rotation.X), consistently with `Load`, so the round trip works. The former omitted
  `reported_as_bot_count` column is fixed in the feature audit below.
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
* systems::quests: `QuestActObjZoneMonsterHunt` (non-score) needs `count > Count` but the
  objective is capped at `Count`: never completes.
* `systems::npcs`: the "spawn all" schedule check is inverted; `DoRandomSpawn` leaks an obj id and spawns twice; a doodad despawn may release its obj id twice; gimmick spawn rotations are not converted from degrees; scheduled doodad spawns stack cron tasks; the player-in-radius check scans every player; NPCs are not despawned when players leave.
* `systems::npcs`: dated game schedules compare `StartTime` / `EndTime` (usually 0) instead of the start / end hours (a one-day event counts as ended at 00:00); the cron builder tests the captured day of week instead of its parameter, and undated schedules produce day / month 0 (rejected by the cron parser); `VendorExist` reads the `id` column; deleting a portal sends no packet; `NotifySubZone` re-sends the portal book once per recall portal of the sub zone; the exit portal's yaw is the book rotation in degrees.
* `systems::npcs`: corrected coin double credit and failed trade-pack acquisition; `BuyFish` uses the checked economy sale. Phase operations reserve live object identity, honor interaction counts, enter functions on time-of-day changes, and cancel superseded timers. Saves/deletes use a FIFO writer drained by the save lifecycle; partial interaction counts use the doodad-interactions SQL update. Scheduled spawns recreate deleted doodads, deduplicate cron callbacks, and retain original world identity. Logic links/families route matching true/false pulses with per-object state and cycle guards; client-created wires require ownership and reach. Immediate gates are supported; delay descriptors 2/3 need reference behavior. `SpawnGimmick` creates the authored object with offsets/faction/kinematic motion; `SpawnMgmt` requires explicit authored `SpawnGroupId` membership. Prefab flow graphs are client-owned. `CheckGrowth` still runs funcs with `nextPhase = 0`; child metadata templates (e.g. `DoodadFuncBuyFishItem`) remain in the phase dictionary.
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
* systems::economy: auction — the "ExpireDate" sort still uses PostDate and a buyout
  sends no SCAuctionBidPacket. Original cancellation-copy and missing balance checks
  are corrected in the quest/mail audit below.
* systems::economy: mail — opening a mailbox re-sends a "got mail" per unread received
  mail; FinalizeForBidFail still uses AucOffFail; receiver lookup is case-sensitive.
  The old return failure and sender unread-count bug are corrected below.
* systems::economy: trade — `OkTrade` proceeds when only one side locked, the put-up amount is only
  checked (the whole item changes hands), negative money put-ups are accepted, trade ids are never
  released, the owner's `ItemRemove` in `SCTradeMade` names the receiver's slot.
* systems::economy: cash shop — loyalty is charged at the undiscounted price, the max-level check
  sends the low-level error, page 0 behaves like page 1.
* `systems::instances`: `RegisterIndunEvents` runs in the `Dungeon` constructor and again in the loader, so every indun event handler is subscribed twice; a dungeon kill raises `OnUnitKilled` (and the victim's `OnDeath`) in `PostUpdateCurrentHp` and again in `DoDie` — NPC-killed action chains run four times; `IndunEventDoodadSpawneds.UnSubscribe` adds the handler (`+=`); `AreaClearTick` stops at the first cleared room (`return`); a new dungeon verifies the requirements twice (the entry item is consumed twice) and the creator gets `SCProcessingInstancePacket` twice; `Transform.CloneDetached(character)` / `MainWorldPosition.Clone()` fire `OnZoneChange(0, zone)` (the instance zone chat is never left); `ApplyWorldSpawnPosition` stores the spawn rotation in degrees; leaving an instance sends the (radian) return rotation through `DegToRad` again and `SCLoadInstancePacket` names `MainWorldPosition.WorldId`; `DestroyDungeon` removes the players from the instance without moving them out; `InstantGame.RemovePlayer` looks the corps lists up by 0 / 1 instead of the faction ids (never cleaned); `AddScore` always sends `Corps1` / `score2 = 0`; the kill packet's victim count is the victim corps' kills; entering a battlefield sets the corps faction, so `Character.SetFaction` kicks guild members of another mother faction from their guild and party; offline queue applicants are never pruned (`CharObj` is never null); `RemoveGame` leaves the game in the queue list; battlefields.json loops stop (`return`) at the first orphan row.
* systems::housing: abandoned-house removal buff 2250 now applies its original effect
  1876 through the normal damage path. C# rejected the house attacking itself, so the
  building never decayed. Only the active server-owned removal tick on that same unowned
  house bypasses the relation check; immunity and ordinary attack checks remain intact.
  The original 10 HP / 15-second tick and death cleanup are tested, including an original
  compact.sqlite3 gate. Demolition still clears ownership before the later decay death.
* systems::housing: Castle reference loading now includes guard_tower_settings, guard_tower_steps,
  siege_plans and siege_zones, preserving stage order and naive reference dates. These
  records remain read-only; persisted Dominion authority now handles claim, retained-tower
  damage, roster membership and the original five-minute engraving capture. Schedules are
  explicit GM operations using authored zone durations, with self-registration through
  `/joinsiege [zoneGroupId]` and `/leavesiege [zoneGroupId]`. The zone defaults to the caller's
  province; the scheduled guilds determine the side, and the current guild rank's JoinSiege
  policy controls enrollment. All shipped ranks allow it. Players cannot enroll another
  character or change the schedule; leaving also cancels the caller's engraving. GM scheduling
  with player registration is the chosen server policy rather than historical auction calendars.
  Any registered current attacker may
  engrave; initial claim requires the current guild leader and exact Purified Archeum pack.
  Capture transfers the nine independently itemized fort designs to the new guild leader,
  preserves ordinary property owners, and applies authored fort-count protection stages.
  Guild-owned fort limits, leader-only construction, sale rejection and design returns are
  world policies inferred from original 1.2 data and contemporary guides, not recovered world
  binary rules. Native wall slope/mirror variant selection remains unverified.
  Participant buffs grant the original headquarters teleport skills. Headquarters bind to
  retained original doodads for the current siege and require explicit GM rebind after restart;
  registered dead participants can revive there. The SiegePeriod doodad predicate fails closed
  because its transition semantics are unresolved; phase cycles are bounded for all groups.
  Historical automatic auctions, reinforcement/commander eligibility and payout calendars are
  not inferred. Building HP persists through restart and lodestone collisions follow the native
  CGA/ANM root curves and settled static damage proxies. Absolute render/collider animation
  phase synchronization remains unverified. Property, authority, mail, items and wallets use
  one short prepared snapshot before SQL awaits; logout and shutdown serialize the same saves.
* systems::housing: sale buyer names retain the original case-sensitive lookup. The housing
  audit fixes the first-house tax/deposit omission, reversed paid flag (confirmed against the
  1.2 housing UI bytecode), overdue counter, finished-house construction, duplicate signs,
  stale building counters, and duplicate deletion. Construction and sale resources/mail
  returns are preflighted together; failed returns preserve houses, furniture and coffers.
  Recovery honors house access and the recovery toggle, including stackable furniture.
  Housing, money, mail and inventory saves acknowledge snapshots only after commit; later
  mutations and failed transactions retain pending changes.
  Player crops and dropped backpacks convert their world position/rotation into house-local
  coordinates before attachment and persistence, preserving placement when houses rotate or reload.
  Renewable new houses begin with one prepaid `DaysForTaxPayment` period plus the existing
  seven-day grace interval; subsequent payments still add one configured period. This is an
  inferred correction from 1.2 `ui_texts` 4718 (`build_taxPaymentDesc`: construction pays the
  first week's taxes) and `TaxDueDate = ProtectionEndDate - 7 days`, rather than recovered
  world-server code. The C# starts protection at seven days, immediately marks that prepaid
  period unpaid and removes it at its first week's end. Nonrenewable beginner plots keep
  their authored seven-day lifetime. Exact original bill dispatch/late-fee timing is unverified.
    Dominion property tax is now collected only against current retained live claimed-tower
    authority in the property's zone and instance. Heavy-tax-exempt properties are also
    territory-tax exempt (original `ui_texts` 5896/5897). The configurable world rule
    `Housing.DominionPropertyTaxBase` defaults to `AfterHeavy`; `Base` applies the territory
    percentage only to the template tax. The available 1.2 client proves its displayed
    base-rate calculation but does not recover the world server's heavy-tax ordering or gold
    distribution. This is an explicit server policy: floor the nominal copper surcharge,
    exclude the refundable two-base-tax deposit, and credit only the collected nominal
    surcharge as property gold. The existing certificate ceil charging rule is preserved;
    bill/quote totals are quantized to whole 10,000-copper certificates so the original native
    nearest-half-up formatter (content_configs61 and verified runtime) displays the actual cost.
    Construction/payment and ledger preflight share authority/economy locking; failed or stale
    payments cannot credit revenue. A current owning guild leader may collect with
    `/collectdominion <zoneGroupId>` (ordinary-player access, current guild leader only),
    receiving the original eleven-argument balance-receipt
    mail. Repeated empty collection creates no mail, failed staged delivery retains revenue,
    and Peace Fund remains untouched. Hunting tax was removed by Trion's 1.2 notes; AA Points,
    Peace Fund percentages, nation splits and historical automatic payout scheduling are not
    invented. Original one-hour territory tax change cooldown uses wire error488/UI3544.
  Floors/walls/roofs use every normal-state prefab element and are synchronized when
  construction, residency or demolition changes. Model-specific placement slope/height/footprint
  checks and the developer-only
  category-8 `max_construct_count` rule are not inferred from later versions.
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
  dead. The former `UnitMoveType` driver-detachment bug is fixed (see vehicle test fixes above).
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
* systems::ai physics: the heightmap collider's bounds use the sample count as metres, so only the first half of each world axis has terrain contacts; the dry-land "roll correction torque" is added as a force and the beached "anti-gravity" force is not mass-scaled; ships are only simulated while `Transform.WorldId` equals the instance id; the buoyancy sample points are computed but unused. The former `SendUpdatedMovementData` yaw-in-pitch transform bug is fixed (see the vehicle test fixes above).

## Coverage audit (phase 5)

### Feature audit against 1.2 data (2026-10-11)

Registration and C# port coverage are separate from feature correctness. Run
`python -I rust/tools/port_audit/features.py --compact-db <compact.sqlite3> --output <report.json>`
to inventory all 244 supported inbound CS/X2 packets, the compact tables, MySQL base tables,
production/test references and incomplete-code candidates. Optional `--reference <decompiled.c>`
links exact table literals to the local reference functions. The report contains schemas and
counts, not rows or secrets. Keep reports with client data outside git. Test the inventory with
`python -I rust/tools/port_audit/features_test.py`.

Confirmed gaps fixed in the world server (1.2 opcodes and packet layouts retained):

* `CSChangeSlaveNamePacket` now validates ownership and the configured name pattern, normalizes
  the name, broadcasts the existing `SCUnitNameChangedPacket` and saves the slave name.
  A two-player packet flow checks unauthorized/invalid requests and the saved snapshot.
* `skill_reqs`, `skill_req_skills` and `skill_req_skill_tags` restrict casts while the matching
  caster/target buff is active. Blacklists and whitelists support skill ids and tags. Native
  reference: 10.x `sub_393616E0` / `sub_39CEECD0`; failure is the existing 1.2 `SkillReqFail`
  (0x32) with the requirement id. Validation runs before mana, cooldown or start-skill buff
  removal. Tests cover real 1.2 bindings, target tags and the byte-exact failure response.
* `item_accept_quests` supplies the authoritative item-to-quest mapping rather than relying
  solely on `QuestActConAcceptItem` discovery. Item-cast acceptance checks `items.impl_id ==
  AcceptQuest` and preserves the ITEM acceptor. Native loader: 10.x `sub_39AF5840`. Loader and
  item acceptance tests cover the binding, normal quest progression and duplicate use.
* `manual_funcs` enables discrete dynamic buff modifiers. Native `sub_39C06B90` loads at most
  200 integers; `sub_39BF39F0` / `sub_39BF6160` selects by completed buff ticks, clamps to the
  last entry, and requires a positive duration/tick. The existing linear behavior is retained.
  Tests check tick boundaries and removal; real data includes the leg-injury and wing curves.
* `CSCannotStartTradePacket` sends the existing refusal response to a pending requester.
  Acceptance and refusal require a matching server-issued invitation, consumed on reply;
  invitations expire after 60 seconds. Tests check byte-exact refusal, forged/replayed replies
  and the normal exchange handshake. This also closes unsolicited trade acceptance in C#.
* `QuestActObjItemGroupGather` / `QuestActObjItemGroupUse` now count as objectives and subscribe
  to inventory/use events. The existing `quest_item_group_items` bindings define membership;
  gather objectives count current inventory, use objectives count matching uses. Cleanup/drop
  consumes only the required group quantity. This extends the adjacent single-item act model;
  it is not claimed as byte-for-byte native quest execution parity. Tests cover multiple item
  types, removal, unrelated items, duplicate member rows, use events and quest abandonment.
  Real 1.2 quests 5490 and 5489 are checked for assigned objective wire slots.
* Priest blessing purchases now use the existing 1.2 `priest_buffs` data, payment item task
  22 and errors 149/150, with live priest interaction/range, funds, duplicate and buff checks.
  The native descriptor is confirmed by 10.x `sub_39B07970`; the world-only purchase logic is
  not present in that reference. `CSRemoveBuffPacket` now checks the slave's summoner before
  allowing client cancellation, matching the existing owned-mate rule.
* House sale/cancellation is owner-only; construction and decoration items must match the
  loaded design bindings. Decoration uses the house permission checks, which now require
  actual family/guild membership before shared access. Battlefield entry requires the
  player to be in the invited instance and enrolls/subscribes once. Regression flows cover
  foreign sales, forged designs, shared permissions, premature entry and duplicate entry.
* `CSMoveUnitPacket` unit motion is limited to the authenticated character and owned mates.
  Vehicle and ship requests require the server's driver-seat record; ordinary passengers
  cannot drive. Client despawn requests require the slave's summoner. Tests reject forged
  movement/control/despawn while retaining normal motion and driver reattachment.
* Object-free quest reports no longer jump from arbitrary active quests straight to rewards.
  They use the existing validated early-completion path or evaluate an automatic/journal
  Ready step. NPC/doodad report acts still require their object. Packet flows cover rejected
  premature/omitted-NPC requests, journal completion, early-completion thresholds and replay.
* Cinema completion resends neighbouring world objects before raising quest events, matching
  the former 1.2 C# packet handler. A packet flow checks NPC state/points replication and the
  cinema objective's normal transition to Ready.

Additional world-side fixes from the same audit:

* Cash-shop checkout charges the effective discounted price, validates templates and checked
  cart totals before fulfillment, serializes stock updates, and uses guarded SQL credit and
  loyalty debits. Gold still uses the inventory transaction path. The formerly free AA Points
  branch now returns the existing failure (778): a distinct authoritative 1.2 AA Points wallet
  has not been established. `money2` is bank gold, not an AA Points balance. Unit flows and
  private-MySQL tests cover failed debits, limits and concurrent overdraft attempts. Debit,
  audit and commercial-mail delivery do not yet form one crash-atomic transaction/outbox.
* Temporary USER chat channels implement the existing join/leave packets and packed eight-byte
  channel identity. Membership belongs to the current player session, chat requires that
  membership, and disconnect releases empty channels and ids. Emulator policy is server-global
  exact names/passwords, USER subtype zero and allocated chat ids; persistent ownership or
  moderation is not inferred from the zone reference. Budgets are 48 UTF-8 bytes for a name,
  64 for a password, eight joined channels per player and 1,024 channels per server. These are
  emulator limits, not a claim about retail 1.2 limits. Packet flows check the exact wire bytes,
  authentication, passwords, stale sessions, malformed/oversized input and capacity cleanup.
  The real 1.2 client creates a password-protected channel and sends through its normal
  `X2Chat` bindings. Explicit native leave parameters remain unverified; disconnect cleanup
  is covered separately.
* Mate pool and regeneration formulas now receive the complete attribute parameter set plus
  mate kind. Real 1.2 level-five horse formulas produce 1,012 maximum HP and 335 maximum MP;
  the old incomplete parameter map failed and produced zero maximum pools. Summoning clamps
  saved pools to the computed maximum and preserves injured, exhausted and dead saves. Mate
  skill requests require ownership or an actual server-managed passenger/parent relation
  before charging resources. The slave child-cannon versus parent-caster mapping remains
  unverified and is not tightened by this mate-only rule.
  A fresh Gray Lilyut Horse scroll (8160) was also checked in the real client: full pools,
  mounting, dismounting and dismissal/resummoning passed. The native Run! action was not
  exercised because the captured pet button had no key binding and its Lua click handler
  only dismissed a tooltip; server cast permission flows are tested separately.
* Character persistence saves `reported_as_bot_count` as well as `bot_reported_count`; the
  former was read but omitted from REPLACE, resetting the reported character's counter on
  every save. A private-MySQL temporary-table test covers repeated saves and adjacent fields.
* Expedition ownership transfer now keeps `owner_name` consistent with the validated new
  owner. Transferring to oneself is a no-op, preserving the owner role instead of demoting
  it. Packet flows and a private-MySQL temporary-table test verify both roles and the saved
  owner identity.

* Safe Portal Mode now honors `CSUsePortalPacket.only_my_portal` against the portal creator's
  character id and sends the existing 609 error for a foreign portal. Shared mode preserves
  public access. The loaded-slave instance check also runs before setting the player's
  movement freeze flag, so rejecting a loaded vehicle leaves movement state unchanged.
  Registry packet flows cover both initial flag states, exact errors and a later valid use.

* Sport-fishing stands now pay the existing 1.2 `items.refund` once and consume the validated
  equipped fish in the same inventory operation. Trophy conversion selects the pack for the
  actual conversion function and preserves the fish when mapping, reward or bag placement
  fails. Successful conversion keeps the stand. A retail weight-dependent sale formula has
  not been established; the existing static refund remains the emulator policy.
  The five rod reactions with `target_my_npc` require an NPC owned by the caster's character
  id before cast, resource, buff or cooldown mutations. Character and object ids are distinct.
  Flow tests cover wrong/correct species chum, active-school expiry replacement, fish sales,
  trophies, replay and failure paths; a real-compact test checks the Bamboo rod's plot 821,
  Yellow Lure and 100-labor consumption. The real client equips the rod and exposes all seven
  mode actions. Native stand confirmation casts also consume one Gargantuan Blue Marlin pack
  for trophy 28004 or pay exactly 400,000 copper (the client shows 40 gold); a later empty-back
  sale attempt leaves the balance unchanged. The stand remains available after conversion.
  Equipment acquisition now rejects multiple packs before any item creation or mutation,
  preserves explicit occupied slots and permits NPC fish pickup into an empty backpack slot
  even with a full bag. Occupied packs and full-bag glider replacement preserve the loot for
  retry. A complete native catch/reaction/loot cycle is still under investigation.
  Native chumming has also activated a Carp school (17243 → 17245), supplied buff 5767 and
  enabled a normal sport cast that consumes one Yellow Lure. Native fish spawning and reaction
  casts are observed; complete capture and corpse pickup remain under investigation.
  NPC fixed-position/self-target Leap controllers now use the reference velocity/acceleration
  math and vertical movement packets, with a kinematic 3D collision sweep. Moving unit targets,
  parented actors and unsupported landing rotation retain the prior linear path; native living
  actor contact prediction is unavailable. Underwater actor models move in 3D without projecting
  the fish onto the seabed. Direct character damage to owned FISH NPCs requires the character
  owner and the loaded fishing exception tag; this conservative FISH-specific rule does not
  infer generic creator/skill-tag exception semantics. Buff caster identity remains the fish.
* Sport-fish movement now uses its authored skill controllers exclusively. Between controllers,
  FISH NPCs stop without changing position or heading. The former experimental C# override
  added continuous movement toward global 10,000-metre offsets (including horizontal drift
  for Dive), beyond the 1.2 Carp's bounded relative endpoints. Regular and real-data tests
  cover cue pauses, controller ownership, subsequent heading and ordinary NPC movement.
* Ship replication preserves the physical quaternion instead of rebuilding it through the
  legacy Euler conversion, including mixed yaw/tilt and positive-W packet reconstruction.
  Solid hull and bottom boxes use transformed authored collision-proxy bounds independently
  of the inertia-only mass box. Native entity bounds can include render and non-brush objects
  unavailable to this loader, so this is a documented proxy adaptation. Explicit buoyancy
  settings remain authoritative; missing model assets retain the prior geometry fallback.

Remaining packet stubs and table candidates are still subject to behavior review. Missing SQL
mentions do not establish disuse (some names are selected dynamically); empty tables, client
visuals and schema migration metadata do not imply missing server features. The 10.x reference
may contain modern-only fields or behavior and is not a source of 1.2 wire layouts.

`rust/PORT-AUDIT.md` maps every C# file of `AAEmu.Commons`, `AAEmu.Login` and `AAEmu.Game`
(GM commands and unit tests excluded) to its Rust counterpart with a status (ported / merged /
dropped with the reason); regenerate it with `python rust/tools/port_audit/gen.py
rust/PORT-AUDIT.md` (verdicts of hand-checked types in `rust/tools/port_audit/verdicts_*.txt`).
`packets::tests::registry_matches_csharp_game_network` keeps the C->S registrations equal to
the C# `GameNetwork`. Deviations of the audit ports:

* Sticky parents are an `ObjectCore` marker (not part of `Transform`, so transform copies such as
  `CloneDetached` never carry it); setting one also makes it the real parent with the world
  position kept, as `SetStickyParent` does.
* The zone conflict state lives in `systems::social::conflicts` (copied from the loaded data at
  start); `NextStateTime` is kept in milliseconds for the timers and sent in seconds.
* `ZoneStateChangeTask` is a tokio task holding a weak server reference.
* `SusManager.finalize_check` keeps the `_skipCheckTime` / `_lastFinalizeTime` of an object by obj
  id; the analysis itself (database log) runs on a task.
* systems::services (commands): `CrimeCmd` keeps `CommandNames = ["doodad"]` (help / call prefix
  `/doodad`); `GoldCmd` hides `AddGold` and `gold set` adds like `add`; `SetFaction` changes the
  caller's faction; `AddPortals` uses the caller's position and roll; `Heal` without a target does
  nothing; the `Fly` cache toggles off when set to true twice; `TestChatChannel list` never
  resets its name buffer; `ItemUnwrap` passes the Secure flag twice; `QuestCmd
  objective|progress` index missing arguments (IndexOutOfRange); `QuestCmd template` /
  `SphereCommandUtil.GetQuestSphere` read the never-set `DetailType`; `GetSphereList` filters on
  the quest object id; `CrimeCreate` stores degrees as radians and throws without `source=`;
  `TestHouse setforsale` does not normalise the buyer name; `Kick` throws on a non-byte reason;
  `SendPacket` throws on a one-byte payload; `Scripts shutdown now` prints `-0` minutes.


### Quest and mail audit follow-up (October 13, 2026)

The quest/mail audit deliberately corrects C# failures rather than preserving
them. Quest Unix deadlines round-trip, timers are reconstructed from earlier
components on reload, and expired quests become Failed during initialization.
Aggro credit uses the killed NPC; CheckSphere uses local presence and template-ID
cleanup. Completion consumes supplies once. Ability/prerequisite-completion
events, zone filters, guarded NPC death, authored NPC actions, acceptor checks,
ID cleanup and sphere trigger/flag handling are wired and covered by regressions.

Quest overflow reward mail is stored before completion under the economy
operation boundary. Recipient Mail escrow is persistent and legacy ownership is
repaired on load. Player mail validates currency/slots/bind state and rejects
early access; returns keep original IDs and do not charge postage again. Ordinary
item packets cannot withdraw or destroy Mail/Auction escrow. Cancellation returns
the actual listed item and bids reserve only affordable funds.

Auction snapshots join mail/items/wallets in the prepared MySQL save transaction;
commit acknowledgement preserves newer dirty rows/deletions. The wiki audits
record targeted tests, MySQL rollback/reload and the real 1.2 client evidence,
including GM fixture setup and remaining authored content/review limitations.
See Docs/wiki/Quest-Audit.md and Docs/wiki/Mail-Audit.md.
