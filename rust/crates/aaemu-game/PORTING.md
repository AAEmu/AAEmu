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
| `Core/Managers/AccountManager` | `managers/account.rs`, `db.rs` | partial | Add/Remove/Contains/Count, GetAccountDetails, UpdateLoginTime. Credits/labor/loyalty updates, dead-connection sweep todo. |
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
| `Core/Managers/World/FactionManager`, `LocalizationManager` | `data/world_data.rs` | partial | system factions (+ localized names), relations, SendFactions / SendRelations. Relation checks todo. |
| `Core/Managers/ExpeditionManager`, `Models/Game/Expeditions` | `managers/expedition.rs` | partial | Load, GetExpedition, SendExpeditions, SendExpeditionInfo, member refresh / login / logout status packets. Guild management todo. |
| `Core/Managers/FriendMananger` | `managers/friend.rs`, `db_character.rs` | partial | Load, SendStatusChange, GetFriendInfo, FormatFriend. Add/remove todo. |
| `Core/Managers/PortalManager` | `data/world_data.rs` | partial | recalls.json + district_return_points (GetRecallBySubZoneId, GetDistrictReturnPoint). Teleports, world gates, respawns todo. |
| `Core/Managers/ChatManager`, `Models/Game/Chat` | `managers/chat.rs`, `world/session.rs` | partial | Faction / nation / zone / guild / family channels, join/leave/send; CSSendChatMessage for every chat type (party/raid -> "not in party/raid" errors as there are no teams). Trial channels, commands todo. |
| `Core/Managers/TimeManager` (+ `TimeOfDayObserver`) | `managers/time.rs` | done | Storm-ship time override todo. |
| `Core/Managers/SaveManager` | `managers/save.rs` | partial | Periodic + final save of items and online characters; the subsystems' rows through `systems::save_stage` (`BeforeItems`: houses, mails; `AfterItems`: auction, crimes; `AfterCharacters`: slaves). |
| `Core/Managers/World/StreamManager` | `managers/stream.rs`, `stream/cells.rs` | done | AddToken/RemoveToken/Login (wrong account removes the token, as C#), RequestCell/ContinueCell incl. the C# `next - 1` copy off-by-one. |
| `Core/Managers/Stream/UccManager`, `Models/Stream`, `Core/Managers/Id/UccIdManager` | `stream/ucc.rs`, `stream/models.rs` | partial | Load/Save (`uccs` table, data in the MEDIUMBLOB as the default C# build), upload (simple + custom), download, checks, id allocation. `CreateStamp`/`ApplyStamp` (item-side, used by skill effects) todo. ConfirmDefaultUcc saves before granting the Crest Ink (C# creates the item first). |
| `Core/Managers/TimedRewardsManager` | – | todo | Daily login + offline labor on enter (called from AccountManager.Add). |
| `Core/Managers/HousingManager`, `Models/Game/Housing` | – | todo | Needed for `Connection.Houses` / `SCLoginCharInfoHouse` / `SCMyHouse`. |
| `Core/Managers/CrimeManager` | `db_character.rs` | partial | CheckWantedThreshold thresholds only. |
| Other `Core/Managers/*` (AI, AccessLevel, Animation, Auction, CashShop, Command, Craft, Duel, EffectTask, ExpressText, Family, FishSchool, Gimmick, Indun, InstantGame, Mail, ManaRegen, Mate, Model, Music, Plot, PublicFarm, Quest, Radar, Shipyard, Slave, SubZone, Sus, Task/Tick, Taxations, Team, Trade, Trial; World: Spawn, AreaTrigger, Physics, Specialty, SphereQuest) | – | todo | |
| `Models/Game/Units/UnitCustomModelParams` | `models/unit_model.rs` | done | |
| `Models/Game/Units/Movements` (MoveType hierarchy) + `MathUtil` direction helpers | `models/movement.rs` | done | Default / Unit / Vehicle / Ship / ShipRequest / Transfer, byte-exact read/write (all actor-flag combinations tested). |
| `Models/AccountPayment`, `Models/Account` | `net/connection.rs`, `db.rs` | done | |
| `Models/AppConfiguration`, `Models/Game/Configurations` + `Program` config loading | `config.rs` | partial | Config.json + Configurations/** + Config.Local.json merge; sections used so far (incl. World, ClientData, Account delete timings). No env/CLI/user-secrets. |
| Other `Models/Game/*` (AI, Achievement, Auction, Crafts, Crime, DoodadObj, Mails, Mate, NPChar, Quests logic, Slaves, Team, ...) | – | todo | |
| `Scripts/Commands`, `Utils/Scripts` | – | todo | Chat commands behave like the C# with no commands loaded ("No commands available" + the text is sent as chat). |
| `Services` (WebApi, DiscordBot) | – | todo | |
| `GameService` startup / `ManagerOrchestrator` | `main.rs`, `systems/mod.rs` | partial | MySQL check, game data (fatal on failure), id managers, names, expeditions, friends, user items, StreamWorld, time, save manager, deletion task, `systems::start_all`, login link, game + stream listeners, final save. |

## Phase 3 subsystems

Each subsystem records its status here, in its own section only (see `CONTRIBUTING-PORT.md` §3).

### systems::skills

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| SkillManager, effects, plots, buff behaviour, combat, cooldowns | `systems/skills/` | todo | Foundation: `models::unit`, `models::buff` (`BuffEvent`), `systems::UnitHooks`. |

### systems::npcs

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| NpcManager, DoodadManager, SpawnManager, GimmickManager, AI | `systems/npcs/` | todo | Foundation: `models::{npc,doodad,gimmick}`, `world::object`, `WorldInstance::spawn`. |

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
| Team, expedition management, family, friends add/remove, duel, crime / trial | `systems/social/` | todo | |

### systems::quests

| Area (C#) | Rust | Status | Notes |
|---|---|---|---|
| QuestManager, quest templates / acts / components | `systems/quests/` | todo | |

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
* Quests: only the rows are loaded / saved / written; the quest template existence check,
  `QuestInitialized` / `RequestEvaluation` and the daily reset at login need the QuestManager.
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

## C# bugs noticed (mirrored, not fixed)

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
