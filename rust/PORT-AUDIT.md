# C# → Rust port coverage audit

Scope: every `.cs` file of `AAEmu.Commons`, `AAEmu.Login` and `AAEmu.Game` (2 498 files; the GM
commands under `AAEmu.Game/Scripts/Commands` and `Scripts/SubCommands` and the C# unit tests in
`AAEmu.UnitTests` are ported by other work streams and excluded here). Reference: the C# in this
worktree (1.2 client, `client_12_r208022`).

## How the audit was built

1. **Type scan.** Every public / internal type of every file was looked up in the Rust sources
   (`rust/crates/**/*.rs`): a Rust definition with the same name or its snake_case form
   (`struct` / `enum` / `trait` / `fn` / `mod` / `type`, packet macros) counts as **ported**; a type
   only referenced by name in the Rust code (every Rust port cites its C# member in doc comments)
   counts as **merged** into the file that cites it most; types mentioned only in "not ported"
   lines count as MISSING.
2. **Manual verification.** Every type the scan could not place (146 files with no match, 88
   further types inside partly matched files, 10 types only mentioned as "not ported") and the
   367 public methods with no Rust counterpart by name were checked by hand against the C#
   callers: each got a verdict (ported / merged / dropped with a reason / missing). Those
   verdicts are the notes in the tables below.
3. **Packets.** `packets::tests::registry_matches_csharp_game_network` parses the C#
   `GameNetwork` constructor and `CSOffsets` and asserts that every `RegisterPacket` (level 1
   and level-2 proxy) is registered in Rust under the same `(level, opcode)` with the same class
   name, and that Rust registers nothing else (the dead `0xfff` `CSReturnMailPacket` excepted).
   Stream (`CT`) registrations are checked by `stream::tests::registered_opcodes_match_ct_offsets`.
   Every S->C packet class of `G2C`, `S2C`, `L2G`, `Proxy`, and the login `L2C` / `L2G` classes
   was checked for a Rust packet type.
4. **"Not ported" notes.** Every `not ported` / `TODO` / `not modelled` comment left in the Rust
   sources was re-checked against the C#; real gaps were ported, stale notes rewritten.

Status values: **ported** (same type / function in Rust), **merged** (behaviour lives inside
another Rust type or function, e.g. event args become hook parameters, enums become constants,
handler classes become match arms), **dropped** (not needed at runtime; the reason is given:
unused in C#, interface / DI / reflection scaffolding, debug-only code hard-wired off,
editor/export tooling), **MISSING**.

## Summary

| | before this audit | after |
|---|---|---|
| files ported (every type has a Rust definition, or a verified verdict) | 1 578 | 1 601 |
| files merged into other Rust code (verified) | 773 (unverified mentions) | 809 |
| intentionally dropped (with reason) | – | 88 |
| files with a missing type (scan) | 146 + 88 types in partly matched files | 0 |
| C# `GameNetwork` registrations missing in Rust | 4 (`CSEditCharacter`, `CSTurretState`, `CSResurrectCharacter`, `CSHang`) | 0 |
| S->C packet classes without a Rust packet | 25 | 0 |
| public methods without a Rust counterpart by name | 367 | 336, every one with a verdict (see [Method-level notes](#method-level-notes)) |

Final generator counts: dropped: 88, merged: 809, ported: 1601.

## What this audit ported

Behaviour found missing and ported (byte-exact packets, unit / flow tests for each):

| C# | Rust |
|---|---|
| 25 `Core/Packets/G2C` classes no code built (`SCAddActionPoint`, `SCCharacterBound`, `SCCharBrief`, `SCCharDetail`, `SCConflictZoneHonorPointSum`, `SCCvF/FvFCombatRelationship`, `SCHackGuardRetAddrsRequest`, `SCIdleKick`, `SCKicked` + `KickedReason`, `SCKnockBackUnit`, `SCMileageChanged`, `SCMineAmount`, `SCPlaytime`, `SCRankAlarm`, `SCRawPacket`, `SCScheduleItemUpdate` + `ScheduleItem`, `SCSearchList`, `SCUnderWater`, `SCUnitGmModeChanged`, `SCUnitOffline`, `SCUnitPvPPointsChanged`, `SCUnknownPacket`, `SCUnlockCurrencySlot`, `SCWorldMessage`, `SCWorldQueue`) | `packets/g2c_misc.rs` |
| `CSEditCharacterPacket`, `CSTurretStatePacket` (registered, log only) and the unregistered `0xfff` packets `CSResetQuestContext`, `CSAcceptCheatQuestContext`, `CSSearchList`, `CSChangeExpeditionSponsor`, `CSChangeSlaveTarget`, `CSSetTeamOfficer`, `CSUpdateNationalTaxRate` | `packets/c2g_misc.rs` |
| `Transform.StickyParent` (`SetStickyParent`), `CSHangPacket`, `Hang` interaction, `CSUnhangPacket` target + harpoon rope break, `CSMoveUnitPacket` sticky clear / `IsBoardedOnTransfer`, `ClearSlaveAttachmentState`, `VehicleSeat` sticky handling | `world/object.rs` (`ObjectCore::sticky_parent`), `world/mod.rs` (`set_sticky_parent`), `systems/vehicles/harpoon.rs`, `systems/vehicles/packets.rs`, `world/session.rs` |
| `CSResurrectCharacterPacket` (priestess / War-zone faction return points / closest return portal, HP / MP, `ApplyRevivalDebuffs`, underwater reset, breath) | `systems/skills/resurrect.rs` |
| `CharacterCombat`: `ComputeDeathWaitTime` (escalating respawn timer), `AwardPvpHonor`, `CollectAssists`, `RecordPvpDamageFrom` / `RecordPvpHealFrom`, `HostileFactionKills`, `DiedInPvp` / `DiedInPvpWarZone`, War-zone honor loss, `ClearAssaultList`, `DropTradePackToFloor`; `ResurrectHp/MpPercent` default 1 | `systems/skills/combat.rs`, `systems/skills/state.rs`, `systems/skills/effects/combat.rs` (heal history), `systems/economy/item_effects.rs` (trade pack drop) |
| `ZoneConflict` runtime (`AddZoneKill`, `SetState`, `ForceNextState`, `CheckTimer`, `SetTimerTask`, `SendSwitchZoneState`) + `ZoneStateChangeTask`; return point / tower columns of `conflict_zones` | `systems/social/zone_conflict.rs`, `data/world_data.rs` |
| `Character.IsUnderWater` setter (`SCUnderWaterPacket`, breath refill) and the underwater part of `Character.SetPosition` (water body probe, deck probe of an attached ship) / `Unit.SetPosition` | `systems/skills/water.rs` |
| `Character.SetPosition` → `TeamManager.UpdatePosition` (party position on move) | `systems/skills/mod.rs::on_moved` |
| `Unit.UpdateGearBonuses` for mates, slaves, NPCs and portals (gear buffs) | `systems/skills/stats.rs` (+ `systems/vehicles`, `systems/npcs/portal`) |
| `UccManager.ConfirmDefaultUcc` money / Crest Ink item tasks in C# order | `stream/ucc.rs` |
| `SpawnFishEffect`, treasure-map dig-up (`GetTreasureMapWithCoordinatesNearbyItem`, `TreasureMap.GetMapPosition`, skill id 18349), storm time-of-day override (`ResolveClientTimeOfDayHours`) | `systems/npcs/bridges.rs`, `systems/skills/unit_reqs.rs`, `managers/time.rs` |
| `Npc.Delete` (AI detached at every NPC delete) | `systems/npcs/npc/manager.rs::delete` |
| `GetAround(…, useModelSize)` in area triggers and `DoodadFuncBuff`; `GetDistanceTo` in `JoinTrialAudience` | `systems/npcs/area_trigger.rs`, `systems/npcs/doodad/funcs/skills.rs`, `systems/social/trial.rs` |
| `SpawnPersistentDoodads` coffer `ItemContainer` attach / `InitializeCoffer` + save, slave `AttachedDoodads` | `systems/npcs/spawn_manager.rs` |
| Quest unit requirement `CheckBuffTag`; labor margin `GetMaxLabor(PremiumState)` (skills used 2000, quests 5000) | `systems/quests/engine.rs`, `systems/skills/unit_reqs.rs` |
| `Character.SubZoneId` (`CSNotifySubZonePacket`) | `models/character.rs`, `systems/npcs/subzone.rs` |
| `Transform.FinalizeTransform` → `SusManager.AnalyzePlayerDeltaMovement` / `AnalyzeMountDeltaMovement` every 5 s | `systems/services/sus.rs` |
| `PublicFarmTick` doodad `Save()` | `systems/npcs/public_farm.rs` |

Also merged into `rust-rewrite` during the audit (ported by other work streams, accounted for in
the tables): `PhysicalExplosionEffect.Apply` / `ComputeKnockDisplacement`
(`systems/skills/effects/unit_fx.rs`), the `AchievementGameData` loader
(`systems/social/achievement.rs`), `WorldManager` lookups (`worlds_by_template`,
`all_*_from_world`, `target_or_self`), id manager fixes and the C# unit-test ports.

## Still missing

Nothing in scope. Items that remain intentionally outside it:

* The GM command scripts (`Scripts/Commands`, `Scripts/SubCommands`) and the helpers only they
  use (`QuestCommandUtil`, `SphereCommandUtil`, `JsonSlaveSpawns`, `WorldInstance.ShipBarrierScriptApi`,
  `WaterDebugSnapshot` / `AutoWaterProbe*`, `ScriptCompiler`): ported by the GM-commands work
  stream.
* Debug instrumentation hard-wired off in C# (`ShipTuningDebug`, `HarpoonMechanicsDebug`,
  `SkillControllerPacketDebug`, the packet `Verbose()` overrides).
* Dead C# code (no caller, or only callers inside comments): listed per file below.

## Packet registration check

* Game C->S (level 1 + level-2 proxy): all 266 uncommented `RegisterPacket` calls of
  `GameNetwork` are registered with the same class (`registry_matches_csharp_game_network`).
* Game S->C: every class in `Core/Packets/G2C`, `Core/Packets/Proxy`, `Core/Packets/S2C` has a Rust
  packet (`ExtraDataFlags` merged into `SCSkillStartedPacket`, `SCLoginCharInfoHouse` is
  `SCLoginCharInfoHousePacket`, `SCChangeJuryOKCountPacket` / `SCUnitMovementsPacket` /
  `SCUserNotesLoadedPacket` are built by `systems/social/packets/trial.rs`,
  `models/movement.rs`, `systems/services/music.rs`).
* Stream: `CT` registrations = `CTOffsets` (`stream::tests::registered_opcodes_match_ct_offsets`);
  every `TC` class has a Rust packet in `stream/s2c.rs`.
* Login link (`LG` / `GL`) and the login server (`CL` / `LC` / `GL` / `LG`): opcode values match
  the C# offsets (`aaemu-login/src/packets/*.rs`, `aaemu-game/src/login/packets.rs`); the C2L / G2L
  handler classes are match arms of `aaemu-login/src/handlers.rs`.

## Runtime data files

Every runtime file the C# reads is read by the Rust servers from the same path:

| C# path | Rust reader |
|---|---|
| `Config.json`, `Configurations/**/*.json` (AccessLevels, CharacterSettings, ClientData, Dungeons, Expedition, Justice, Specialty, World), `Config.Local.json` | `aaemu-game/src/config.rs` (same load order) |
| `AAEmu.Login/Config.json`, `Config.Local.json` | `aaemu-login/src/config.rs` |
| `Data/compact.sqlite3` | `aaemu-game/src/data/sqlite.rs` (+ every `Data::load`) |
| `Data/CharTemplates.json` | `data/character_templates.rs` |
| `Data/Creatures.xml`, `Data/Doodads.xml` | `aaemu-commons/src/utils/creature.rs` |
| `Data/Path/*.path` | `systems/ai/path_handler.rs` (`AiPathsManager`) |
| `Data/Portal/recalls.json`, `respawns.json`, `worldgates.json` | `data/world_data.rs`, `systems/npcs/portal/data.rs` |
| `Data/battlefields.json` | `systems/instances/data.rs` |
| `Data/housing_bindings.json` | `systems/housing/data.rs` |
| `Data/slave_attach_points.json` | `systems/vehicles/data.rs` |
| `Data/Worlds/*/{npc,doodad,gimmick,slave,transfer}_spawns.json`, `world_spawns.json` | `systems/npcs/spawn_manager.rs`, `systems/npcs/gimmick`, `data/world_templates.rs` |
| client files (`ClientData`, `game_pak`): `game/worlds/*/world.xml`, `heightmap.dat`, `object.dat`, `*.bai`, `subzone_area.xml`, `housing_area.xml`, `transfer_path.xml`, `quest_sign_sphere.g`, `combat_sync_event_list.g` | `aaemu-commons/src/client_files.rs`, `data/world_templates.rs`, `systems/npcs/heightmap.rs`, `systems/ai/physics/objects_file.rs`, `systems/ai/geodata`, `systems/npcs/subzone.rs`, `systems/vehicles/data.rs`, `systems/quests/spheres.rs`, `systems/skills/data/misc.rs` |
| `NLog.config` | replaced by `RUST_LOG` / `tracing` (`systems/services/game_service.rs`) |
| `Resources/icon_*` (Windows tray icons), `AAEmu.Login/Resources/*` (README images) | not runtime data (dropped) |
| `Scripts/Commands/kits.json`, `slave_spawns_new.json` | GM commands only (GM-commands work stream) |

## Method-level notes

Public methods with no Rust counterpart by name, and their verdicts:

* **Merged** – `Buffs.CancelAllEffectTasks` / `GetBuffsRequiring` (dispel-task generations,
  inline in `dispel`), `BuffModifiers` / `SkillModifiers` getters (`models/unit.rs`
  `ModifierCache::apply`), `PlotTargetInfo.UpdateSource/UpdateTargets`,
  `PlotEventTemplate.HasSpecialEffects` (`systems/skills/plots.rs`), `KnockBack.ApplyKnockbackToNpc`
  (`movement::move_npc`), the `QuestAct*.On*` handlers (`systems/quests/acts.rs` + `events.rs`),
  `QuestComponent.Initialize/FinalizeComponent`, `Quest.SkipUpdatePackets`,
  `Behavior/NpcAi.SetDefaultBehavior`, `NpcGameData.GetNpSkills`, `ItemManager` stat / durability
  constants (`ItemConfig`), `ZoneManager.IsPirateDesperadoZone` (inline zone-group lookup),
  `GameScheduleManager.LoadGameSchedule*` (`ScheduleData::load`), `ManagerOrchestrator.Run*`
  (`main.rs` startup order), `SaveManager.SaveTick` (`managers/save.rs`), `Duel.SendPacket*`,
  `ChatChannel.TryRemoveIfEmpty`, `Character.SendOption` / `SetHostileActivity`,
  `Unit/ICharacter.Set*Rate` / `Set*Message` / `SetGeoDataMode` (config mutation of the world
  sub-commands), `UnitCustomModelParams` setters, `PositionAndRotation` / `Transform` /
  `WorldSpawnPosition` helpers (plain value types), `WorldCell.GetCellWorldOffset`,
  `WorldInstance.GetAllMates` / `InitShipStaticBarriers`, `PhysicsManager` terrain helpers and the
  Jitter2 force helpers (`systems/ai/physics`), `GameConnection` / connection-table / session
  attribute helpers (`net/connection.rs`, `aaemu-login/src/network`), `PacketStream` readers /
  writers (`aaemu-commons/src/packet_stream.rs` names: `read_i32`, `read_f64`, ...),
  `Helpers.ConvertPosition` (`write_position`), `SQLiteWrapperReader` getters
  (`data/sqlite.rs`), `TimeOfDayObserver.OnNext` (`managers/time.rs`),
  `DiscordBotService.MessageReceived` (`systems/services/discord.rs`), the login
  `LoginClient` / `LoginSession` / `LoginConnection` send helpers and the `ACEnterOtp` /
  `ACEnterPcCert` / `ACJoinResponse` factories (`aaemu-login/src/network`, `packets/l2c.rs`).
* **Dropped (no caller in C#)** – `AggroExtensions.*`, `Aggro.RemoveAggro`, `NpcGroupGameData.*`,
  `TagsGameData.GetTagsByTargetId`, the six `UnitRequirementsGameData.Get*Requirements` getters,
  `PlotManager.GetEventByPlotId`, `SlaveManager.GetActiveSlavesByKind`,
  `FactionManager.AddFaction`, `SpecialtyManager.GetValueOfOne`, `WorldManager.GetWorldTemplateForZoneGroup`,
  `WorldInstance.GetAllGimmicks` / `SetNpc`, `Region.AreNeighborsEmpty` / `GetObjectIdsList` /
  `GetObjectsList`, `PositionAndRotation.LookAt` / `SubDistance`, `WaterBodyArea.GetCenter`,
  `Dungeon.GetDungeonWorldId`, `ItemManager.LoadPlayerInventory`, `Patrol.*`,
  `UnitProcs.AddProc/RemoveProc` (`RollProcsForKind` never fires: `LastProc` starts at
  `DateTime.MinValue`), `Buffs.GetEffectByTemplate` / `GetEffectsByType`,
  `BaseUnit.ApplyBuffModifers`, `Task.CancelAsync`, `TimeOfDayObserver.OnCompleted`,
  `PhysicsManager.GetRollAngle`, the CryEngine `CheckVersion` / export helpers of readers no
  server code calls, `EndianBitConverter` helpers.
* **Debug / tooling only** – `DebugName`, `ToFullString`, `ToggleDebugTracker`,
  `SkillTlIdManager.ReportStatus`, `SkillControllerPacketDebug.*`, `HarpoonMechanicsDebug.*`,
  `ShipTuningDebug.*`, `WaterDebugSnapshot.*`, `AutoWaterProbe*`, `ReloadWaterFromLoadedCells`,
  `ClearIngestedAreas`, the `ShipBarrierScriptApi` helpers, `ScriptCompiler.*`,
  `QuestCommandUtil` / `SphereCommandUtil` (GM commands).
* **DI / reflection scaffolding** – `ServiceCollectionExtensions.Add*`, `GameDataManager.LoadGameData`
  / `PostLoadGameData` (explicit loads in `data/mod.rs`), `JsonModelsConverter` (serde),
  `XML.Serialization` (roxmltree readers).

## Per-file status


### `AAEmu.Commons/Conversion`

| File | Types | Status | Rust |
|---|---|---|---|
| `BigEndianBitConverter.cs` | BigEndianBitConverter | merged | `BigEndianBitConverter` merged: aaemu-commons (u16/u32::from_le_bytes / from_be_bytes in packet_stream.rs); the C# converters are only referenced by EndianBitConverter |
| `EndianBitConverter.cs` | EndianBitConverter | merged | `EndianBitConverter` merged into `aaemu-commons/src/lib.rs` |
| `Endianness.cs` | Endianness | merged | `Endianness` merged: aaemu-commons (u16/u32::from_le_bytes / from_be_bytes in packet_stream.rs); the C# converters are only referenced by EndianBitConverter |
| `LittleEndianBitConverter.cs` | LittleEndianBitConverter | merged | `LittleEndianBitConverter` merged: aaemu-commons (u16/u32::from_le_bytes / from_be_bytes in packet_stream.rs); the C# converters are only referenced by EndianBitConverter |

### `AAEmu.Commons/Exceptions`

| File | Types | Status | Rust |
|---|---|---|---|
| `GameException.cs` | GameException | merged | `GameException` merged into `aaemu-commons/src/error.rs` |
| `MarshalException.cs` | MarshalException | merged | `MarshalException` merged into `aaemu-commons/src/error.rs` |

### `AAEmu.Commons/IO`

| File | Types | Status | Rust |
|---|---|---|---|
| `FileManager.cs` | FileManager | ported | `FileManager` → `aaemu-commons/src/lib.rs` |

### `AAEmu.Commons/Models`

| File | Types | Status | Rust |
|---|---|---|---|
| `LoginCharacterInfo.cs` | LoginCharacterInfo | ported | `LoginCharacterInfo` → `aaemu-commons/src/models.rs` |
| `MySqlConnectionSettings.cs` | MySqlConnectionSettings | ported | `MySqlConnectionSettings` → `aaemu-login/src/config.rs` |

### `AAEmu.Commons/Network`

| File | Types | Status | Rust |
|---|---|---|---|
| `BaseProtocolHandler.cs` | BaseProtocolHandler | merged | `BaseProtocolHandler` merged: aaemu-commons/src/net + aaemu-game/src/net (FrameCodec, ServerPacket / ClientPacket traits) |
| `IBaseProtocolHandler.cs` | IBaseProtocolHandler | dropped | `IBaseProtocolHandler` dropped: interface only (DI / abstraction); implementations audited separately |
| `PacketBase.cs` | PacketBase | merged | `PacketBase` merged: aaemu-commons/src/net + aaemu-game/src/net (FrameCodec, ServerPacket / ClientPacket traits) |
| `PacketLogLevel.cs` | PacketLogLevel | merged | `PacketLogLevel` merged: tracing levels (packet logging verbosity) |
| `PacketMarshaler.cs` | PacketMarshaler | merged | `PacketMarshaler` merged into `aaemu-game/src/world/unit_state.rs` |
| `PacketStream.cs` | PacketStream | ported | `PacketStream` → `aaemu-commons/src/packet_stream.rs` |

### `AAEmu.Commons/Network/Core`

| File | Types | Status | Rust |
|---|---|---|---|
| `Client.cs` | Client | ported | `Client` → `aaemu-game/src/world_flow_tests.rs` |
| `Server.cs` | Server | ported | `Server` → `aaemu-game/src/systems/ai/flow_tests.rs` |
| `Session.cs` | ISession, Session | merged | `ISession` merged into `aaemu-login/src/network/login/tests.rs`; `Session` → `aaemu-game/src/systems/social/mod.rs` |

### `AAEmu.Commons/Utils`

| File | Types | Status | Rust |
|---|---|---|---|
| `BitSet.cs` | BitSet | ported | `BitSet` → `aaemu-commons/src/utils/bitset.rs` |
| `Helper.cs` | Helper | ported | `Helper` → `aaemu-commons/src/utils/mod.rs` |
| `Helpers.cs` | Helpers | ported | `Helpers` → `aaemu-game/src/systems/services/webapi/http.rs` |
| `JsonHelper.cs` | JsonHelper | ported | `JsonHelper` → `aaemu-commons/src/utils/mod.rs` |
| `PrimeFinder.cs` | PrimeFinder | ported | `PrimeFinder` → `aaemu-commons/src/utils/mod.rs` |
| `RandomElementByWeight.cs` | IEnumerableExtensions | merged | `IEnumerableExtensions` merged into `aaemu-commons/src/utils/random.rs` |
| `RandomExtensions.cs` | RandomExtensions | merged | `RandomExtensions` merged into `aaemu-commons/src/utils/random.rs` |
| `SequenceReaderExtensions.cs` | SequenceReaderExtensions | merged | `SequenceReaderExtensions` merged into `aaemu-commons/src/utils/mod.rs` |
| `Singleton.cs` | Singleton | merged | `Singleton` merged into `aaemu-commons/src/utils/mod.rs` |
| `SingletonContainer.cs` | SingletonContainer | merged | `SingletonContainer` merged into `aaemu-commons/src/utils/mod.rs` |
| `StringExtensions.cs` | StringExtensions | ported | `StringExtensions` → `aaemu-commons/src/utils/string_ext.rs` |
| `TimeSpanExtensions.cs` | TimeSpanExtensions | ported | `TimeSpanExtensions` → `aaemu-commons/src/utils/time_span.rs` |

### `AAEmu.Commons/Utils/AAPak`

| File | Types | Status | Rust |
|---|---|---|---|
| `AAPak.cs` | AAPakFileInfo, AAPakFileHeader, PakFileType, AAPak | merged | `AAPakFileInfo` merged into `aaemu-commons/src/aapak/mod.rs`; `AAPakFileHeader` merged into `aaemu-commons/src/aapak/mod.rs`; `PakFileType` → `aaemu-commons/src/aapak/mod.rs`; `AAPak` → `aaemu-commons/src/aapak/mod.rs` |
| `SubStream.cs` | SubStream | ported | `SubStream` → `aaemu-commons/src/aapak/sub_stream.rs` |

### `AAEmu.Commons/Utils/Creatures`

| File | Types | Status | Rust |
|---|---|---|---|
| `Creature.cs` | Creature | ported | `Creature` → `aaemu-commons/src/utils/creature.rs` |

### `AAEmu.Commons/Utils/DB`

| File | Types | Status | Rust |
|---|---|---|---|
| `MySQL.cs` | MySQL | merged | `MySQL` merged into `aaemu-login/tests/login_controller_mysql.rs` |

### `AAEmu.Commons/Utils/Updater`

| File | Types | Status | Rust |
|---|---|---|---|
| `MySqlDatabaseUpdater.cs` | MySqlDatabaseUpdater | merged | `MySqlDatabaseUpdater` merged into `aaemu-login/src/db.rs` |

### `AAEmu.Commons/Utils/XML`

| File | Types | Status | Rust |
|---|---|---|---|
| `Serialization.cs` | Serialization | merged | `Serialization` merged into `aaemu-commons/src/utils/mod.rs` |
| `XmlHelper.cs` | XmlHelper | ported | `XmlHelper` → `aaemu-commons/src/utils/mod.rs` |

### `AAEmu.Game`

| File | Types | Status | Rust |
|---|---|---|---|
| `GameService.cs` | GameService | ported | `GameService` → `aaemu-game/src/systems/services/game_service.rs` |
| `Program.cs` | Program | merged | `Program` merged into `aaemu-game/src/systems/services/game_service.rs` |

### `AAEmu.Game/Core/Managers`

| File | Types | Status | Rust |
|---|---|---|---|
| `AIManager.cs` | AIManager | merged | `AIManager` merged into `aaemu-game/src/systems/ai/manager.rs` |
| `AccessLevelManager.cs` | AccessLevelManager, Command | ported | `AccessLevelManager` → `aaemu-game/src/systems/services/commands/mod.rs`; `Command` → `aaemu-game/src/systems/services/commands/mod.rs` |
| `AccountManager.cs` | AccountManager | ported | `AccountManager` → `aaemu-game/src/managers/account.rs` |
| `AiGeodataManager.cs` | AiGeoDataManager | merged | `AiGeoDataManager` merged into `aaemu-game/src/systems/ai/geodata/manager.rs` |
| `AiPathsManager.cs` | AiPathsManager | ported | `AiPathsManager` → `aaemu-game/src/systems/ai/path_handler.rs` |
| `AnimationManager.cs` | AnimationManager | merged | `AnimationManager` merged into `aaemu-game/src/systems/skills/data/misc.rs` |
| `AuctionManager.cs` | AuctionManager | ported | `AuctionManager` → `aaemu-game/src/systems/economy/auction.rs` |
| `CashShopManager.cs` | CashShopManager | ported | `CashShopManager` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `ChatManager.cs` | ChatManager | ported | `ChatManager` → `aaemu-game/src/systems/social/chat.rs` |
| `CommandManager.cs` | CommandManager | ported | `CommandManager` → `aaemu-game/src/systems/services/commands/mod.rs` |
| `CraftManager.cs` | CraftManager | ported | `CraftManager` → `aaemu-game/src/systems/economy/craft.rs` |
| `CrimeManager.cs` | CrimeManager | ported | `CrimeManager` → `aaemu-game/src/systems/social/crime.rs` |
| `DuelManager.cs` | DuelManager | ported | `DuelManager` → `aaemu-game/src/systems/social/duel.rs` |
| `EffectTaskManager.cs` | EffectTaskManager | merged | `EffectTaskManager` merged into `aaemu-game/src/systems/skills/buffs.rs` |
| `ExpeditionManager.cs` | ExpeditionManager | ported | `ExpeditionManager` → `aaemu-game/src/systems/social/expedition.rs` |
| `ExperienceManager.cs` | ExperienceManager | merged | `ExperienceManager` merged into `aaemu-game/src/data/experience.rs` |
| `ExpressTextManager .cs` | ExpressTextManager | merged | `ExpressTextManager` merged into `aaemu-game/src/systems/services/mod.rs` |
| `FamilyManager.cs` | FamilyManager | ported | `FamilyManager` → `aaemu-game/src/systems/social/family.rs` |
| `FeaturesManager.cs` | FeaturesManager | merged | `FeaturesManager` merged into `aaemu-game/src/systems/services/mod.rs` |
| `FishSchoolManager.cs` | FishSchoolManager | ported | `FishSchoolManager` → `aaemu-game/src/systems/npcs/fish_school.rs` |
| `FormulaManager.cs` | FormulaManager | ported | `FormulaManager` → `aaemu-game/src/data/formulas.rs` |
| `FriendMananger.cs` | FriendMananger, FriendTemplate | merged | `FriendMananger` merged into `aaemu-game/src/systems/social/friend.rs`; `FriendTemplate` merged into `aaemu-game/src/models/char_data.rs` |
| `GameScheduleManager.cs` | GameScheduleManager, PeriodStatus | merged | `GameScheduleManager` merged into `aaemu-game/src/systems/npcs/schedule.rs`; `PeriodStatus` → `aaemu-game/src/systems/npcs/schedule.rs` |
| `GimmickManager.cs` | GimmickManager | ported | `GimmickManager` → `aaemu-game/src/systems/npcs/gimmick/manager.rs` |
| `HousingManager.cs` | HousingManager | ported | `HousingManager` → `aaemu-game/src/systems/housing/manager.rs` |
| `IAIManager.cs` | IAIManager | merged | `IAIManager` merged into `aaemu-game/src/systems/ai/manager.rs` |
| `IAccessLevelManager.cs` | IAccessLevelManager | ported | `IAccessLevelManager` → `aaemu-game/src/systems/services/commands/mod.rs` |
| `IAccountManager.cs` | IAccountManager | ported | `IAccountManager` → `aaemu-game/src/managers/account.rs` |
| `IAiPathsManager.cs` | IAiPathsManager | ported | `IAiPathsManager` → `aaemu-game/src/systems/ai/path_handler.rs` |
| `IAnimationManager.cs` | IAnimationManager | merged | `IAnimationManager` merged into `aaemu-game/src/systems/skills/data/skill_manager_tests.rs` |
| `IAuctionManager.cs` | IAuctionManager | ported | `IAuctionManager` → `aaemu-game/src/systems/economy/auction.rs` |
| `ICashShopManager.cs` | ICashShopManager | ported | `ICashShopManager` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `IChatManager.cs` | IChatManager | ported | `IChatManager` → `aaemu-game/src/systems/social/chat.rs` |
| `ICommandManager.cs` | ICommandManager | ported | `ICommandManager` → `aaemu-game/src/systems/services/commands/mod.rs` |
| `ICraftManager.cs` | ICraftManager | ported | `ICraftManager` → `aaemu-game/src/systems/economy/craft.rs` |
| `ICrimeManager.cs` | ICrimeManager | ported | `ICrimeManager` → `aaemu-game/src/systems/social/crime.rs` |
| `IDuelManager.cs` | IDuelManager | ported | `IDuelManager` → `aaemu-game/src/systems/social/duel.rs` |
| `IEffectTaskManager.cs` | IEffectTaskManager | merged | `IEffectTaskManager` merged into `aaemu-game/src/systems/skills/buffs.rs` |
| `IExpeditionManager.cs` | IExpeditionManager | ported | `IExpeditionManager` → `aaemu-game/src/systems/social/expedition.rs` |
| `IExperienceLevelTemplateLoader.cs` | IExperienceLevelTemplateLoader | merged | `IExperienceLevelTemplateLoader` merged into `aaemu-game/src/data/experience_tests.rs` |
| `IExperienceManager.cs` | IExperienceManager | merged | `IExperienceManager` merged into `aaemu-game/src/data/experience.rs` |
| `IExpressTextManager.cs` | IExpressTextManager | merged | `IExpressTextManager` merged into `aaemu-game/src/systems/services/mod.rs` |
| `IFamilyManager.cs` | IFamilyManager | ported | `IFamilyManager` → `aaemu-game/src/systems/social/family.rs` |
| `IFeaturesManager.cs` | IFeaturesManager | merged | `IFeaturesManager` merged into `aaemu-game/src/systems/services/mod.rs` |
| `IFishSchoolManager.cs` | IFishSchoolManager | ported | `IFishSchoolManager` → `aaemu-game/src/systems/npcs/fish_school.rs` |
| `IFormulaManager.cs` | IFormulaManager | ported | `IFormulaManager` → `aaemu-game/src/data/formulas.rs` |
| `IFriendManager.cs` | IFriendManager | ported | `IFriendManager` → `aaemu-game/src/systems/social/friend.rs` |
| `IGameScheduleManager.cs` | IGameScheduleManager | merged | `IGameScheduleManager` merged into `aaemu-game/src/systems/npcs/schedule.rs` |
| `IHousingManager.cs` | IHousingManager | ported | `IHousingManager` → `aaemu-game/src/systems/housing/manager.rs` |
| `IIndunManager.cs` | IIndunManager | ported | `IIndunManager` → `aaemu-game/src/systems/instances/indun.rs` |
| `IInstantGameManager.cs` | IInstantGameManager | ported | `IInstantGameManager` → `aaemu-game/src/systems/instances/instant_game.rs` |
| `IItemManager.cs` | IItemManager | ported | `IItemManager` → `aaemu-game/src/managers/item.rs` |
| `ILoadable.cs` | ILoadable, IInitializable | merged | `ILoadable` merged into `aaemu-game/src/systems/services/game_service.rs`; `IInitializable` merged into `aaemu-game/src/systems/services/game_service.rs` |
| `ILocalizationManager.cs` | ILocalizationManager | ported | `ILocalizationManager` → `aaemu-game/src/systems/services/localization.rs` |
| `IMailManager.cs` | IMailManager | ported | `IMailManager` → `aaemu-game/src/systems/economy/mail.rs` |
| `IManaRegenManager.cs` | IManaRegenManager | merged | `IManaRegenManager` merged into `aaemu-game/src/systems/skills/regen.rs` |
| `IModelManager.cs` | IModelManager | ported | `IModelManager` → `aaemu-game/src/systems/services/models.rs` |
| `IMusicManager.cs` | IMusicManager | ported | `IMusicManager` → `aaemu-game/src/systems/services/music.rs` |
| `INameManager.cs` | INameManager | ported | `INameManager` → `aaemu-game/src/managers/name.rs` |
| `IPlotManager.cs` | IPlotManager | merged | `IPlotManager` merged into `aaemu-game/src/systems/skills/data/skill_manager_tests.rs` |
| `IPortalManager.cs` | IPortalManager | ported | `IPortalManager` → `aaemu-game/src/systems/npcs/portal/mod.rs` |
| `IPublicFarmManager.cs` | IPublicFarmManager | ported | `IPublicFarmManager` → `aaemu-game/src/systems/npcs/public_farm.rs` |
| `IQuestManager.cs` | IQuestManager | merged | `IQuestManager` merged into `aaemu-game/src/systems/npcs/hooks.rs` |
| `IRadarManager.cs` | IRadarManager | ported | `IRadarManager` → `aaemu-game/src/systems/services/radar.rs` |
| `ISaveManager.cs` | ISaveManager | ported | `ISaveManager` → `aaemu-game/src/managers/save.rs` |
| `IShipyardManager.cs` | IShipyardManager | ported | `IShipyardManager` → `aaemu-game/src/systems/vehicles/shipyards.rs` |
| `ISkillManager.cs` | ISkillManager | merged | `ISkillManager` merged into `aaemu-game/src/systems/skills/data/mod.rs` |
| `ISubZoneManager.cs` | ISubZoneManager | merged | `ISubZoneManager` merged into `aaemu-game/src/systems/npcs/subzone.rs` |
| `ISusManager.cs` | ISusManager | ported | `ISusManager` → `aaemu-game/src/systems/services/sus.rs` |
| `ITaskManager.cs` | ITaskManager | ported | `ITaskManager` → `aaemu-game/src/systems/services/tasks.rs` |
| `ITaxationsManager.cs` | ITaxationsManager | merged | `ITaxationsManager` merged into `aaemu-game/src/systems/housing/data.rs` |
| `ITeamManager.cs` | ITeamManager | ported | `ITeamManager` → `aaemu-game/src/systems/social/team.rs` |
| `ITickManager.cs` | ITickManager | ported | `ITickManager` → `aaemu-game/src/systems/services/tick.rs` |
| `ITimeManager.cs` | ITimeManager | ported | `ITimeManager` → `aaemu-game/src/managers/time.rs` |
| `ITimedRewardsManager.cs` | ITimedRewardsManager | merged | `ITimedRewardsManager` merged into `aaemu-game/src/systems/services/mod.rs` |
| `ITradeManager.cs` | ITradeManager | ported | `ITradeManager` → `aaemu-game/src/systems/economy/trade.rs` |
| `ITrialManager.cs` | ITrialManager | ported | `ITrialManager` → `aaemu-game/src/systems/social/trial.rs` |
| `IndunManager.cs` | IndunManager | ported | `IndunManager` → `aaemu-game/src/systems/instances/indun.rs` |
| `InstantGameManager.cs` | InstantGameManager | ported | `InstantGameManager` → `aaemu-game/src/systems/instances/instant_game.rs` |
| `ItemManager.cs` | ItemManager | ported | `ItemManager` → `aaemu-game/src/managers/item.rs` |
| `LocalizationManager.cs` | LocalizationManager | ported | `LocalizationManager` → `aaemu-game/src/systems/services/localization.rs` |
| `MailManager.cs` | MailManager | ported | `MailManager` → `aaemu-game/src/systems/economy/mail.rs` |
| `ManaRegenManager.cs` | ManaRegenManager | merged | `ManaRegenManager` merged into `aaemu-game/src/systems/skills/regen.rs` |
| `ManagerOrchestrator.cs` | ManagerOrchestrator | ported | `ManagerOrchestrator` → `aaemu-game/src/systems/services/game_service.rs` |
| `MateManager.cs` | MateManager | ported | `MateManager` → `aaemu-game/src/systems/vehicles/mates.rs` |
| `ModelManager.cs` | ModelManager | ported | `ModelManager` → `aaemu-game/src/systems/services/models.rs` |
| `MusicManager.cs` | MusicManager | ported | `MusicManager` → `aaemu-game/src/systems/services/music.rs` |
| `NameManager.cs` | NameManager | ported | `NameManager` → `aaemu-game/src/managers/name.rs` |
| `PlotManager.cs` | PlotManager | merged | `PlotManager` merged into `aaemu-game/src/systems/skills/data/mod.rs` |
| `PortalManager.cs` | PortalManager | ported | `PortalManager` → `aaemu-game/src/systems/npcs/portal/mod.rs` |
| `PublicFarmManager.cs` | PublicFarmManager | ported | `PublicFarmManager` → `aaemu-game/src/systems/npcs/public_farm.rs` |
| `QuestManager.cs` | QuestManager | merged | `QuestManager` merged into `aaemu-game/src/systems/npcs/hooks.rs` |
| `QuestManagerEvents.cs` | QuestManager | merged | `QuestManager` merged into `aaemu-game/src/systems/npcs/hooks.rs` |
| `RadarManager.cs` | RadarManager | ported | `RadarManager` → `aaemu-game/src/systems/services/radar.rs` |
| `SaveManager.cs` | SaveManager | ported | `SaveManager` → `aaemu-game/src/managers/save.rs` |
| `ShipyardManager.cs` | ShipyardManager | ported | `ShipyardManager` → `aaemu-game/src/systems/vehicles/shipyards.rs` |
| `SkillManager.cs` | SkillManager | merged | `SkillManager` merged into `aaemu-game/src/systems/skills/data/mod.rs` |
| `SlaveManager.cs` | SlaveManager | merged | `SlaveManager` merged into `aaemu-game/src/systems/skills/ext.rs` |
| `SqliteExperienceLevelTemplateLoader.cs` | SqliteExperienceLevelTemplateLoader | merged | `SqliteExperienceLevelTemplateLoader` merged into `aaemu-game/src/data/experience.rs` |
| `SubZoneManager.cs` | SubZoneManager | merged | `SubZoneManager` merged into `aaemu-game/src/systems/npcs/subzone.rs` |
| `SusManager.cs` | SusManager | ported | `SusManager` → `aaemu-game/src/systems/services/sus.rs` |
| `TaskManager2.cs` | TaskManager | ported | `TaskManager` → `aaemu-game/src/systems/services/tasks.rs` |
| `TaxationsManager.cs` | TaxationsManager | merged | `TaxationsManager` merged into `aaemu-game/src/systems/housing/data.rs` |
| `TeamManager.cs` | TeamManager, MemberSyncState, InvitationTemplate | ported | `TeamManager` → `aaemu-game/src/systems/social/team.rs`; `MemberSyncState` → `aaemu-game/src/systems/social/team.rs`; `InvitationTemplate` merged into `aaemu-game/src/systems/social/team.rs` |
| `TickManager.cs` | TickManager, TickEventEntity, TickEventHandler | ported | `TickManager` → `aaemu-game/src/systems/services/tick.rs`; `TickEventEntity` → `aaemu-game/src/systems/services/tick.rs`; `TickEventHandler` → `aaemu-game/src/systems/services/tick.rs` |
| `TimeManager.cs` | TimeManager | ported | `TimeManager` → `aaemu-game/src/managers/time.rs` |
| `TimedRewardsManager.cs` | TimedRewardsManager | merged | `TimedRewardsManager` merged into `aaemu-game/src/systems/services/mod.rs` |
| `TradeManager.cs` | TradeTemplate, TradeManager | ported | `TradeTemplate` → `aaemu-game/src/systems/economy/trade.rs`; `TradeManager` → `aaemu-game/src/systems/economy/trade.rs` |
| `TransferManager.cs` | TransferManager | ported | `TransferManager` → `aaemu-game/src/systems/vehicles/transfers.rs` |
| `TrialManager.cs` | TrialManager | ported | `TrialManager` → `aaemu-game/src/systems/social/trial.rs` |

### `AAEmu.Game/Core/Managers/Id`

| File | Types | Status | Rust |
|---|---|---|---|
| `AuctionIdManager.cs` | AuctionIdManager | merged | `AuctionIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `CharacterIdManager.cs` | CharacterIdManager | merged | `CharacterIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ChatIdManager.cs` | ChatIdManager | merged | `ChatIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ContainerIdManager.cs` | ContainerIdManager | merged | `ContainerIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `CrimeIdManager.cs` | CrimeIdManager | merged | `CrimeIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `DoodadIdManager.cs` | DoodadIdManager | merged | `DoodadIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ExpeditionIdManager.cs` | ExpeditionIdManager | merged | `ExpeditionIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `FamilyIdManager.cs` | FamilyIdManager | merged | `FamilyIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `FriendIdManager.cs` | FriendIdManager | merged | `FriendIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `GimmickIdManager.cs` | GimmickIdManager | merged | `GimmickIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `HousingIdManager.cs` | HousingIdManager | merged | `HousingIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `HousingTldManager.cs` | HousingTldManager | merged | `HousingTldManager` merged into `aaemu-game/src/managers/id.rs` |
| `IAuctionIdManager.cs` | IAuctionIdManager | merged | `IAuctionIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ICharacterIdManager.cs` | ICharacterIdManager | merged | `ICharacterIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IChatIdManager.cs` | IChatIdManager | merged | `IChatIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IContainerIdManager.cs` | IContainerIdManager | merged | `IContainerIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ICrimeIdManager.cs` | ICrimeIdManager | merged | `ICrimeIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IDoodadIdManager.cs` | IDoodadIdManager | merged | `IDoodadIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IExpeditionIdManager.cs` | IExpeditionIdManager | merged | `IExpeditionIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IFamilyIdManager.cs` | IFamilyIdManager | merged | `IFamilyIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IFriendIdManager.cs` | IFriendIdManager | merged | `IFriendIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IGimmickIdManager.cs` | IGimmickIdManager | merged | `IGimmickIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IHousingIdManager.cs` | IHousingIdManager | merged | `IHousingIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IHousingTldManager.cs` | IHousingTldManager | merged | `IHousingTldManager` merged into `aaemu-game/src/managers/id.rs` |
| `IIdManager.cs` | IIdManager | ported | `IIdManager` → `aaemu-game/src/managers/id.rs` |
| `IItemIdManager.cs` | IItemIdManager | merged | `IItemIdManager` merged into `aaemu-game/src/managers/item_tests.rs` |
| `IMailIdManager.cs` | IMailIdManager | merged | `IMailIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IMateIdManager.cs` | IMateIdManager | merged | `IMateIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IMusicIdManager.cs` | IMusicIdManager | merged | `IMusicIdManager` merged into `aaemu-game/src/systems/services/music_tests.rs` |
| `IObjectIdManager.cs` | IObjectIdManager | merged | `IObjectIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IPrivateBookIdManager.cs` | IPrivateBookIdManager | merged | `IPrivateBookIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IQuestIdManager.cs` | IQuestIdManager | merged | `IQuestIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IShipyardIdManager.cs` | IShipyardIdManager | merged | `IShipyardIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ITaskIdManager.cs` | ITaskIdManager | merged | `ITaskIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ITeamIdManager.cs` | ITeamIdManager | merged | `ITeamIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ITlIdManager.cs` | ITlIdManager | ported | `ITlIdManager` → `aaemu-game/src/systems/skills/tasks.rs` |
| `ITradeIdManager.cs` | ITradeIdManager | merged | `ITradeIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ITrialIdManager.cs` | ITrialIdManager | merged | `ITrialIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IUccIdManager.cs` | IUccIdManager | ported | `IUccIdManager` → `aaemu-game/src/stream/ucc.rs` |
| `IVisitedSubZoneIdManager.cs` | IVisitedSubZoneIdManager | merged | `IVisitedSubZoneIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `IWorldIdManager.cs` | IWorldIdManager | merged | `IWorldIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ItemIdManager.cs` | ItemIdManager | merged | `ItemIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `MailIdManager.cs` | MailIdManager | merged | `MailIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `MateIdManager.cs` | MateIdManager | merged | `MateIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `MusicIdManager.cs` | MusicIdManager | merged | `MusicIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ObjectIdManager.cs` | ObjectIdManager | merged | `ObjectIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `PrivateBookIdManager.cs` | PrivateBookIdManager | merged | `PrivateBookIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `QuestIdManager.cs` | QuestIdManager | merged | `QuestIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `ShipyardIdManager.cs` | ShipyardIdManager | merged | `ShipyardIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `SkillTlIdManager.cs` | SkillTlIdManager | merged | `SkillTlIdManager` merged into `aaemu-game/src/systems/skills/tasks.rs` |
| `TaskIdManager.cs` | TaskIdManager | merged | `TaskIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `TeamIdManager.cs` | TeamIdManager | merged | `TeamIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `TlIdManager.cs` | TlIdManager | ported | `TlIdManager` → `aaemu-game/src/systems/skills/tasks.rs` |
| `TradeIdManager.cs` | TradeIdManager | merged | `TradeIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `TrialIdManager.cs` | TrialIdManager | merged | `TrialIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `UccIdManager.cs` | UccIdManager | ported | `UccIdManager` → `aaemu-game/src/stream/ucc.rs` |
| `VisitedSubZoneIdManager.cs` | VisitedSubZoneIdManager | merged | `VisitedSubZoneIdManager` merged into `aaemu-game/src/managers/id.rs` |
| `WorldIdManager.cs` | WorldIdManager | merged | `WorldIdManager` merged into `aaemu-game/src/managers/id.rs` |

### `AAEmu.Game/Core/Managers/Stream`

| File | Types | Status | Rust |
|---|---|---|---|
| `IUccManager.cs` | IUccManager | ported | `IUccManager` → `aaemu-game/src/stream/ucc.rs` |
| `UccManager.cs` | UccManager | ported | `UccManager` → `aaemu-game/src/stream/ucc.rs` |

### `AAEmu.Game/Core/Managers/UnitManagers`

| File | Types | Status | Rust |
|---|---|---|---|
| `CharacterManager.cs` | CharacterManager | merged | `CharacterManager` merged into `aaemu-game/src/systems/services/mod.rs` |
| `DoodadManager.cs` | DoodadManager | ported | `DoodadManager` → `aaemu-game/src/systems/npcs/doodad/manager.rs` |
| `ICharacterManager.cs` | ICharacterManager | merged | `ICharacterManager` merged into `aaemu-game/src/managers/name_tests.rs` |
| `IDoodadManager.cs` | IDoodadManager | ported | `IDoodadManager` → `aaemu-game/src/systems/npcs/doodad/manager.rs` |
| `INpcManager.cs` | INpcManager | ported | `INpcManager` → `aaemu-game/src/systems/npcs/npc/manager.rs` |
| `NpcManager.cs` | NpcManager | ported | `NpcManager` → `aaemu-game/src/systems/npcs/npc/manager.rs` |

### `AAEmu.Game/Core/Managers/World`

| File | Types | Status | Rust |
|---|---|---|---|
| `AreaTriggerManager.cs` | AreaTriggerManager | ported | `AreaTriggerManager` → `aaemu-game/src/systems/npcs/area_trigger.rs` |
| `EnterWorldManager.cs` | EnterWorldManager | ported | `EnterWorldManager` → `aaemu-game/src/managers/enter_world.rs` |
| `FactionManager.cs` | FactionManager | merged | `FactionManager` merged into `aaemu-game/src/systems/social/faction.rs` |
| `IAreaTriggerManager.cs` | IAreaTriggerManager | ported | `IAreaTriggerManager` → `aaemu-game/src/systems/npcs/area_trigger.rs` |
| `IEnterWorldManager.cs` | IEnterWorldManager | ported | `IEnterWorldManager` → `aaemu-game/src/managers/enter_world.rs` |
| `IFactionManager.cs` | IFactionManager | merged | `IFactionManager` merged into `aaemu-game/src/systems/social/faction.rs` |
| `ISpecialtyManager.cs` | ISpecialtyManager | ported | `ISpecialtyManager` → `aaemu-game/src/systems/npcs/specialty.rs` |
| `ISphereQuestManager.cs` | ISphereQuestManager | ported | `ISphereQuestManager` → `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `IStreamManager.cs` | IStreamManager | ported | `IStreamManager` → `aaemu-game/src/managers/stream.rs` |
| `IWorldManager.cs` | IWorldManager | ported | `IWorldManager` → `aaemu-game/src/world/mod.rs` |
| `IZoneManager.cs` | IZoneManager | merged | `IZoneManager` merged into `aaemu-game/src/systems/instances/data.rs` |
| `PhysicsManager.cs` | PhysicsManager | merged | `PhysicsManager` merged into `aaemu-game/src/systems/ai/physics/mod.rs` |
| `ShipStaticBarrierBaiIngestor.cs` | ShipStaticBarrierBaiIngestor, BaiLayers, Defaults, ReferenceEqualityComparer | merged | `ShipStaticBarrierBaiIngestor` merged into `aaemu-game/src/systems/ai/physics/barriers.rs`; `BaiLayers` merged into `systems/ai/physics/barriers.rs`; `Defaults` → `aaemu-game/src/systems/skills/ext.rs`; `ReferenceEqualityComparer` merged into `systems/ai/physics/barriers.rs` |
| `SpawnManager.cs` | SpawnManager | ported | `SpawnManager` → `aaemu-game/src/systems/npcs/spawn_manager.rs` |
| `SpecialtyManager.cs` | SpecialtyManager | ported | `SpecialtyManager` → `aaemu-game/src/systems/npcs/specialty.rs` |
| `SphereQuestManager.cs` | SphereQuestManager | ported | `SphereQuestManager` → `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `StreamManager.cs` | StreamManager | ported | `StreamManager` → `aaemu-game/src/managers/stream.rs` |
| `WorldManager.cs` | WorldManager | ported | `WorldManager` → `aaemu-game/src/world/mod.rs` |
| `ZoneManager.cs` | ZoneManager | merged | `ZoneManager` merged into `aaemu-game/src/systems/instances/data.rs` |

### `AAEmu.Game/Core/Managers/World/Debug`

| File | Types | Status | Rust |
|---|---|---|---|
| `WaterDebugSnapshot.cs` | WaterDebugSnapshot | dropped | `WaterDebugSnapshot` dropped: debug only (water-debug GM command) |

### `AAEmu.Game/Core/Network/Connections`

| File | Types | Status | Rust |
|---|---|---|---|
| `GameConnection.cs` | GameConnection | ported | `GameConnection` → `aaemu-game/src/net/connection.rs` |
| `GameConnectionTable.cs` | GameConnectionTable | merged | `GameConnectionTable` merged into `aaemu-game/src/net/connection.rs` |
| `GameState.cs` | GameState | ported | `GameState` → `aaemu-game/src/systems/instances/instant_game.rs` |
| `LoginConnection.cs` | LoginConnection | ported | `LoginConnection` → `aaemu-login/src/network/login.rs` |
| `StreamConnection.cs` | StreamConnection | ported | `StreamConnection` → `aaemu-game/src/stream/connection.rs` |
| `StreamConnectionTable.cs` | StreamConnectionTable | ported | `StreamConnectionTable` → `aaemu-game/src/stream/connection.rs` |

### `AAEmu.Game/Core/Network/Game`

| File | Types | Status | Rust |
|---|---|---|---|
| `GameNetwork.cs` | GameNetwork | merged | `GameNetwork` merged into `aaemu-game/src/packets/mod.rs` |
| `GamePacket.cs` | GamePacket | merged | `GamePacket` merged into `aaemu-game/src/net/connection.rs` |
| `GameProtocolHandler.cs` | GameProtocolHandler | merged | `GameProtocolHandler` merged into `aaemu-game/src/net/listener.rs` |

### `AAEmu.Game/Core/Network/Login`

| File | Types | Status | Rust |
|---|---|---|---|
| `LoginNetwork.cs` | LoginNetwork | merged | `LoginNetwork` merged into `aaemu-game/src/login/mod.rs` |
| `LoginPacket.cs` | LoginPacket | merged | `LoginPacket` merged into `aaemu-game/src/login/mod.rs` |
| `LoginProtocolHandler.cs` | LoginProtocolHandler | merged | `LoginProtocolHandler` merged into `aaemu-game/src/login/mod.rs` |

### `AAEmu.Game/Core/Network/Stream`

| File | Types | Status | Rust |
|---|---|---|---|
| `StreamNetwork.cs` | StreamNetwork | merged | `StreamNetwork` merged into `aaemu-game/src/stream/listener.rs` |
| `StreamPacket.cs` | StreamPacket | merged | `StreamPacket` merged into `aaemu-game/src/stream/packet.rs` |
| `StreamProtocolHandler.cs` | StreamProtocolHandler | merged | `StreamProtocolHandler` merged into `aaemu-game/src/stream/listener.rs` |

### `AAEmu.Game/Core/Packets`

| File | Types | Status | Rust |
|---|---|---|---|
| `CompressedGamePackets.cs` | CompressedGamePackets | ported | `CompressedGamePackets` → `aaemu-game/src/systems/skills/packets.rs` |

### `AAEmu.Game/Core/Packets/C2G`

| File | Types | Status | Rust |
|---|---|---|---|
| `CSAcceptCheatQuestContextPacket.cs` | CSAcceptCheatQuestContextPacket | ported | `CSAcceptCheatQuestContextPacket` → `aaemu-game/src/opcodes.rs` |
| `CSActiveWeaponChangedPacket.cs` | CSActiveWeaponChangedPacket | ported | `CSActiveWeaponChangedPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `CSAddBlockedUserPacket.cs` | CSAddBlockedUserPacket | ported | `CSAddBlockedUserPacket` → `aaemu-game/src/systems/social/packets/friend.rs` |
| `CSAddFriendPacket.cs` | CSAddFriendPacket | ported | `CSAddFriendPacket` → `aaemu-game/src/systems/social/packets/friend.rs` |
| `CSAllowHousingRecoverPacket.cs` | CSAllowHousingRecoverPacket | ported | `CSAllowHousingRecoverPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSApplyToInstantGamePacket.cs` | CSApplyToInstantGamePacket | ported | `CSApplyToInstantGamePacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `CSAskRiskyTeamActionPacket.cs` | CSAskRiskyTeamActionPacket | ported | `CSAskRiskyTeamActionPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSAuctionLowestPricePacket.cs` | CSAuctionLowestPricePacket | ported | `CSAuctionLowestPricePacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `CSAuctionMyBidListPacket.cs` | CSAuctionMyBidListPacket | ported | `CSAuctionMyBidListPacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `CSAuctionPostPacket.cs` | CSAuctionPostPacket | ported | `CSAuctionPostPacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `CSAuctionSearchPacket.cs` | CSAuctionSearchPacket | ported | `CSAuctionSearchPacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `CSBeautyshopDataPacket.cs` | CSBeautyshopDataPacket | ported | `CSBeautyshopDataPacket` → `aaemu-game/src/systems/services/beauty_salon.rs` |
| `CSBidAuctionPacket.cs` | CSBidAuctionPacket | ported | `CSBidAuctionPacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `CSBindSlavePacket.cs` | CSBindSlavePacket | ported | `CSBindSlavePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSBoardingTransferPacket.cs` | CSBoardingTransferPacket | ported | `CSBoardingTransferPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSBroadcastVisualOptionPacket.cs` | CSBroadcastVisualOptionPacket | ported | `CSBroadcastVisualOptionPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `CSBuyCoinItemPacket.cs` | CSBuyCoinItemPacket | ported | `CSBuyCoinItemPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `CSBuyHousePacket.cs` | CSBuyHousePacket | ported | `CSBuyHousePacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSBuyItemsPacket.cs` | CSBuyItemsPacket | ported | `CSBuyItemsPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `CSBuyPriestBuffPacket.cs` | CSBuyPriestBuffPacket | ported | `CSBuyPriestBuffPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `CSBuySpecialtyItemPacket.cs` | CSBuySpecialtyItemPacket | ported | `CSBuySpecialtyItemPacket` → `aaemu-game/src/systems/economy/specialty.rs` |
| `CSCanStartTradePacket.cs` | CSCanStartTradePacket | ported | `CSCanStartTradePacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `CSCancelAuctionPacket.cs` | CSCancelAuctionPacket | ported | `CSCancelAuctionPacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `CSCancelCharacterDeletePacket.cs` | CSCancelCharacterDeletePacket | ported | `CSCancelCharacterDeletePacket` → `aaemu-game/src/packets/c2g_world.rs` |
| `CSCancelInstantGamePacket.cs` | CSCancelInstantGamePacket | ported | `CSCancelInstantGamePacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `CSCancelLeaveWorldPacket.cs` | CSCancelLeaveWorldPacket | ported | `CSCancelLeaveWorldPacket` → `aaemu-game/src/packets/c2g_world.rs` |
| `CSCancelTradePacket.cs` | CSCancelTradePacket | ported | `CSCancelTradePacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `CSCancelTrialPacket.cs` | CSCancelTrialPacket | ported | `CSCancelTrialPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSCannotStartTradePacket.cs` | CSCannotStartTradePacket | ported | `CSCannotStartTradePacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `CSChallengeDuelPacket.cs` | CSChallengeDuelPacket | ported | `CSChallengeDuelPacket` → `aaemu-game/src/systems/social/packets/duel.rs` |
| `CSChangeAppellationPacket.cs` | CSChangeAppellationPacket | ported | `CSChangeAppellationPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `CSChangeDoodadDataPacket.cs` | CSChangeDoodadDataPacket | ported | `CSChangeDoodadDataPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `CSChangeDoodadPhasePacket.cs` | CSChangeDoodadPhasePacket | ported | `CSChangeDoodadPhasePacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `CSChangeExpeditionMemberRolePacket.cs` | CSChangeExpeditionMemberRolePacket | ported | `CSChangeExpeditionMemberRolePacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `CSChangeExpeditionOwnerPacket.cs` | CSChangeExpeditionOwnerPacket | ported | `CSChangeExpeditionOwnerPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `CSChangeExpeditionRolePolicyPacket.cs` | CSChangeExpeditionRolePolicyPacket | ported | `CSChangeExpeditionRolePolicyPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `CSChangeExpeditionSponsorPacket.cs` | CSChangeExpeditionSponsorPacket | ported | `CSChangeExpeditionSponsorPacket` → `aaemu-game/src/packets/c2g_misc.rs` |
| `CSChangeHouseNamePacket.cs` | CSChangeHouseNamePacket | ported | `CSChangeHouseNamePacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSChangeHousePayPacket.cs` | CSChangeHousePayPacket | ported | `CSChangeHousePayPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSChangeHousePermissionPacket.cs` | CSChangeHousePermissionPacket | ported | `CSChangeHousePermissionPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSChangeItemLookPacket.cs` | CSChangeItemLookPacket | ported | `CSChangeItemLookPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `CSChangeLootingRulePacket.cs` | CSChangeLootingRulePacket | ported | `CSChangeLootingRulePacket` → `aaemu-game/src/systems/economy/looting.rs` |
| `CSChangeMateEquipmentPacket.cs` | CSChangeMateEquipmentPacket | ported | `CSChangeMateEquipmentPacket` → `aaemu-game/src/systems/economy/mate_equipment.rs` |
| `CSChangeMateNamePacket.cs` | CSChangeMateNamePacket | ported | `CSChangeMateNamePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSChangeMateTargetPacket.cs` | CSChangeMateTargetPacket | ported | `CSChangeMateTargetPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSChangeMateUserStatePacket.cs` | CSChangeMateUserStatePacket | ported | `CSChangeMateUserStatePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSChangeSlaveEquipmentPacket.cs` | CSChangeSlaveEquipmentPacket | ported | `CSChangeSlaveEquipmentPacket` → `aaemu-game/src/systems/economy/mate_equipment.rs` |
| `CSChangeSlaveNamePacket.cs` | CSChangeSlaveNamePacket | ported | `CSChangeSlaveNamePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSChangeSlaveTargetPacket.cs` | CSChangeSlaveTargetPacket | ported | `CSChangeSlaveTargetPacket` → `aaemu-game/src/packets/c2g_misc.rs` |
| `CSChangeTargetPacket.cs` | CSChangeTargetPacket | ported | `CSChangeTargetPacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSCharDetailPacket.cs` | CSCharDetailPacket | ported | `CSCharDetailPacket` → `aaemu-game/src/opcodes.rs` |
| `CSCleanupLogicLinkPacket.cs` | CSCleanupLogicLinkPacket | ported | `CSCleanupLogicLinkPacket` → `aaemu-game/src/opcodes.rs` |
| `CSCofferInteractionPacket.cs` | CSCofferInteractionPacket | ported | `CSCofferInteractionPacket` → `aaemu-game/src/systems/npcs/doodad/coffer.rs` |
| `CSCompleteQuestContextPacket.cs` | CSCompleteQuestContextPacket | ported | `CSCompleteQuestContextPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `CSCompletedCinemaPacket.cs` | CSCompletedCinemaPacket | ported | `CSCompletedCinemaPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `CSConsoleCmdUsedPacket.cs` | CSConsoleCmdUsedPacket | ported | `CSConsoleCmdUsedPacket` → `aaemu-game/src/opcodes.rs` |
| `CSConstructHouseTaxPacket.cs` | CSConstructHouseTaxPacket | ported | `CSConstructHouseTaxPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSConvertItemLookPacket.cs` | CSConvertItemLookPacket | ported | `CSConvertItemLookPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `CSConvertToRaidTeamPacket.cs` | CSConvertToRaidTeamPacket | ported | `CSConvertToRaidTeamPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSCreateCharacterPacket.cs` | CSCreateCharacterPacket | ported | `CSCreateCharacterPacket` → `aaemu-game/src/packets/c2g_world.rs` |
| `CSCreateDoodadPacket.cs` | CSCreateDoodadPacket | ported | `CSCreateDoodadPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `CSCreateExpeditionPacket.cs` | CSCreateExpeditionPacket | ported | `CSCreateExpeditionPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `CSCreateHousePacket.cs` | CSCreateHousePacket | ported | `CSCreateHousePacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSCreateShipyardPacket.cs` | CSCreateShipyardPacket | ported | `CSCreateShipyardPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSCreateSkillControllerPacket.cs` | CSCreateSkillControllerPacket | ported | `CSCreateSkillControllerPacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSCriminalLockedPacket.cs` | CSCriminalLockedPacket | ported | `CSCriminalLockedPacket` → `aaemu-game/src/opcodes.rs` |
| `CSDecorateHousePacket.cs` | CSDecorateHousePacket | ported | `CSDecorateHousePacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSDeleteBlockedUserPacket.cs` | CSDeleteBlockedUserPacket | ported | `CSDeleteBlockedUserPacket` → `aaemu-game/src/systems/social/packets/friend.rs` |
| `CSDeleteCharacterPacket.cs` | CSDeleteCharacterPacket | ported | `CSDeleteCharacterPacket` → `aaemu-game/src/packets/c2g_world.rs` |
| `CSDeleteFriendPacket.cs` | CSDeleteFriendPacket | ported | `CSDeleteFriendPacket` → `aaemu-game/src/systems/social/packets/friend.rs` |
| `CSDeleteMailPacket.cs` | CSDeleteMailPacket | ported | `CSDeleteMailPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSDeletePortalPacket.cs` | CSDeletePortalPacket | ported | `CSDeletePortalPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `CSDepositMoneyPacket.cs` | CSDepositMoneyPacket | ported | `CSDepositMoneyPacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSDespawnSlavePacket.cs` | CSDespawnSlavePacket | ported | `CSDespawnSlavePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSDestroyItemPacket.cs` | CSDestroyItemPacket | ported | `CSDestroyItemPacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSDestroySlavePacket.cs` | CSDestroySlavePacket | ported | `CSDestroySlavePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSDiscardSlavePacket.cs` | CSDiscardSlavePacket | ported | `CSDiscardSlavePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSDismissExpeditionPacket.cs` | CSDismissExpeditionPacket | ported | `CSDismissExpeditionPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `CSDismissTeamPacket.cs` | CSDismissTeamPacket | ported | `CSDismissTeamPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSDowngradeExpertLimitPacket.cs` | CSDowngradeExpertLimitPacket | ported | `CSDowngradeExpertLimitPacket` → `aaemu-game/src/systems/services/expands.rs` |
| `CSDropQuestContextPacket.cs` | CSDropQuestContextPacket | ported | `CSDropQuestContextPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `CSEditCharacterPacket.cs` | CSEditCharacterPacket | ported | `CSEditCharacterPacket` → `aaemu-game/src/packets/c2g_misc.rs` |
| `CSEditorGameModePacket.cs` | CSEditorGameModePacket | ported | `CSEditorGameModePacket` → `aaemu-game/src/opcodes.rs` |
| `CSEndMusicPacket.cs` | CSEndMusicPacket | ported | `CSEndMusicPacket` → `aaemu-game/src/systems/services/music.rs` |
| `CSEnterBeautySalonPacket.cs` | CSEnterBeautySalonPacket | ported | `CSEnterBeautySalonPacket` → `aaemu-game/src/systems/services/beauty_salon.rs` |
| `CSEnteredInstantGameWorldPacket.cs` | CSEnteredInstantGameWorldPacket | ported | `CSEnteredInstantGameWorldPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `CSEquipmentsSecurePacket.cs` | CSEquipmentsSecurePacket | ported | `CSEquipmentsSecurePacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSEquipmentsUnsecurePacket.cs` | CSEquipmentsUnsecurePacket | ported | `CSEquipmentsUnsecurePacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSExecuteCraft.cs` | CSExecuteCraft | ported | `CSExecuteCraft` → `aaemu-game/src/systems/economy/craft.rs` |
| `CSExitBeautySalonPacket.cs` | CSExitBeautySalonPacket | ported | `CSExitBeautySalonPacket` → `aaemu-game/src/systems/services/beauty_salon.rs` |
| `CSExpandExpertPacket.cs` | CSExpandExpertPacket | ported | `CSExpandExpertPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `CSExpandSlotsPacket.cs` | CSExpandSlotsPacket | ported | `CSExpandSlotsPacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSExpressEmotionPacket.cs` | CSExpressEmotionPacket | ported | `CSExpressEmotionPacket` → `aaemu-game/src/systems/services/express_text.rs` |
| `CSFactionDeclareHostilePacket.cs` | CSFactionDeclareHostilePacket | ported | `CSFactionDeclareHostilePacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `CSFactionImmigrateToOriginPacket.cs` | CSFactionImmigrateToOriginPacket | ported | `CSFactionImmigrateToOriginPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `CSFactionImmigrationInvitePacket.cs` | CSFactionImmigrationInvitePacket | ported | `CSFactionImmigrationInvitePacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `CSFactionImmigrationInviteReplyPacket.cs` | CSFactionImmigrationInviteReplyPacket | ported | `CSFactionImmigrationInviteReplyPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `CSFactionKickToOriginPacket.cs` | CSFactionKickToOriginPacket | ported | `CSFactionKickToOriginPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `CSFamilyChangeOwnerPacket.cs` | CSFamilyChangeOwnerPacket | ported | `CSFamilyChangeOwnerPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `CSFamilyChangeTitlePacket.cs` | CSFamilyChangeTitlePacket | ported | `CSFamilyChangeTitlePacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `CSFamilyInviteMemberPacket.cs` | CSFamilyInviteMemberPacket | ported | `CSFamilyInviteMemberPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `CSFamilyKickPacket.cs` | CSFamilyKickPacket | ported | `CSFamilyKickPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `CSFamilyLeavePacket.cs` | CSFamilyLeavePacket | ported | `CSFamilyLeavePacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `CSFamilyReplyInvitationPacket.cs` | CSFamilyReplyInvitationPacket | ported | `CSFamilyReplyInvitationPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `CSHangPacket.cs` | CSHangPacket | ported | `CSHangPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSICSBuyGoodPacket.cs` | CSICSBuyGoodPacket | ported | `CSICSBuyGoodPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CSICSGoodsListPacket.cs` | CSICSGoodsListPacket | ported | `CSICSGoodsListPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CSICSMenuListPacket.cs` | CSICSMenuListPacket | ported | `CSICSMenuListPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CSICSMoneyRequestPacket.cs` | CSICSMoneyRequestPacket | ported | `CSICSMoneyRequestPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CSIdleStatusPacket.cs` | CSIdleStatusPacket | ported | `CSIdleStatusPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `CSInstanceLoadedPacket.cs` | CSInstanceLoadedPacket | ported | `CSInstanceLoadedPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `CSInteractNPCEndPacket.cs` | CSInteractNPCEndPacket | ported | `CSInteractNPCEndPacket` → `aaemu-game/src/systems/npcs/npc/packets.rs` |
| `CSInteractNPCPacket.cs` | CSInteractNPCPacket | ported | `CSInteractNPCPacket` → `aaemu-game/src/systems/npcs/npc/packets.rs` |
| `CSInviteAreaToTeamPacket.cs` | CSInviteAreaToTeamPacket | ported | `CSInviteAreaToTeamPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSInviteToExpeditionPacket.cs` | CSInviteToExpeditionPacket | ported | `CSInviteToExpeditionPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `CSInviteToTeamPacket.cs` | CSInviteToTeamPacket | ported | `CSInviteToTeamPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSItemSecurePacket.cs` | CSItemSecurePacket | ported | `CSItemSecurePacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSItemUnsecurePacket.cs` | CSItemUnsecurePacket | ported | `CSItemUnsecurePacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSJoinInstantGamePacket.cs` | CSJoinInstantGamePacket | ported | `CSJoinInstantGamePacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `CSJoinTrialAudiencePacket.cs` | CSJoinTrialAudiencePacket | ported | `CSJoinTrialAudiencePacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSJoinUserChatChannelPacket.cs` | CSJoinUserChatChannelPacket | ported | `CSJoinUserChatChannelPacket` → `aaemu-game/src/systems/social/packets/chat.rs` |
| `CSJuryEndTestimonyPacket.cs` | CSJuryEndTestimonyPacket | ported | `CSJuryEndTestimonyPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSJurySummonedPacket.cs` | CSJurySummonedPacket | ported | `CSJurySummonedPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSJuryVerdictPacket.cs` | CSJuryVerdictPacket | ported | `CSJuryVerdictPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSKickFromExpeditionPacket.cs` | CSKickFromExpeditionPacket | ported | `CSKickFromExpeditionPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `CSKickTeamMemberPacket.cs` | CSKickTeamMemberPacket | ported | `CSKickTeamMemberPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSLearnBuffPacket.cs` | CSLearnBuffPacket | ported | `CSLearnBuffPacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSLearnSkillPacket.cs` | CSLearnSkillPacket | ported | `CSLearnSkillPacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSLeaveChatChannelPacket.cs` | CSLeaveChatChannelPacket | ported | `CSLeaveChatChannelPacket` → `aaemu-game/src/systems/social/packets/chat.rs` |
| `CSLeaveExpeditionPacket.cs` | CSLeaveExpeditionPacket | ported | `CSLeaveExpeditionPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `CSLeaveInstantGamePacket.cs` | CSLeaveInstantGamePacket | ported | `CSLeaveInstantGamePacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `CSLeaveTeamPacket.cs` | CSLeaveTeamPacket | ported | `CSLeaveTeamPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSLeaveTrialAudiencePacket.cs` | CSLeaveTrialAudiencePacket | ported | `CSLeaveTrialAudiencePacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSLeaveWorldPacket.cs` | CSLeaveWorldPacket | ported | `CSLeaveWorldPacket` → `aaemu-game/src/packets/c2g.rs` |
| `CSListCharacterPacket.cs` | CSListCharacterPacket | ported | `CSListCharacterPacket` → `aaemu-game/src/packets/c2g.rs` |
| `CSListMailContinuePacket.cs` | CSListMailContinuePacket | ported | `CSListMailContinuePacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSListMailPacket.cs` | CSListMailPacket | ported | `CSListMailPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSListSoldItemPacket.cs` | CSListSoldItemPacket | ported | `CSListSoldItemPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `CSListSpecialtyGoodsPacket.cs` | CSListSpecialtyGoodsPacket | ported | `CSListSpecialtyGoodsPacket` → `aaemu-game/src/systems/economy/specialty.rs` |
| `CSLootCloseBagPacket.cs` | CSLootCloseBagPacket | ported | `CSLootCloseBagPacket` → `aaemu-game/src/systems/economy/looting.rs` |
| `CSLootDicePacket.cs` | CSLootDicePacket | ported | `CSLootDicePacket` → `aaemu-game/src/systems/economy/looting.rs` |
| `CSLootItemPacket.cs` | CSLootItemPacket | ported | `CSLootItemPacket` → `aaemu-game/src/systems/economy/looting.rs` |
| `CSLootOpenBagPacket.cs` | CSLootOpenBagPacket | ported | `CSLootOpenBagPacket` → `aaemu-game/src/systems/economy/looting.rs` |
| `CSMakeTeamOwnerPacket.cs` | CSMakeTeamOwnerPacket | ported | `CSMakeTeamOwnerPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSMountMatePacket.cs` | CSMountMatePacket | ported | `CSMountMatePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSMoveTeamMemberPacket.cs` | CSMoveTeamMemberPacket | ported | `CSMoveTeamMemberPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSMoveUnitPacket.cs` | CSMoveUnitPacket | ported | `CSMoveUnitPacket` → `aaemu-game/src/packets/c2g_world.rs` |
| `CSNaviOpenBountyPacket.cs` | CSNaviOpenBountyPacket | ported | `CSNaviOpenBountyPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSNaviOpenPortalPacket.cs` | CSNaviOpenPortalPacket | ported | `CSNaviOpenPortalPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `CSNaviTeleportPacket.cs` | CSNaviTeleportPacket | ported | `CSNaviTeleportPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `CSNotifyInGameCompletedPacket.cs` | CSNotifyInGameCompletedPacket | ported | `CSNotifyInGameCompletedPacket` → `aaemu-game/src/packets/c2g_world.rs` |
| `CSNotifyInGamePacket.cs` | CSNotifyInGamePacket | ported | `CSNotifyInGamePacket` → `aaemu-game/src/packets/c2g_world.rs` |
| `CSNotifySubZonePacket.cs` | CSNotifySubZonePacket | ported | `CSNotifySubZonePacket` → `aaemu-game/src/systems/npcs/subzone.rs` |
| `CSOffsets.cs` | CSOffsets | merged | `CSOffsets` merged into `aaemu-game/src/opcodes.rs` |
| `CSPayChargeMoneyPacket.cs` | CSPayChargeMoneyPacket | ported | `CSPayChargeMoneyPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSPremiumServiceBuyPacket.cs` | CSPremiumServiceBuyPacket | ported | `CSPremiumServiceBuyPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CSPremiumServiceListPacket.cs` | CSPremiumServiceListPacket | ported | `CSPremiumServiceListPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CSPremiumServiceMsgPacket.cs` | CSPremiumServiceMsgPacket | ported | `CSPremiumServiceMsgPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CSPutupTradeItemPacket.cs` | CSPutupTradeItemPacket | ported | `CSPutupTradeItemPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `CSPutupTradeMoneyPacket.cs` | CSPutupTradeMoneyPacket | ported | `CSPutupTradeMoneyPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `CSQuestStartWithPacket.cs` | CSQuestStartWithPacket | ported | `CSQuestStartWithPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `CSQuestTalkMadePacket.cs` | CSQuestTalkMadePacket | ported | `CSQuestTalkMadePacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `CSRankCharacterPacket.cs` | CSRankCharacterPacket | ported | `CSRankCharacterPacket` → `aaemu-game/src/opcodes.rs` |
| `CSReadMailPacket.cs` | CSReadMailPacket | ported | `CSReadMailPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSRefreshInCharacterListPacket.cs` | CSRefreshInCharacterListPacket | ported | `CSRefreshInCharacterListPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `CSRemoveBuffPacket.cs` | CSRemoveBuffPacket | ported | `CSRemoveBuffPacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSRemoveMatePacket.cs` | CSRemoveMatePacket | ported | `CSRemoveMatePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSRenameExpeditionPacket.cs` | CSRenameExpeditionPacket | ported | `CSRenameExpeditionPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `CSRepairAllEquipmentsPacket.cs` | CSRepairAllEquipmentsPacket | ported | `CSRepairAllEquipmentsPacket` → `aaemu-game/src/systems/economy/repair.rs` |
| `CSRepairPetItemsPacket.cs` | CSRepairPetItemsPacket | ported | `CSRepairPetItemsPacket` → `aaemu-game/src/systems/economy/mate_equipment.rs` |
| `CSRepairSingleEquipmentPacket.cs` | CSRepairSingleEquipmentPacket | ported | `CSRepairSingleEquipmentPacket` → `aaemu-game/src/systems/economy/repair.rs` |
| `CSRepairSlaveItemsPacket.cs` | CSRepairSlaveItemsPacket | ported | `CSRepairSlaveItemsPacket` → `aaemu-game/src/systems/economy/mate_equipment.rs` |
| `CSReplyExpeditionInvitationPacket.cs` | CSReplyExpeditionInvitationPacket | ported | `CSReplyExpeditionInvitationPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `CSReplyImprisonOrTrialPacket.cs` | CSReplyImprisonOrTrialPacket | ported | `CSReplyImprisonOrTrialPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSReplyInviteJuryPacket.cs` | CSReplyInviteJuryPacket | ported | `CSReplyInviteJuryPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSReplyToJoinTeamPacket.cs` | CSReplyToJoinTeamPacket | ported | `CSReplyToJoinTeamPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSReportCrimePacket.cs` | CSReportCrimePacket | ported | `CSReportCrimePacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSReportSpamPacket.cs` | CSReportSpamPacket | ported | `CSReportSpamPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `CSRequestCharBriefPacket.cs` | CSRequestCharBriefPacket | ported | `CSRequestCharBriefPacket` → `aaemu-game/src/opcodes.rs` |
| `CSRequestCommonFarmList.cs` | CSRequestCommonFarmList | ported | `CSRequestCommonFarmList` → `aaemu-game/src/systems/npcs/public_farm.rs` |
| `CSRequestHouseTaxPacket.cs` | CSRequestHouseTaxPacket | ported | `CSRequestHouseTaxPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSRequestJuryWaitingNumberPacket.cs` | CSRequestJuryWaitingNumberPacket | ported | `CSRequestJuryWaitingNumberPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSRequestMusicNotesPacket.cs` | CSRequestMusicNotesPacket | ported | `CSRequestMusicNotesPacket` → `aaemu-game/src/systems/services/music.rs` |
| `CSRequestPermissionToPlayCinemaForDirectingMode.cs` | CSRequestPermissionToPlayCinemaForDirectingMode | ported | `CSRequestPermissionToPlayCinemaForDirectingMode` → `aaemu-game/src/opcodes.rs` |
| `CSRequestSecondPasswordKeyTablesPacket.cs` | CSRequestSecondPasswordKeyTablesPacket | ported | `CSRequestSecondPasswordKeyTablesPacket` → `aaemu-game/src/opcodes.rs` |
| `CSRequestSpecialtyCurrentPacket.cs` | CSRequestSpecialtyCurrentPacket | ported | `CSRequestSpecialtyCurrentPacket` → `aaemu-game/src/systems/economy/specialty.rs` |
| `CSRequestUIDataPacket.cs` | CSRequestUIDataPacket | ported | `CSRequestUIDataPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `CSResetQuestContextPacket.cs` | CSResetQuestContextPacket | ported | `CSResetQuestContextPacket` → `aaemu-game/src/opcodes.rs` |
| `CSResetSkillsPacket.cs` | CSResetSkillsPacket | ported | `CSResetSkillsPacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSRestartMainQuestPacket.cs` | CSRestartMainQuestPacket | ported | `CSRestartMainQuestPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `CSRestrictCheckPacket.cs` | CSRestrictCheckPacket | ported | `CSRestrictCheckPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `CSResturnAddrsPacket.cs` | CSResturnAddrsPacket | ported | `CSResturnAddrsPacket` → `aaemu-game/src/opcodes.rs` |
| `CSResurrectCharacterPacket.cs` | CSResurrectCharacterPacket | ported | `CSResurrectCharacterPacket` → `aaemu-game/src/systems/skills/resurrect.rs` |
| `CSReturnMailPacket.cs` | CSReturnMailPacket | ported | `CSReturnMailPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSRollDicePacket.cs` | CSRollDicePacket | ported | `CSRollDicePacket` → `aaemu-game/src/systems/services/character.rs` |
| `CSSaveDoodadUccStringPacket.cs` | CSSaveDoodadUccStringPacket | ported | `CSSaveDoodadUccStringPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `CSSaveTutorialPacket.cs` | CSSaveTutorialPacket | ported | `CSSaveTutorialPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `CSSaveUIDataPacket.cs` | CSSaveUIDataPacket | ported | `CSSaveUIDataPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `CSSaveUserMusicNotes.cs` | CSSaveUserMusicNotesPacket | ported | `CSSaveUserMusicNotesPacket` → `aaemu-game/src/systems/services/music.rs` |
| `CSSearchListPacket.cs` | CSSearchListPacket | ported | `CSSearchListPacket` → `aaemu-game/src/packets/c2g_misc.rs` |
| `CSSelectCharacterPacket.cs` | CSSelectCharacterPacket | ported | `CSSelectCharacterPacket` → `aaemu-game/src/packets/c2g_world.rs` |
| `CSSelectInteractionExPacket.cs` | CSSelectInteractionExPacket | ported | `CSSelectInteractionExPacket` → `aaemu-game/src/systems/npcs/npc/packets.rs` |
| `CSSellBackpackGoodsPacket.cs` | CSSellBackpackGoodsPacket | ported | `CSSellBackpackGoodsPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `CSSellHouseCancelPacket.cs` | CSSellHouseCancelPacket | ported | `CSSellHouseCancelPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSSellHousePacket.cs` | CSSellHousePacket | ported | `CSSellHousePacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `CSSellItemsPacket.cs` | CSSellItemsPacket | ported | `CSSellItemsPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `CSSendChatMessagePacket.cs` | CSSendChatMessagePacket | ported | `CSSendChatMessagePacket` → `aaemu-game/src/packets/c2g_world.rs` |
| `CSSendMailPacket.cs` | CSSendMailPacket | ported | `CSSendMailPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSSendUserMusicPacket.cs` | CSSendUserMusicPacket | ported | `CSSendUserMusicPacket` → `aaemu-game/src/systems/services/music.rs` |
| `CSSetCraftingPayPacket.cs` | CSSetCraftingPayPacket | ported | `CSSetCraftingPayPacket` → `aaemu-game/src/systems/economy/craft.rs` |
| `CSSetForceAttackPacket.cs` | CSSetForceAttackPacket | ported | `CSSetForceAttackPacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSSetLogicDoodadPacket.cs` | CSSetLogicDoodadPacket | ported | `CSSetLogicDoodadPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `CSSetLpManageCharacterPacket.cs` | CSSetLpManageCharacterPacket | ported | `CSSetLpManageCharacterPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `CSSetOverHeadMarkerPacket.cs` | CSSetOverHeadMarkerPacket | ported | `CSSetOverHeadMarkerPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSSetPingPosPacket.cs` | CSSetPingPosPacket | ported | `CSSetPingPosPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSSetTeamMemberRolePacket.cs` | CSSetTeamMemberRolePacket | ported | `CSSetTeamMemberRolePacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `CSSetTeamOfficerPacket.cs` | CSSetTeamOfficerPacket | ported | `CSSetTeamOfficerPacket` → `aaemu-game/src/packets/c2g_misc.rs` |
| `CSSetupSecondPassword.cs` | CSSetupSecondPassword | ported | `CSSetupSecondPassword` → `aaemu-game/src/opcodes.rs` |
| `CSSkillControllerStatePacket.cs` | CSSkillControllerStatePacket | ported | `CSSkillControllerStatePacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSSkipFinalStatementPacket.cs` | CSSkipFinalStatementPacket | ported | `CSSkipFinalStatementPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `CSSpawnCharacterPacket.cs` | CSSpawnCharacterPacket | ported | `CSSpawnCharacterPacket` → `aaemu-game/src/packets/c2g_world.rs` |
| `CSSpawnSlavePacket.cs` | CSSpawnSlavePacket | ported | `CSSpawnSlavePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSSpecialtyRatioPacket.cs` | CSSpecialtyRatioPacket | ported | `CSSpecialtyRatioPacket` → `aaemu-game/src/systems/economy/specialty.rs` |
| `CSSpecialtyRecordLoadPacket.cs` | CSSpecialtyRecordLoadPacket | ported | `CSSpecialtyRecordLoadPacket` → `aaemu-game/src/systems/economy/specialty.rs` |
| `CSSplitBagItemPacket.cs` | CSSplitBagItemPacket | ported | `CSSplitBagItemPacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSSplitCofferItemPacket.cs` | CSSplitCofferItemPacket | ported | `CSSplitCofferItemPacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSStartDuelPacket.cs` | CSStartDuelPacket | ported | `CSStartDuelPacket` → `aaemu-game/src/systems/social/packets/duel.rs` |
| `CSStartInteractionPacket.cs` | CSStartInteractionPacket | ported | `CSStartInteractionPacket` → `aaemu-game/src/systems/npcs/npc/packets.rs` |
| `CSStartQuestContextPacket.cs` | CSStartQuestContextPacket | ported | `CSStartQuestContextPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `CSStartSkillPacket.cs` | CSStartSkillPacket | ported | `CSStartSkillPacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSStartTradePacket.cs` | CSStartTradePacket | ported | `CSStartTradePacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `CSStartedCinemaPacket.cs` | CSStartedCinemaPacket | ported | `CSStartedCinemaPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `CSStopCastingPacket.cs` | CSStopCastingPacket | ported | `CSStopCastingPacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSSwapAbilityPacket.cs` | CSSwapAbilityPacket | ported | `CSSwapAbilityPacket` → `aaemu-game/src/systems/skills/handlers.rs` |
| `CSSwapCofferItemsPacket.cs` | CSSwapCofferItemsPacket | ported | `CSSwapCofferItemsPacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSSwapItemsPacket.cs` | CSSwapItemsPacket | ported | `CSSwapItemsPacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSTakeAllAttachmentItemPacket.cs` | CSTakeAllAttachmentItemPacket | ported | `CSTakeAllAttachmentItemPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSTakeAttachmentItemPacket.cs` | CSTakeAttachmentItemPacket | ported | `CSTakeAttachmentItemPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSTakeAttachmentMoneyPacket.cs` | CSTakeAttachmentMoneyPacket | ported | `CSTakeAttachmentMoneyPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSTakeAttachmentSequentially.cs` | CSTakeAttachmentSequentially | ported | `CSTakeAttachmentSequentially` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `CSTakedownTradeItemPacket.cs` | CSTakedownTradeItemPacket | ported | `CSTakedownTradeItemPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `CSTeleportEndedPacket.cs` | CSTeleportEndedPacket | ported | `CSTeleportEndedPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `CSThisTimeUnpackItemPacket.cs` | CSThisTimeUnpackItemPacket | ported | `CSThisTimeUnpackItemPacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `CSTradeLockPacket.cs` | CSTradeLockPacket | ported | `CSTradeLockPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `CSTradeOkPacket.cs` | CSTradeOkPacket | ported | `CSTradeOkPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `CSTryQuestCompleteAsLetItDonePacket.cs` | CSTryQuestCompleteAsLetItDonePacket | ported | `CSTryQuestCompleteAsLetItDonePacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `CSTurretStatePacket.cs` | CSTurretStatePacket | ported | `CSTurretStatePacket` → `aaemu-game/src/packets/c2g_misc.rs` |
| `CSUnMountMatePacket.cs` | CSUnMountMatePacket | ported | `CSUnMountMatePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSUnbondDoodadPacket.cs` | CSUnbondDoodadPacket | ported | `CSUnbondDoodadPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `CSUnhangPacket.cs` | CSUnhangPacket | ported | `CSUnhangPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `CSUnknownInstancePacket.cs` | CSUnknownInstancePacket | ported | `CSUnknownInstancePacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `CSUpdateActionSlotPacket.cs` | CSUpdateActionSlotPacket | ported | `CSUpdateActionSlotPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `CSUpdateDominionTaxRatePacket.cs` | CSUpdateDominionTaxRatePacket | ported | `CSUpdateDominionTaxRatePacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `CSUpdateNationalTaxRatePacket.cs` | CSUpdateNationalTaxRatePacket | ported | `CSUpdateNationalTaxRatePacket` → `aaemu-game/src/packets/c2g_misc.rs` |
| `CSUpgradeExpertLimitPacket.cs` | CSUpgradeExpertLimitPacket | ported | `CSUpgradeExpertLimitPacket` → `aaemu-game/src/systems/services/expands.rs` |
| `CSUsePortalPacket.cs` | CSUsePortalPacket | ported | `CSUsePortalPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `CSUseTeleportPacket.cs` | CSUseTeleportPacket | ported | `CSUseTeleportPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `CSWithdrawMoneyPacket.cs` | CSWithdrawMoneyPacket | ported | `CSWithdrawMoneyPacket` → `aaemu-game/src/systems/economy/item_handlers.rs` |
| `X2EnterWorldPacket.cs` | X2EnterWorldPacket | ported | `X2EnterWorldPacket` → `aaemu-game/src/packets/c2g.rs` |

### `AAEmu.Game/Core/Packets/C2S`

| File | Types | Status | Rust |
|---|---|---|---|
| `CTCancelCellPacket.cs` | CTCancelCellPacket | ported | `CTCancelCellPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTContinuePacket.cs` | CTContinuePacket | ported | `CTContinuePacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTEmblemPartDownloadedPacket.cs` | CTEmblemPartDownloadedPacket | ported | `CTEmblemPartDownloadedPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTEmblemStreamDownloadStatusPacket.cs` | CTEmblemStreamDownloadStatusPacket | ported | `CTEmblemStreamDownloadStatusPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTEmblemStreamUploadStatusPacket.cs` | CTEmblemStreamUploadStatusPacket | ported | `CTEmblemStreamUploadStatusPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTItemUccPacket.cs` | CTItemUccPacket | ported | `CTItemUccPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTJoinPacket.cs` | CTJoinPacket | ported | `CTJoinPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTOffsets.cs` | CTOffsets | merged | `CTOffsets` merged into `aaemu-game/src/stream/opcodes.rs` |
| `CTQueryCharNamePacket.cs` | CTQueryCharNamePacket | ported | `CTQueryCharNamePacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTRequestCellPacket.cs` | CTRequestCellPacket | ported | `CTRequestCellPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTRequestEmblemPacket.cs` | CTRequestEmblemPacket | ported | `CTRequestEmblemPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTStartUploadEmblemStreamPacket.cs` | CTStartUploadEmblemStreamPacket | ported | `CTStartUploadEmblemStreamPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTUccCharacterNamePacket.cs` | CTUccCharacterNamePacket | ported | `CTUccCharacterNamePacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTUccComplexCheckValidPacket.cs` | CTUccComplexCheckValidPacket | ported | `CTUccComplexCheckValidPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTUccComplexPacket.cs` | CTUccComplexPacket | ported | `CTUccComplexPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTUccPositionPacket.cs` | CTUccPositionPacket | ported | `CTUccPositionPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTUccStringPacket.cs` | CTUccStringPacket | ported | `CTUccStringPacket` → `aaemu-game/src/stream/c2s.rs` |
| `CTUploadEmblemStreamPacket.cs` | CTUploadEmblemStreamPacket | ported | `CTUploadEmblemStreamPacket` → `aaemu-game/src/stream/c2s.rs` |

### `AAEmu.Game/Core/Packets/Debug`

| File | Types | Status | Rust |
|---|---|---|---|
| `SkillControllerPacketDebug.cs` | SkillControllerPacketDebug | dropped | `SkillControllerPacketDebug` dropped: debug logging, flag hard-wired off |

### `AAEmu.Game/Core/Packets/G2C`

| File | Types | Status | Rust |
|---|---|---|---|
| `SCAbilityExpChangedPacket.cs` | SCAbilityExpChangedPacket | ported | `SCAbilityExpChangedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCAbilitySwappedPacket.cs` | SCAbilitySwappedPacket | ported | `SCAbilitySwappedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCAboxTeleportPacket.cs` | SCAboxTeleportPacket | ported | `SCAboxTeleportPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `SCAccountAttributeConfigPacket.cs` | SCAccountAttributeConfigPacket | ported | `SCAccountAttributeConfigPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCAccountInfoPacket.cs` | SCAccountInfoPacket | ported | `SCAccountInfoPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCAccountWarnedPacket.cs` | SCAccountWarnedPacket | ported | `SCAccountWarnedPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCAchievementChangedPacket.cs` | SCAchievementChangedPacket | ported | `SCAchievementChangedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCAchievementCompletedPacket.cs` | SCAchievementCompletedPacket | ported | `SCAchievementCompletedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCAchievementItemSentPacket.cs` | SCAchievementItemSentPacket | ported | `SCAchievementItemSentPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCAchievementResetedPacket.cs` | SCAchievementResetedPacket | ported | `SCAchievementResetedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCAchievementsPacket.cs` | SCAchievementsPacket | ported | `SCAchievementsPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCActabilityPacket.cs` | SCActabilityPacket | ported | `SCActabilityPacket` → `aaemu-game/src/packets/world.rs` |
| `SCActionSlotsPacket.cs` | SCActionSlotsPacket | ported | `SCActionSlotsPacket` → `aaemu-game/src/packets/world.rs` |
| `SCActiveWeaponChangedPacket.cs` | SCActiveWeaponChangedPacket | ported | `SCActiveWeaponChangedPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `SCAddActionPointPacket.cs` | SCAddActionPointPacket | ported | `SCAddActionPointPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCAddBlockedUserPacket.cs` | SCAddBlockedUserPacket | ported | `SCAddBlockedUserPacket` → `aaemu-game/src/systems/social/packets/friend.rs` |
| `SCAddFriendPacket.cs` | SCAddFriendPacket | ported | `SCAddFriendPacket` → `aaemu-game/src/systems/social/packets/friend.rs` |
| `SCAggroTargetChangedPacket.cs` | SCAggroTargetChangedPacket | ported | `SCAggroTargetChangedPacket` → `aaemu-game/src/systems/npcs/npc/packets.rs` |
| `SCAiAggroPacket.cs` | SCAiAggroPacket | ported | `SCAiAggroPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCAppellationChangedPacket.cs` | SCAppellationChangedPacket | ported | `SCAppellationChangedPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `SCAppellationGainedPacket.cs` | SCAppellationGainedPacket | ported | `SCAppellationGainedPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `SCAppellationsPacket.cs` | SCAppellationsPacket | ported | `SCAppellationsPacket` → `aaemu-game/src/packets/world.rs` |
| `SCAppliedToInstantGamePacket.cs` | SCAppliedToInstantGamePacket | ported | `SCAppliedToInstantGamePacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCAreaChatBubblePacket.cs` | SCAreaChatBubblePacket | ported | `SCAreaChatBubblePacket` → `aaemu-game/src/systems/social/packets/duel.rs` |
| `SCAskImprisonOrTrialPacket.cs` | SCAskImprisonOrTrialPacket | ported | `SCAskImprisonOrTrialPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCAskToJoinTeamAreaPacket.cs` | SCAskToJoinTeamAreaPacket | ported | `SCAskToJoinTeamAreaPacket` → `aaemu-game/src/opcodes.rs` |
| `SCAskToJoinTeamPacket.cs` | SCAskToJoinTeamPacket | ported | `SCAskToJoinTeamPacket` → `aaemu-game/src/opcodes.rs` |
| `SCAttachmentTakenPacket.cs` | SCAttachmentTakenPacket | ported | `SCAttachmentTakenPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCAuctionBidPacket.cs` | SCAuctionBidPacket | ported | `SCAuctionBidPacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `SCAuctionCanceledPacket.cs` | SCAuctionCanceledPacket | ported | `SCAuctionCanceledPacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `SCAuctionLowestPricePacket.cs` | SCAuctionLowestPricePacket | ported | `SCAuctionLowestPricePacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `SCAuctionPostedPacket.cs` | SCAuctionPostedPacket | ported | `SCAuctionPostedPacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `SCAuctionSearchedPacket.cs` | SCAuctionSearchedPacket | ported | `SCAuctionSearchedPacket` → `aaemu-game/src/systems/economy/auction_packets.rs` |
| `SCBlinkUnitPacket.cs` | SCBlinkUnitPacket | ported | `SCBlinkUnitPacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCBlockedUsersPacket.cs` | SCBlockedUsersPacket | ported | `SCBlockedUsersPacket` → `aaemu-game/src/packets/world.rs` |
| `SCBmPointPacket.cs` | SCBmPointPacket | ported | `SCBmPointPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCBondDoodadPacket.cs` | SCBondDoodadPacket | ported | `SCBondDoodadPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `SCBotSuspectArrestedPacket.cs` | SCBotSuspectArrestedPacket | ported | `SCBotSuspectArrestedPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCBotSuspectReportedPacket.cs` | SCBotSuspectReportedPacket | ported | `SCBotSuspectReportedPacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCBountyListPacket.cs` | SCBountyListPacket | ported | `SCBountyListPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCBuffCreatedPacket.cs` | SCBuffCreatedPacket | ported | `SCBuffCreatedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCBuffLearnedPacket.cs` | SCBuffLearnedPacket | ported | `SCBuffLearnedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCBuffRemovedPacket.cs` | SCBuffRemovedPacket | ported | `SCBuffRemovedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCCanStartTradePacket.cs` | SCCanStartTradePacket | ported | `SCCanStartTradePacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCCancelCharacterDeleteResponsePacket.cs` | SCCancelCharacterDeleteResponsePacket | ported | `SCCancelCharacterDeleteResponsePacket` → `aaemu-game/src/packets/world.rs` |
| `SCCannotStartTradePacket.cs` | SCCannotStartTradePacket | ported | `SCCannotStartTradePacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCCastingStoppedPacket.cs` | SCCastingStoppedPacket | ported | `SCCastingStoppedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCChangeJuryOKCountPacket.cs` | SCChangeJuryOKCountPacket | merged | `SCChangeJuryOKCountPacket` merged into `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCChangeJuryVerdictCountPacket.cs` | SCChangeJuryVerdictCountPacket | ported | `SCChangeJuryVerdictCountPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCChangeTrialStatePacket.cs` | SCChangeTrialStatePacket | ported | `SCChangeTrialStatePacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCCharBriefPacket.cs` | SCCharBriefPacket | ported | `SCCharBriefPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCCharDetailPacket.cs` | SCCharDetailPacket | ported | `SCCharDetailPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCCharacterBoundPacket.cs` | SCCharacterBoundPacket | ported | `SCCharacterBoundPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCCharacterCreationFailedPacket.cs` | SCCharacterCreationFailedPacket | ported | `SCCharacterCreationFailedPacket` → `aaemu-game/src/packets/world.rs` |
| `SCCharacterDeletedPacket.cs` | SCCharacterDeletedPacket | ported | `SCCharacterDeletedPacket` → `aaemu-game/src/packets/world.rs` |
| `SCCharacterGamePointsPacket.cs` | SCCharacterGamePointsPacket | ported | `SCCharacterGamePointsPacket` → `aaemu-game/src/packets/world.rs` |
| `SCCharacterGenderAndModelModifiedPacket.cs` | SCCharacterGenderAndModelModifiedPacket | ported | `SCCharacterGenderAndModelModifiedPacket` → `aaemu-game/src/systems/services/beauty_salon.rs` |
| `SCCharacterInvenContentsPacket.cs` | SCCharacterInvenContentsPacket | ported | `SCCharacterInvenContentsPacket` → `aaemu-game/src/packets/world.rs` |
| `SCCharacterInvenInitPacket.cs` | SCCharacterInvenInitPacket | ported | `SCCharacterInvenInitPacket` → `aaemu-game/src/packets/world.rs` |
| `SCCharacterLaborPowerChangedPacket.cs` | SCCharacterLaborPowerChangedPacket | ported | `SCCharacterLaborPowerChangedPacket` → `aaemu-game/src/systems/skills/progress.rs` |
| `SCCharacterListPacket.cs` | SCCharacterListPacket | ported | `SCCharacterListPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCCharacterPortalsPacket.cs` | SCCharacterPortalsPacket | ported | `SCCharacterPortalsPacket` → `aaemu-game/src/packets/world.rs` |
| `SCCharacterRenamedPacket.cs` | SCCharacterRenamedPacket | ported | `SCCharacterRenamedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCCharacterResurrectedPacket.cs` | SCCharacterResurrectedPacket | ported | `SCCharacterResurrectedPacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCCharacterReturnDistrictsPacket.cs` | SCCharacterReturnDistrictsPacket | ported | `SCCharacterReturnDistrictsPacket` → `aaemu-game/src/packets/world.rs` |
| `SCCharacterStatePacket.cs` | SCCharacterStatePacket | ported | `SCCharacterStatePacket` → `aaemu-game/src/world/unit_state.rs` |
| `SCChargeMoneyPaidPacket.cs` | SCChargeMoneyPaidPacket | ported | `SCChargeMoneyPaidPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCChatBubblePacket.cs` | SCChatBubblePacket | ported | `SCChatBubblePacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCChatMessagePacket.cs` | SCChatMessagePacket | ported | `SCChatMessagePacket` → `aaemu-game/src/packets/world.rs` |
| `SCChatSpamDelayPacket.cs` | SCChatSpamDelayPacket | ported | `SCChatSpamDelayPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCCofferContentsUpdatePacket.cs` | SCCofferContentsUpdatePacket | ported | `SCCofferContentsUpdatePacket` → `aaemu-game/src/systems/npcs/doodad/coffer.rs` |
| `SCCombatClearedPacket.cs` | SCCombatClearedPacket | ported | `SCCombatClearedPacket` → `aaemu-game/src/opcodes.rs` |
| `SCCombatEngagedPacket.cs` | SCCombatEngagedPacket | ported | `SCCombatEngagedPacket` → `aaemu-game/src/opcodes.rs` |
| `SCCombatFirstHitPacket.cs` | SCCombatFirstHitPacket | ported | `SCCombatFirstHitPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCCombatTextPacket.cs` | SCCombatTextPacket | ported | `SCCombatTextPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCCompletedQuestsPacket.cs` | SCCompletedQuestsPacket | ported | `SCCompletedQuestsPacket` → `aaemu-game/src/packets/world.rs` |
| `SCConflictZoneHonorPointSumPacket.cs` | SCConflictZoneHonorPointSumPacket | ported | `SCConflictZoneHonorPointSumPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCConflictZoneStatePacket.cs` | SCConflictZoneStatePacket | ported | `SCConflictZoneStatePacket` → `aaemu-game/src/packets/world.rs` |
| `SCConstructHouseTaxPacket.cs` | SCConstructHouseTaxPacket | ported | `SCConstructHouseTaxPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCCoolDownPacket.cs` | SCCooldownsPacket | ported | `SCCooldownsPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCCountUnreadMailPacket.cs` | SCCountUnreadMailPacket | ported | `SCCountUnreadMailPacket` → `aaemu-game/src/packets/world.rs` |
| `SCCraftFailedPacket.cs` | SCCraftFailedPacket | ported | `SCCraftFailedPacket` → `aaemu-game/src/systems/economy/craft.rs` |
| `SCCreateCharacterResponsePacket.cs` | SCCreateCharacterResponsePacket | ported | `SCCreateCharacterResponsePacket` → `aaemu-game/src/world/unit_state.rs` |
| `SCCrimeChangedPacket.cs` | SCCrimeChangedPacket | ported | `SCCrimeChangedPacket` → `aaemu-game/src/systems/social/crime.rs` |
| `SCCrimeDataPacket.cs` | SCCrimeDataPacket | ported | `SCCrimeDataPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCCrimeRecordsPacket.cs` | SCCrimeRecordsPacket | ported | `SCCrimeRecordsPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCCriminalArrestedPacket.cs` | SCCriminalArrestedPacket | ported | `SCCriminalArrestedPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCCvFCombatRelationshipPacket.cs` | SCCvFCombatRelationshipPacket | ported | `SCCvFCombatRelationshipPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCDeleteBlockedUserPacket.cs` | SCDeleteBlockedUserPacket | ported | `SCDeleteBlockedUserPacket` → `aaemu-game/src/systems/social/packets/friend.rs` |
| `SCDeleteCharacterResponsePacket.cs` | SCDeleteCharacterResponsePacket | ported | `SCDeleteCharacterResponsePacket` → `aaemu-game/src/packets/world.rs` |
| `SCDeleteFriendPacket.cs` | SCDeleteFriendPacket | ported | `SCDeleteFriendPacket` → `aaemu-game/src/systems/social/packets/friend.rs` |
| `SCDetailedTimeOfDayPacket.cs` | SCDetailedTimeOfDayPacket | ported | `SCDetailedTimeOfDayPacket` → `aaemu-game/src/packets/world.rs` |
| `SCDiceValuePacket.cs` | SCDiceValuePacket | ported | `SCDiceValuePacket` → `aaemu-game/src/systems/services/character.rs` |
| `SCDominionDataPacket.cs` | SCDominionDataPacket | ported | `SCDominionDataPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCDominionDeletedPacket.cs` | SCDominionDeletedPacket | ported | `SCDominionDeletedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCDominionOwnerChangedPacket.cs` | SCDominionOwnerChangedPacket | ported | `SCDominionOwnerChangedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCDominionTaxBalancedPacket.cs` | SCDominionTaxBalancedPacket | ported | `SCDominionTaxBalancedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCDominionTaxRatePacket.cs` | SCDominionTaxRatePacket | ported | `SCDominionTaxRatePacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCDoodadChangedPacket.cs` | SCDoodadChangedPacket | ported | `SCDoodadChangedPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `SCDoodadCreatedPacket.cs` | SCDoodadCreatedPacket | ported | `SCDoodadCreatedPacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCDoodadOriginatorPacket.cs` | SCDoodadOriginatorPacket | ported | `SCDoodadOriginatorPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `SCDoodadPhaseChangedPacket.cs` | SCDoodadPhaseChangedPacket | ported | `SCDoodadPhaseChangedPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `SCDoodadQuestAcceptPacket.cs` | SCDoodadQuestAcceptPacket | ported | `SCDoodadQuestAcceptPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `SCDoodadRemovedPacket.cs` | SCDoodadRemovedPacket | ported | `SCDoodadRemovedPacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCDoodadSoundPacket.cs` | SCDoodadSoundPacket | ported | `SCDoodadSoundPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `SCDoodadsCreatedPacket.cs` | SCDoodadsCreatedPacket | ported | `SCDoodadsCreatedPacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCDoodadsRemovedPacket.cs` | SCDoodadsRemovedPacket | ported | `SCDoodadsRemovedPacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCDuelChallengedPacket.cs` | SCDuelChallengedPacket | ported | `SCDuelChallengedPacket` → `aaemu-game/src/systems/social/packets/duel.rs` |
| `SCDuelEndedPacket.cs` | SCDuelEndedPacket | ported | `SCDuelEndedPacket` → `aaemu-game/src/systems/social/packets/duel.rs` |
| `SCDuelStartCountdownPacket.cs` | SCDuelStartCountdownPacket | ported | `SCDuelStartCountdownPacket` → `aaemu-game/src/systems/social/packets/duel.rs` |
| `SCDuelStartedPacket.cs` | SCDuelStartedPacket | ported | `SCDuelStartedPacket` → `aaemu-game/src/systems/social/packets/duel.rs` |
| `SCDuelStatePacket.cs` | SCDuelStatePacket | ported | `SCDuelStatePacket` → `aaemu-game/src/systems/social/packets/duel.rs` |
| `SCEmotionExpressedPacket.cs` | SCEmotionExpressedPacket | ported | `SCEmotionExpressedPacket` → `aaemu-game/src/systems/services/express_text.rs` |
| `SCEnvDamagePacket.cs` | SCEnvDamagePacket | ported | `SCEnvDamagePacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCErrorMsgPacket.cs` | SCErrorMsgPacket | ported | `SCErrorMsgPacket` → `aaemu-game/src/packets/world.rs` |
| `SCEscapeSlavePacket.cs` | SCEscapeSlavePacket | ported | `SCEscapeSlavePacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `SCExpChangedPacket.cs` | SCExpChangedPacket | ported | `SCExpChangedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCExpeditionDismissedPacket.cs` | SCExpeditionDismissedPacket | ported | `SCExpeditionDismissedPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `SCExpeditionInvitationPacket.cs` | SCExpeditionInvitationPacket | ported | `SCExpeditionInvitationPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `SCExpeditionMemberListPacket.cs` | SCExpeditionMemberListPacket | ported | `SCExpeditionMemberListPacket` → `aaemu-game/src/packets/world.rs` |
| `SCExpeditionMemberStatusChangedPacket.cs` | SCExpeditionMemberStatusChangedPacket | ported | `SCExpeditionMemberStatusChangedPacket` → `aaemu-game/src/packets/world.rs` |
| `SCExpeditionOwnerChangedPacket.cs` | SCExpeditionOwnerChangedPacket | ported | `SCExpeditionOwnerChangedPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `SCExpeditionRoleChangedPacket.cs` | SCExpeditionRoleChangedPacket | ported | `SCExpeditionRoleChangedPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `SCExpeditionRolePolicyChangedPacket.cs` | SCExpeditionRolePolicyChangedPacket | ported | `SCExpeditionRolePolicyChangedPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `SCExpeditionRolePolicyListPacket.cs` | SCExpeditionRolePolicyListPacket | ported | `SCExpeditionRolePolicyListPacket` → `aaemu-game/src/packets/world.rs` |
| `SCExpeditionShowRenameUIPacket.cs` | SCExpeditionShowRenameUIPacket | ported | `SCExpeditionShowRenameUIPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `SCExpeditionSponsorChangedPacket.cs` | SCExpeditionSponsorChangedPacket | ported | `SCExpeditionSponsorChangedPacket` → `aaemu-game/src/opcodes.rs` |
| `SCExpertExpandedPacket.cs` | SCExpertExpandedPacket | ported | `SCExpertExpandedPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `SCExpertLimitModifiedPacket.cs` | SCExpertLimitModifiedPacket | ported | `SCExpertLimitModifiedPacket` → `aaemu-game/src/systems/services/expands.rs` |
| `SCFactionCreatedPacket.cs` | SCFactionCreatedPacket | ported | `SCFactionCreatedPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `SCFactionDeclareHostileResultPacket.cs` | SCFactionDeclareHostileResultPacket | ported | `SCFactionDeclareHostileResultPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFactionImmigrateInvitePacket.cs` | SCFactionImmigrateInvitePacket | ported | `SCFactionImmigrateInvitePacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFactionImmigrateInviteResultPacket.cs` | SCFactionImmigrateInviteResultPacket | ported | `SCFactionImmigrateInviteResultPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFactionImmigrateToOriginResultPacket.cs` | SCFactionImmigrateToOriginResultPacket | ported | `SCFactionImmigrateToOriginResultPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFactionIndependencePacket.cs` | SCFactionIndependencePacket | ported | `SCFactionIndependencePacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFactionKickToOriginResultPacket.cs` | SCFactionKickToOriginResultPacket | ported | `SCFactionKickToOriginResultPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFactionListPacket.cs` | SCFactionListPacket | ported | `SCFactionListPacket` → `aaemu-game/src/packets/world.rs` |
| `SCFactionOwnerChangedPacket.cs` | SCFactionOwnerChangedPacket | ported | `SCFactionOwnerChangedPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFactionRelationExpiredPacket.cs` | SCFactionRelationExpiredPacket | ported | `SCFactionRelationExpiredPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFactionRelationListPacket.cs` | SCFactionRelationListPacket | ported | `SCFactionRelationListPacket` → `aaemu-game/src/packets/world.rs` |
| `SCFactionRenamedPacket.cs` | SCFactionRenamedPacket | ported | `SCFactionRenamedPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFactionRetryRenamePacket.cs` | SCFactionRetryRenamePacket | ported | `SCFactionRetryRenamePacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFactionSetRelationStatePacket.cs` | SCFactionSetRelationStatePacket | ported | `SCFactionSetRelationStatePacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCFamilyCreatedPacket.cs` | SCFamilyCreatedPacket | ported | `SCFamilyCreatedPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCFamilyDescPacket.cs` | SCFamilyDescPacket | ported | `SCFamilyDescPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCFamilyInvitationPacket.cs` | SCFamilyInvitationPacket | ported | `SCFamilyInvitationPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCFamilyMemberAddedPacket.cs` | SCFamilyMemberAddedPacket | ported | `SCFamilyMemberAddedPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCFamilyMemberNameChangedPacket.cs` | SCFamilyMemberNameChangedPacket | ported | `SCFamilyMemberNameChangedPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCFamilyMemberOnlinePacket.cs` | SCFamilyMemberOnlinePacket | ported | `SCFamilyMemberOnlinePacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCFamilyMemberRemovedPacket.cs` | SCFamilyMemberRemovedPacket | ported | `SCFamilyMemberRemovedPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCFamilyOwnerChangedPacket.cs` | SCFamilyOwnerChangedPacket | ported | `SCFamilyOwnerChangedPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCFamilyRemovedPacket.cs` | SCFamilyRemovedPacket | ported | `SCFamilyRemovedPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCFamilyTitleChangedPacket.cs` | SCFamilyTitleChangedPacket | ported | `SCFamilyTitleChangedPacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCFamilyTitlePacket.cs` | SCFamilyTitlePacket | ported | `SCFamilyTitlePacket` → `aaemu-game/src/systems/social/packets/family.rs` |
| `SCForceAttackSetPacket.cs` | SCForceAttackSetPacket | ported | `SCForceAttackSetPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCFriendStatusChangedPacket.cs` | SCFriendStatusChangedPacket | ported | `SCFriendStatusChangedPacket` → `aaemu-game/src/packets/world.rs` |
| `SCFriendsPacket.cs` | SCFriendsPacket | ported | `SCFriendsPacket` → `aaemu-game/src/packets/world.rs` |
| `SCFvFCombatRelationshipPacket.cs` | SCFvFCombatRelationshipPacket | ported | `SCFvFCombatRelationshipPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCGamePointChangedPacket.cs` | SCGamePointChangedPacket | ported | `SCGamePointChangedPacket` → `aaemu-game/src/systems/skills/progress.rs` |
| `SCGetSlotCountPacket.cs` | SCGetSlotCountPacket | ported | `SCGetSlotCountPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCGimmickJointsBrokenPacket.cs` | SCGimmickJointsBrokenPacket | ported | `SCGimmickJointsBrokenPacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCGimmickMovementPacket.cs` | SCGimmickMovementPacket | ported | `SCGimmickMovementPacket` → `aaemu-game/src/systems/npcs/gimmick/packets.rs` |
| `SCGimmicksCreatedPacket.cs` | SCGimmicksCreatedPacket | ported | `SCGimmicksCreatedPacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCGimmicksRemovedPacket.cs` | SCGimmicksRemovedPacket | ported | `SCGimmicksRemovedPacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCGotMailPacket.cs` | SCGotMailPacket | ported | `SCGotMailPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCGradeEnchantBroadcastPacket.cs` | SCGradeEnchantBroadcastPacket | ported | `SCGradeEnchantBroadcastPacket` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `SCGradeEnchantResultPacket.cs` | SCGradeEnchantResultPacket | ported | `SCGradeEnchantResultPacket` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `SCHackGuardRetAddrsRequestPacket.cs` | SCHackGuardRetAddrsRequestPacket | ported | `SCHackGuardRetAddrsRequestPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCHouseBuildPayChangedPacket.cs` | SCHouseBuildPayChangedPacket | ported | `SCHouseBuildPayChangedPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCHouseBuildProgressPacket.cs` | SCHouseBuildProgressPacket | ported | `SCHouseBuildProgressPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCHouseDemolishedPacket.cs` | SCHouseDemolishedPacket | ported | `SCHouseDemolishedPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCHouseFarmPacket.cs` | SCHouseFarmPacket | ported | `SCHouseFarmPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCHouseOwnerNameChangedPacket.cs` | SCHouseOwnerNameChangedPacket | ported | `SCHouseOwnerNameChangedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCHousePermissionChangedPacket.cs` | SCHousePermissionChangedPacket | ported | `SCHousePermissionChangedPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCHouseResetForSalePacket.cs` | SCHouseResetForSalePacket | ported | `SCHouseResetForSalePacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCHouseSetForSalePacket.cs` | SCHouseSetForSalePacket | ported | `SCHouseSetForSalePacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCHouseSoldPacket.cs` | SCHouseSoldPacket | ported | `SCHouseSoldPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCHouseStatePacket.cs` | SCHouseStatePacket | ported | `SCHouseStatePacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCHouseTaxInfoPacket.cs` | SCHouseTaxInfoPacket | ported | `SCHouseTaxInfoPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCHousingRecoverTogglePacket.cs` | SCHousingRecoverTogglePacket | ported | `SCHousingRecoverTogglePacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCHungPacket.cs` | SCHungPacket | ported | `SCHungPacket` → `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `SCICSBuyResultPacket.cs` | SCICSBuyResultPacket | ported | `SCICSBuyResultPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCICSCashPointPacket.cs` | SCICSCashPointPacket | ported | `SCICSCashPointPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCICSCheckTimePacket.cs` | SCICSCheckTimePacket | ported | `SCICSCheckTimePacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCICSExchangeRatioPacket.cs` | SCICSExchangeRatioPacket | ported | `SCICSExchangeRatioPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCICSGoodDetailPacket.cs` | SCICSGoodDetailPacket | ported | `SCICSGoodDetailPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCICSGoodListPacket.cs` | SCICSGoodListPacket | ported | `SCICSGoodListPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCICSMenuListPacket.cs` | SCICSMenuListPacket | ported | `SCICSMenuListPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCICSSyncGoodPacket.cs` | SCICSSyncGoodPacket | ported | `SCICSSyncGoodPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCIdleKickPacket.cs` | SCIdleKickPacket | ported | `SCIdleKickPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCInitialConfigPacket.cs` | SCInitialConfigPacket | ported | `SCInitialConfigPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCInstantGameAddPointPacket.cs` | SCInstantGameAddPointPacket | ported | `SCInstantGameAddPointPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCInstantGameEndPacket.cs` | SCInstantGameEndPacket | ported | `SCInstantGameEndPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCInstantGameJoinedPacket.cs` | SCInstantGameJoinedPacket | ported | `SCInstantGameJoinedPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCInstantGameKillPacket.cs` | SCInstantGameKillPacket | ported | `SCInstantGameKillPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCInstantGameKillstreakPacket.cs` | SCInstantGameKillstreakPacket | ported | `SCInstantGameKillstreakPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCInstantGameRemainTimePacket.cs` | SCInstantGameRemainTimePacket | ported | `SCInstantGameRemainTimePacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCInstantGameStartPacket.cs` | SCInstantGameStartPacket | ported | `SCInstantGameStartPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCInvenExpandedPacket.cs` | SCInvenExpandedPacket | ported | `SCInvenExpandedPacket` → `aaemu-game/src/systems/economy/item_packets.rs` |
| `SCInviteJuryPacket.cs` | SCInviteJuryPacket | ported | `SCInviteJuryPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCInviteToInstantGamePacket.cs` | SCInviteToInstantGamePacket | ported | `SCInviteToInstantGamePacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCItemSocketingLunagemResultPacket.cs` | SCItemSocketingLunagemResultPacket | ported | `SCItemSocketingLunagemResultPacket` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `SCItemSocketingLunastoneResultPacket.cs` | SCItemSocketingLunastoneResultPacket | ported | `SCItemSocketingLunastoneResultPacket` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `SCItemTaskSuccessPacket.cs` | SCItemTaskSuccessPacket | ported | `SCItemTaskSuccessPacket` → `aaemu-game/src/systems/economy/tests.rs` |
| `SCItemUccDataChangedPacket.cs` | SCItemUccDataChangedPacket | ported | `SCItemUccDataChangedPacket` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `SCJoinedChatChannelPacket.cs` | SCJoinedChatChannelPacket | ported | `SCJoinedChatChannelPacket` → `aaemu-game/src/packets/world.rs` |
| `SCJoinedTeamPacket.cs` | SCJoinedTeamPacket | ported | `SCJoinedTeamPacket` → `aaemu-game/src/systems/social/team.rs` |
| `SCJuryBeSeatedPacket.cs` | SCJuryBeSeatedPacket | ported | `SCJuryBeSeatedPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCJuryPointChangedPacket.cs` | SCJuryPointChangedPacket | ported | `SCJuryPointChangedPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCJuryWaitStatusPacket.cs` | SCJuryWaitStatusPacket | ported | `SCJuryWaitStatusPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCJuryWaitingNumberPacket.cs` | SCJuryWaitingNumberPacket | ported | `SCJuryWaitingNumberPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCKickedPacket.cs` | SCKickedPacket, KickedReason | ported | `SCKickedPacket` → `aaemu-game/src/packets/g2c_misc.rs`; `KickedReason` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCKnockBackUnitPacket.cs` | SCKnockBackUnitPacket | ported | `SCKnockBackUnitPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCLeaveWorldCanceledPacket.cs` | SCLeaveWorldCanceledPacket | ported | `SCLeaveWorldCanceledPacket` → `aaemu-game/src/packets/world.rs` |
| `SCLeaveWorldGrantedPacket.cs` | SCLeaveWorldGrantedPacket | ported | `SCLeaveWorldGrantedPacket` → `aaemu-game/src/packets/world.rs` |
| `SCLeavedChatChannelPacket.cs` | SCLeavedChatChannelPacket | ported | `SCLeavedChatChannelPacket` → `aaemu-game/src/packets/world.rs` |
| `SCLeavedTeamPacket.cs` | SCLeavedTeamPacket | ported | `SCLeavedTeamPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCLevelChangedPacket.cs` | SCLevelChangedPacket | ported | `SCLevelChangedPacket` → `aaemu-game/src/systems/skills/progress.rs` |
| `SCLevelRestrictionConfigPacket.cs` | SCLevelRestrictionConfigPacket | ported | `SCLevelRestrictionConfigPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCLoadInstancePacket.cs` | SCLoadInstancePacket | ported | `SCLoadInstancePacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCLoginCharInfoHouse.cs` | SCLoginCharInfoHouse | merged | `SCLoginCharInfoHouse` merged into `aaemu-game/src/packets/c2g.rs` |
| `SCLootBagDataPacket.cs` | SCLootBagDataPacket | ported | `SCLootBagDataPacket` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `SCLootDiceNotifyPacket.cs` | SCLootDiceNotifyPacket | ported | `SCLootDiceNotifyPacket` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `SCLootDicePacket.cs` | SCLootDicePacket | ported | `SCLootDicePacket` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `SCLootDiceSummaryPacket.cs` | SCLootDiceSummaryPacket | ported | `SCLootDiceSummaryPacket` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `SCLootItemFailedPacket.cs` | SCLootItemFailedPacket | ported | `SCLootItemFailedPacket` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `SCLootItemTookPacket.cs` | SCLootItemTookPacket | ported | `SCLootItemTookPacket` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `SCLootableStatePacket.cs` | SCLootableStatePacket | ported | `SCLootableStatePacket` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `SCLpManagedPacket.cs` | SCLpManagedPacket | ported | `SCLpManagedPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `SCMailBodyPacket.cs` | SCMailBodyPacket | ported | `SCMailBodyPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCMailDeletedPacket.cs` | SCMailDeletedPacket | ported | `SCMailDeletedPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCMailFailedPacket.cs` | SCMailFailedPacket | ported | `SCMailFailedPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCMailListEndPacket.cs` | SCMailListEndPacket | ported | `SCMailListEndPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCMailListPacket.cs` | SCMailListPacket | ported | `SCMailListPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCMailReceiverOpenedPacket.cs` | SCMailReceiverOpenedPacket | ported | `SCMailReceiverOpenedPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCMailRemovedPacket.cs` | SCMailRemovedPacket | ported | `SCMailRemovedPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCMailReturnedPacket.cs` | SCMailReturnedPacket | ported | `SCMailReturnedPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCMailSentPacket.cs` | SCMailSentPacket | ported | `SCMailSentPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCMailStatusUpdatedPacket.cs` | SCMailStatusUpdatedPacket | ported | `SCMailStatusUpdatedPacket` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `SCMateEquipmentChangedPacket.cs` | SCMateEquipmentChangedPacket | ported | `SCMateEquipmentChangedPacket` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `SCMateSpawnedPacket.cs` | SCMateSpawnedPacket | ported | `SCMateSpawnedPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `SCMateStatePacket.cs` | SCMateStatePacket | ported | `SCMateStatePacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCMileageChangedPacket.cs` | SCMileageChangedPacket | ported | `SCMileageChangedPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCMineAmountPacket.cs` | SCMineAmountPacket | ported | `SCMineAmountPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCMyHousePacket.cs` | SCMyHousePacket | ported | `SCMyHousePacket` → `aaemu-game/src/systems/housing/ops.rs` |
| `SCMyHouseRemovedPacket.cs` | SCMyHouseRemovedPacket | ported | `SCMyHouseRemovedPacket` → `aaemu-game/src/systems/housing/packets.rs` |
| `SCMySlavePacket.cs` | SCMySlavePacket | ported | `SCMySlavePacket` → `aaemu-game/src/systems/vehicles/slaves.rs` |
| `SCNationalMonumentChangedPacket.cs` | SCNationalMonumentChangedPacket | ported | `SCNationalMonumentChangedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCNationalTaxRatePacket.cs` | SCNationalTaxRatePacket | ported | `SCNationalTaxRatePacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCNaviTeleportPacket.cs` | SCNaviTeleportPacket | ported | `SCNaviTeleportPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `SCNoticeMessagePacket.cs` | SCNoticeMessagePacket | ported | `SCNoticeMessagePacket` → `aaemu-game/src/systems/services/shutdown.rs` |
| `SCNotifyResurrectionPacket.cs` | SCNotifyResurrectionPacket | ported | `SCNotifyResurrectionPacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCNpcChatMessagePacket.cs` | SCNpcChatMessagePacket | ported | `SCNpcChatMessagePacket` → `aaemu-game/src/systems/npcs/npc/packets.rs` |
| `SCNpcInteractionSkillListPacket.cs` | SCNpcInteractionSkillListPacket | ported | `SCNpcInteractionSkillListPacket` → `aaemu-game/src/systems/npcs/npc/packets.rs` |
| `SCOffsets.cs` | SCOffsets | merged | `SCOffsets` merged into `aaemu-game/src/opcodes.rs` |
| `SCOnOffSnowPacket.cs` | SCOnOffSnowPacket | ported | `SCOnOffSnowPacket` → `aaemu-game/src/packets/world.rs` |
| `SCOneUnitMovementPacket.cs` | SCOneUnitMovementPacket | ported | `SCOneUnitMovementPacket` → `aaemu-game/src/packets/world.rs` |
| `SCOtherTradeItemPutupPacket.cs` | SCOtherTradeItemPutupPacket | ported | `SCOtherTradeItemPutupPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCOtherTradeItemTookdownPacket.cs` | SCOtherTradeItemTookdownPacket | ported | `SCOtherTradeItemTookdownPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCOtherTradeMoneyPutupPacket.cs` | SCOtherTradeMoneyPutupPacket | ported | `SCOtherTradeMoneyPutupPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCOverHeadMarkerSetPacket.cs` | SCOverHeadMarkerSetPacket | ported | `SCOverHeadMarkerSetPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCPauseUserMusicPacket.cs` | SCPauseUserMusicPacket | ported | `SCPauseUserMusicPacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCPlaySoundPacket.cs` | SCPlaySoundPacket | ported | `SCPlaySoundPacket` → `aaemu-game/src/systems/ai/packets.rs` |
| `SCPlaytimePacket.cs` | SCPlaytimePacket | ported | `SCPlaytimePacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCPlotCastingStoppedPacket.cs` | SCPlotCastingStoppedPacket | ported | `SCPlotCastingStoppedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCPlotChannelingStoppedPacket.cs` | SCPlotChannelingStoppedPacket | ported | `SCPlotChannelingStoppedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCPlotEndedPacket.cs` | SCPlotEndedPacket | ported | `SCPlotEndedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCPlotEventPacket.cs` | SCPlotEventPacket | ported | `SCPlotEventPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCPortalDeletedPacket.cs` | SCPortalDeletedPacket | ported | `SCPortalDeletedPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `SCPortalInfoSavedPacket.cs` | SCPortalInfoSavedPacket | ported | `SCPortalInfoSavedPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `SCPremiumPointChangedPacket.cs` | SCPremiumPointChangedPacket | ported | `SCPremiumPointChangedPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCPremiumServiceListPacket.cs` | SCPremiumServiceListPacket | ported | `SCPremiumServiceListPacket` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `SCPrepareLeaveWorldPacket.cs` | SCPrepareLeaveWorldPacket | ported | `SCPrepareLeaveWorldPacket` → `aaemu-game/src/packets/world.rs` |
| `SCProcessingInstancePacket.cs` | SCProcessingInstancePacket | ported | `SCProcessingInstancePacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCQuestContextCompletedPacket.cs` | SCQuestContextCompletedPacket | ported | `SCQuestContextCompletedPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `SCQuestContextResetPacket.cs` | SCQuestContextResetPacket | ported | `SCQuestContextResetPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `SCQuestContextStartedPacket.cs` | SCQuestContextStartedPacket | ported | `SCQuestContextStartedPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `SCQuestContextUpdatedPacket.cs` | SCQuestContextUpdatedPacket | ported | `SCQuestContextUpdatedPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `SCQuestRewardedByMailPacket.cs` | SCQuestRewardedByMailPacket | ported | `SCQuestRewardedByMailPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `SCQuestsPacket.cs` | SCQuestsPacket | ported | `SCQuestsPacket` → `aaemu-game/src/packets/world.rs` |
| `SCRaceCongestionPacket.cs` | SCRaceCongestionPacket | ported | `SCRaceCongestionPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCRankAlarmPacket.cs` | SCRankAlarmPacket | ported | `SCRankAlarmPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCRawPacket.cs` | SCRawPacket | ported | `SCRawPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCReconnectAuthPacket.cs` | SCReconnectAuthPacket | ported | `SCReconnectAuthPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCRecoverableExpPacket.cs` | SCRecoverableExpPacket | ported | `SCRecoverableExpPacket` → `aaemu-game/src/systems/skills/progress.rs` |
| `SCRefreshInCharacterListPacket.cs` | SCRefreshInCharacterListPacket | ported | `SCRefreshInCharacterListPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `SCRefreshTeamMemberPacket.cs` | SCRefreshTeamMemberPacket | ported | `SCRefreshTeamMemberPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCRejectedTeamPacket.cs` | SCRejectedTeamPacket | ported | `SCRejectedTeamPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCResponseCommonFarmListPacket.cs` | SCResponseCommonFarmListPacket | ported | `SCResponseCommonFarmListPacket` → `aaemu-game/src/systems/npcs/public_farm.rs` |
| `SCResponseUIDataPacket.cs` | SCResponseUIDataPacket | ported | `SCResponseUIDataPacket` → `aaemu-game/src/packets/world.rs` |
| `SCResultRestrictCheckPacket.cs` | SCResultRestrictCheckPacket | ported | `SCResultRestrictCheckPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `SCRulingClosedPacket.cs` | SCRulingClosedPacket | ported | `SCRulingClosedPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCRulingStatusPacket.cs` | SCRulingStatusPacket | ported | `SCRulingStatusPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCScheduleItemUpdatePacket.cs` | SCScheduleItemUpdatePacket | ported | `SCScheduleItemUpdatePacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCSchoolOfFishDoodadsPacket.cs` | SCSchoolOfFishDoodadsPacket | ported | `SCSchoolOfFishDoodadsPacket` → `aaemu-game/src/systems/npcs/fish_school.rs` |
| `SCSchoolOfFishFinderToggledPacket.cs` | SCSchoolOfFishFinderToggledPacket | ported | `SCSchoolOfFishFinderToggledPacket` → `aaemu-game/src/systems/npcs/fish_school.rs` |
| `SCSearchListPacket.cs` | SCSearchListPacket | ported | `SCSearchListPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCSendUserMusicPacket.cs` | SCSendUserMusicPacket | ported | `SCSendUserMusicPacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCSetBreathPacket.cs` | SCSetBreathPacket | ported | `SCSetBreathPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCShipyardStatePacket.cs` | SCShipyardStatePacket | ported | `SCShipyardStatePacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCShowCommonFarmPacket.cs` | SCShowCommonFarmPacket | ported | `SCShowCommonFarmPacket` → `aaemu-game/src/systems/npcs/public_farm.rs` |
| `SCSkillControllerStatePacket.cs` | SCSkillControllerStatePacket | ported | `SCSkillControllerStatePacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCSkillCooldownResetPacket.cs` | SCSkillCooldownResetPacket | ported | `SCSkillCooldownResetPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCSkillEndedPacket.cs` | SCSkillEndedPacket | ported | `SCSkillEndedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCSkillFiredPacket.cs` | SCSkillFiredPacket | ported | `SCSkillFiredPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCSkillLearnedPacket.cs` | SCSkillLearnedPacket | ported | `SCSkillLearnedPacket` → `aaemu-game/src/opcodes.rs` |
| `SCSkillStartedPacket.cs` | ExtraDataFlags, SCSkillStartedPacket | merged | `ExtraDataFlags` merged into `aaemu-game/src/systems/skills/packets.rs`; `SCSkillStartedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCSkillStopped.cs` | SCSkillStoppedPacket | ported | `SCSkillStoppedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCSkillUpgradedPacket.cs` | SCSkillUpgradedPacket | ported | `SCSkillUpgradedPacket` → `aaemu-game/src/opcodes.rs` |
| `SCSkillsResetPacket.cs` | SCSkillsResetPacket | ported | `SCSkillsResetPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCSlaveBoundPacket.cs` | SCSlaveBoundPacket | ported | `SCSlaveBoundPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `SCSlaveCreatedPacket.cs` | SCSlaveCreatedPacket | ported | `SCSlaveCreatedPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `SCSlaveDespawnPacket.cs` | SCSlaveDespawnPacket | ported | `SCSlaveDespawnPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `SCSlaveEquipmentChangedPacket.cs` | SCSlaveEquipmentChangedPacket | ported | `SCSlaveEquipmentChangedPacket` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `SCSlaveRemovedPacket.cs` | SCSlaveRemovedPacket | ported | `SCSlaveRemovedPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `SCSlaveStatePacket.cs` | SCSlaveStatePacket | ported | `SCSlaveStatePacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCSoldItemListPacket.cs` | SCSoldItemListPacket | ported | `SCSoldItemListPacket` → `aaemu-game/src/systems/economy/store.rs` |
| `SCSpamReportedPacket.cs` | SCSpamReportedPacket | ported | `SCSpamReportedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCSpecialtyCurrentPacket.cs` | SCSpecialtyCurrentPacket | ported | `SCSpecialtyCurrentPacket` → `aaemu-game/src/systems/economy/specialty.rs` |
| `SCSpecialtyRatioPacket.cs` | SCSpecialtyRatioPacket | ported | `SCSpecialtyRatioPacket` → `aaemu-game/src/systems/economy/specialty.rs` |
| `SCSummonDefendantPacket.cs` | SCSummonDefendantPacket | ported | `SCSummonDefendantPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCSummonJuryPacket.cs` | SCSummonJuryPacket | ported | `SCSummonJuryPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCSuspectGoingBotTrial.cs` | SCSuspectGoingBotTrial | ported | `SCSuspectGoingBotTrial` → `aaemu-game/src/opcodes.rs` |
| `SCSyncItemLifespanPacket.cs` | SCSyncItemLifespanPacket | ported | `SCSyncItemLifespanPacket` → `aaemu-game/src/systems/economy/item_packets.rs` |
| `SCTargetChangedPacket.cs` | SCTargetChangedPacket | ported | `SCTargetChangedPacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCTeamAckRiskyActionPacket.cs` | SCTeamAckRiskyActionPacket | ported | `SCTeamAckRiskyActionPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCTeamAreaInvitedPacket.cs` | SCTeamAreaInvitedPacket | ported | `SCTeamAreaInvitedPacket` → `aaemu-game/src/opcodes.rs` |
| `SCTeamBecameRaidTeamPacket.cs` | SCTeamBecameRaidTeamPacket | ported | `SCTeamBecameRaidTeamPacket` → `aaemu-game/src/opcodes.rs` |
| `SCTeamDismissedPacket.cs` | SCTeamDismissedPacket | ported | `SCTeamDismissedPacket` → `aaemu-game/src/opcodes.rs` |
| `SCTeamLootingRuleChangedPacket.cs` | SCTeamLootingRuleChangedPacket | ported | `SCTeamLootingRuleChangedPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCTeamMemberDisconnectedPacket.cs` | SCTeamMemberDisconnectedPacket | ported | `SCTeamMemberDisconnectedPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCTeamMemberJoinedPacket.cs` | SCTeamMemberJoinedPacket | ported | `SCTeamMemberJoinedPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCTeamMemberLeavedPacket.cs` | SCTeamMemberLeavedPacket | ported | `SCTeamMemberLeavedPacket` → `aaemu-game/src/opcodes.rs` |
| `SCTeamMemberMovedPacket.cs` | SCTeamMemberMovedPacket | ported | `SCTeamMemberMovedPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCTeamMemberRoleChangedPacket.cs` | SCTeamMemberRoleChangedPacket | ported | `SCTeamMemberRoleChangedPacket` → `aaemu-game/src/opcodes.rs` |
| `SCTeamOwnerChangedPacket.cs` | SCTeamOwnerChangedPacket | ported | `SCTeamOwnerChangedPacket` → `aaemu-game/src/opcodes.rs` |
| `SCTeamPingPosPacket.cs` | SCTeamPingPosPacket | ported | `SCTeamPingPosPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCTeamRemoteMembersExPacket.cs` | SCTeamRemoteMembersExPacket | ported | `SCTeamRemoteMembersExPacket` → `aaemu-game/src/systems/social/packets/team.rs` |
| `SCTeleportUnitPacket.cs` | SCTeleportUnitPacket | ported | `SCTeleportUnitPacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCTelescopeToggledPacket.cs` | SCTelescopeToggledPacket | ported | `SCTelescopeToggledPacket` → `aaemu-game/src/systems/services/radar.rs` |
| `SCTelescopeUnitsPacket.cs` | SCTelescopeUnitsPacket | ported | `SCTelescopeUnitsPacket` → `aaemu-game/src/systems/services/radar.rs` |
| `SCTimeOfDayPacket.cs` | SCTimeOfDayPacket | ported | `SCTimeOfDayPacket` → `aaemu-game/src/packets/world.rs` |
| `SCToggleBeautyshopResponsePacket.cs` | SCToggleBeautyshopResponsePacket | ported | `SCToggleBeautyshopResponsePacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCTowerDefEndPacket.cs` | SCTowerDefEndPacket | ported | `SCTowerDefEndPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCTowerDefListPacket.cs` | SCTowerDefListPacket | ported | `SCTowerDefListPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCTowerDefStartPacket.cs` | SCTowerDefStartPacket | ported | `SCTowerDefStartPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCTowerDefWaveStartPacket.cs` | SCTowerDefWaveStartPacket | ported | `SCTowerDefWaveStartPacket` → `aaemu-game/src/systems/instances/packets.rs` |
| `SCTradeCanceledPacket.cs` | SCTradeCanceledPacket | ported | `SCTradeCanceledPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCTradeItemPutupPacket.cs` | SCTradeItemPutupPacket | ported | `SCTradeItemPutupPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCTradeItemTookdownPacket.cs` | SCTradeItemTookdownPacket | ported | `SCTradeItemTookdownPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCTradeLockUpdatePacket.cs` | SCTradeLockUpdatePacket | ported | `SCTradeLockUpdatePacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCTradeMadePacket.cs` | SCTradeMadePacket | ported | `SCTradeMadePacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCTradeMoneyPutupPacket.cs` | SCTradeMoneyPutupPacket | ported | `SCTradeMoneyPutupPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCTradeOkUpdatePacket.cs` | SCTradeOkUpdatePacket | ported | `SCTradeOkUpdatePacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCTradeStartedPacket.cs` | SCTradeStartedPacket | ported | `SCTradeStartedPacket` → `aaemu-game/src/systems/economy/trade.rs` |
| `SCTransferTelescopeToggledPacket.cs` | SCTransferTelescopeToggledPacket | ported | `SCTransferTelescopeToggledPacket` → `aaemu-game/src/systems/services/radar.rs` |
| `SCTransferTelescopeUnitsPacket.cs` | SCTransferTelescopeUnitsPacket | ported | `SCTransferTelescopeUnitsPacket` → `aaemu-game/src/systems/services/radar.rs` |
| `SCTrialAudienceJoinedPacket.cs` | SCTrialAudienceJoinedPacket | ported | `SCTrialAudienceJoinedPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCTrialAudienceLeftPacket.cs` | SCTrialAudienceLeftPacket | ported | `SCTrialAudienceLeftPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCTrialCanceledPacket.cs` | SCTrialCanceledPacket | ported | `SCTrialCanceledPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCTrialInfoPacket.cs` | SCTrialInfoPacket | ported | `SCTrialInfoPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCTrialWaitStatusPacket.cs` | SCTrialWaitStatusPacket | ported | `SCTrialWaitStatusPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCTrionConfigPacket.cs` | SCTrionConfigPacket | ported | `SCTrionConfigPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCTutorialSavedPacket.cs` | SCTutorialSavedPacket | ported | `SCTutorialSavedPacket` → `aaemu-game/src/systems/quests/packets.rs` |
| `SCUnbondDoodadPacket.cs` | SCUnbondDoodadPacket | ported | `SCUnbondDoodadPacket` → `aaemu-game/src/systems/npcs/doodad/packets.rs` |
| `SCUnderWaterPacket.cs` | SCUnderWaterPacket | ported | `SCUnderWaterPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCUnhungPacket.cs` | SCUnhungPacket | ported | `SCUnhungPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `SCUnitAttachedPacket.cs` | SCUnitAttachedPacket | ported | `SCUnitAttachedPacket` → `aaemu-game/src/packets/world_objects.rs` |
| `SCUnitBountyMoneyPacket.cs` | SCUnitBountyMoneyPacket | ported | `SCUnitBountyMoneyPacket` → `aaemu-game/src/systems/social/packets/trial.rs` |
| `SCUnitDamagedPacket.cs` | SCUnitDamagedPacket | ported | `SCUnitDamagedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCUnitDeathPacket.cs` | SCUnitDeathPacket | ported | `SCUnitDeathPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCUnitDetachedPacket.cs` | SCUnitDetachedPacket | ported | `SCUnitDetachedPacket` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `SCUnitEquipmentsChangedPacket.cs` | SCUnitEquipmentsChangedPacket | ported | `SCUnitEquipmentsChangedPacket` → `aaemu-game/src/systems/economy/item_packets.rs` |
| `SCUnitExpeditionChangedPacket.cs` | SCUnitExpeditionChangedPacket | ported | `SCUnitExpeditionChangedPacket` → `aaemu-game/src/systems/social/packets/expedition.rs` |
| `SCUnitFactionChangedPacket.cs` | SCUnitFactionChangedPacket | ported | `SCUnitFactionChangedPacket` → `aaemu-game/src/systems/social/packets/faction.rs` |
| `SCUnitFlyingStateChangedPacket.cs` | SCUnitFlyingStateChangedPacket | ported | `SCUnitFlyingStateChangedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCUnitGmModeChangedPacket.cs` | SCUnitGmModeChangedPacket | ported | `SCUnitGmModeChangedPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCUnitHealedPacket.cs` | SCUnitHealedPacket | ported | `SCUnitHealedPacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCUnitIdleStatusPacket.cs` | SCUnitIdleStatusPacket | ported | `SCUnitIdleStatusPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `SCUnitImpulsePacket.cs` | SCUnitImpulsePacket | ported | `SCUnitImpulsePacket` → `aaemu-game/src/systems/skills/packets_special.rs` |
| `SCUnitInvisiblePacket.cs` | SCUnitInvisiblePacket | ported | `SCUnitInvisiblePacket` → `aaemu-game/src/systems/skills/packets.rs` |
| `SCUnitLootingStatePacket.cs` | SCUnitLootingStatePacket | ported | `SCUnitLootingStatePacket` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `SCUnitModelPostureChangedPacket.cs` | SCUnitModelPostureChangedPacket | ported | `SCUnitModelPostureChangedPacket` → `aaemu-game/src/systems/npcs/npc/packets.rs` |
| `SCUnitMovementsPacket.cs` | SCUnitMovementsPacket | ported | `SCUnitMovementsPacket` → `aaemu-game/src/opcodes.rs` |
| `SCUnitNameChangedPacket.cs` | SCUnitNameChangedPacket | ported | `SCUnitNameChangedPacket` → `aaemu-game/src/systems/social/packets/misc.rs` |
| `SCUnitOfflinePacket.cs` | SCUnitOfflinePacket | ported | `SCUnitOfflinePacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCUnitPointsPacket.cs` | SCUnitPointsPacket | ported | `SCUnitPointsPacket` → `aaemu-game/src/packets/world.rs` |
| `SCUnitPortalUsedPacket.cs` | SCUnitPortalUsedPacket | ported | `SCUnitPortalUsedPacket` → `aaemu-game/src/systems/npcs/portal/packets.rs` |
| `SCUnitPvPPointsChangedPacket.cs` | SCUnitPvPPointsChangedPacket | ported | `SCUnitPvPPointsChangedPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCUnitStatePacket.cs` | SCUnitStatePacket | ported | `SCUnitStatePacket` → `aaemu-game/src/world/unit_state.rs` |
| `SCUnitVisualOptionsPacket.cs` | SCUnitVisualOptionsPacket | ported | `SCUnitVisualOptionsPacket` → `aaemu-game/src/systems/services/character_packets.rs` |
| `SCUnitsRemovedPacket.cs` | SCUnitsRemovedPacket | ported | `SCUnitsRemovedPacket` → `aaemu-game/src/packets/world.rs` |
| `SCUnknownPacket.cs` | SCUnknownPacket | ported | `SCUnknownPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCUnlockCurrencySlotPacket.cs` | SCUnlockCurrencySlotPacket | ported | `SCUnlockCurrencySlotPacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCUpdatePremiumPointPacket.cs` | SCUpdatePremiumPointPacket | ported | `SCUpdatePremiumPointPacket` → `aaemu-game/src/packets/g2c.rs` |
| `SCUserNotesLoadedPacket.cs` | SCUserNotesLoadedPacket | merged | `SCUserNotesLoadedPacket` merged into `aaemu-game/src/systems/services/music.rs` |
| `SCVegetationCutdowningPacket.cs` | SCVegetationCutdowningPacket | ported | `SCVegetationCutdowningPacket` → `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `SCWorldMessagePacket.cs` | SCWorldMessagePacket | ported | `SCWorldMessagePacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SCWorldQueuePacket.cs` | SCWorldQueuePacket | ported | `SCWorldQueuePacket` → `aaemu-game/src/packets/g2c_misc.rs` |
| `X2EnterWorldResponsePacket.cs` | X2EnterWorldResponsePacket | ported | `X2EnterWorldResponsePacket` → `aaemu-game/src/packets/g2c.rs` |

### `AAEmu.Game/Core/Packets/G2L`

| File | Types | Status | Rust |
|---|---|---|---|
| `GLGameServerLoadPacket.cs` | GLGameServerLoadPacket | ported | `GLGameServerLoadPacket` → `aaemu-game/src/login/packets.rs` |
| `GLOffsets.cs` | GLOffsets | merged | `GLOffsets` merged into `aaemu-game/src/opcodes.rs` |
| `GLPlayerEnterPacket.cs` | GLPlayerEnterPacket | ported | `GLPlayerEnterPacket` → `aaemu-game/src/login/packets.rs` |
| `GLPlayerReconnectPacket.cs` | GLPlayerReconnectPacket | ported | `GLPlayerReconnectPacket` → `aaemu-game/src/login/packets.rs` |
| `GLRegisterGameServerPacket.cs` | GLRegisterGameServerPacket | ported | `GLRegisterGameServerPacket` → `aaemu-game/src/login/packets.rs` |
| `GLRequestInfoPacket.cs` | GLRequestInfoPacket | ported | `GLRequestInfoPacket` → `aaemu-game/src/login/packets.rs` |

### `AAEmu.Game/Core/Packets/L2G`

| File | Types | Status | Rust |
|---|---|---|---|
| `LGOffsets.cs` | LGOffsets | merged | `LGOffsets` merged into `aaemu-game/src/opcodes.rs` |
| `LGPlayerEnterPacket.cs` | LGPlayerEnterPacket | ported | `LGPlayerEnterPacket` → `aaemu-game/src/opcodes.rs` |
| `LGPlayerReconnectPacket.cs` | LGPlayerReconnectPacket | ported | `LGPlayerReconnectPacket` → `aaemu-game/src/opcodes.rs` |
| `LGRegisterGameServerPacket.cs` | LGRegisterGameServerPacket | ported | `LGRegisterGameServerPacket` → `aaemu-game/src/opcodes.rs` |
| `LGRequestInfoPacket.cs` | LGRequestInfoPacket | ported | `LGRequestInfoPacket` → `aaemu-game/src/opcodes.rs` |

### `AAEmu.Game/Core/Packets/Proxy`

| File | Types | Status | Rust |
|---|---|---|---|
| `BeginBindObjPacket.cs` | BeginBindObjPacket | ported | `BeginBindObjPacket` → `aaemu-game/src/opcodes.rs` |
| `BeginUpdateObjPacket.cs` | BeginUpdateObjPacket | ported | `BeginUpdateObjPacket` → `aaemu-game/src/opcodes.rs` |
| `ChangeCVarPacket.cs` | ChangeCVarPacket | ported | `ChangeCVarPacket` → `aaemu-game/src/opcodes.rs` |
| `ChangeStatePacket.cs` | ChangeStatePacket | ported | `ChangeStatePacket` → `aaemu-game/src/packets/proxy.rs` |
| `EndBindObjPacket.cs` | EndBindObjPacket | ported | `EndBindObjPacket` → `aaemu-game/src/opcodes.rs` |
| `EndUpdateObjPacket.cs` | EndUpdateObjPacket | ported | `EndUpdateObjPacket` → `aaemu-game/src/opcodes.rs` |
| `EntityClassRegistrationPacket.cs` | EntityClassRegistrationPacket | ported | `EntityClassRegistrationPacket` → `aaemu-game/src/opcodes.rs` |
| `FastPingPacket.cs` | FastPingPacket | ported | `FastPingPacket` → `aaemu-game/src/packets/proxy.rs` |
| `FastPongPacket.cs` | FastPongPacket | ported | `FastPongPacket` → `aaemu-game/src/packets/proxy.rs` |
| `FinishStatePacket.cs` | FinishStatePacket | ported | `FinishStatePacket` → `aaemu-game/src/packets/proxy.rs` |
| `FlushMsgsPacket.cs` | FlushMsgsPacket | ported | `FlushMsgsPacket` → `aaemu-game/src/opcodes.rs` |
| `PPOffsets.cs` | PPOffsets | merged | `PPOffsets` merged into `aaemu-game/src/opcodes.rs` |
| `PacketSeqChange.cs` | PacketSeqChange | ported | `PacketSeqChange` → `aaemu-game/src/opcodes.rs` |
| `PartialAspectPacket.cs` | PartialAspectPacket | ported | `PartialAspectPacket` → `aaemu-game/src/opcodes.rs` |
| `PingPacket.cs` | PingPacket | ported | `PingPacket` → `aaemu-game/src/packets/proxy.rs` |
| `PongPacket.cs` | PongPacket | ported | `PongPacket` → `aaemu-game/src/packets/proxy.rs` |
| `RemoveStaticObjPacket.cs` | RemoveStaticObjPacket | ported | `RemoveStaticObjPacket` → `aaemu-game/src/opcodes.rs` |
| `SetAspectProfilePacket.cs` | SetAspectProfilePacket | ported | `SetAspectProfilePacket` → `aaemu-game/src/opcodes.rs` |
| `SetGameTypePacket.cs` | SetGameTypePacket | ported | `SetGameTypePacket` → `aaemu-game/src/packets/proxy.rs` |
| `UnbindPredictedObjPacket.cs` | UnbindPredictedObjPacket | ported | `UnbindPredictedObjPacket` → `aaemu-game/src/opcodes.rs` |
| `UpdateAspectPacket.cs` | UpdateAspectPacket | ported | `UpdateAspectPacket` → `aaemu-game/src/opcodes.rs` |
| `UpdatePhysicsTimePacket.cs` | UpdatePhysicsTimePacket | ported | `UpdatePhysicsTimePacket` → `aaemu-game/src/opcodes.rs` |
| `VoiceDataPacket.cs` | VoiceDataPacket | ported | `VoiceDataPacket` → `aaemu-game/src/opcodes.rs` |

### `AAEmu.Game/Core/Packets/S2C`

| File | Types | Status | Rust |
|---|---|---|---|
| `TCCharNameQueriedPacket.cs` | TCCharNameQueriedPacket | ported | `TCCharNameQueriedPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCDoodadIdsPacket.cs` | TCDoodadIdsPacket | ported | `TCDoodadIdsPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCDoodadStreamPacket.cs` | TCDoodadStreamPacket | ported | `TCDoodadStreamPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCDownloadEmblemPacket.cs` | TCDownloadEmblemPacket | ported | `TCDownloadEmblemPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCEmblemStreamDownloadPacket.cs` | TCEmblemStreamDownloadPacket | ported | `TCEmblemStreamDownloadPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCEmblemStreamRecvStatusPacket.cs` | TCEmblemStreamRecvStatusPacket, EmblemStreamStatus | ported | `TCEmblemStreamRecvStatusPacket` → `aaemu-game/src/stream/s2c.rs`; `EmblemStreamStatus` → `aaemu-game/src/stream/s2c.rs` |
| `TCEmblemStreamSendStatusPacket.cs` | TCEmblemStreamSendStatusPacket | ported | `TCEmblemStreamSendStatusPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCHouseFarmPacket.cs` | TCHouseFarmPacket | ported | `TCHouseFarmPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCItemUccDataPacket.cs` | TCItemUccDataPacket | ported | `TCItemUccDataPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCJoinResponsePacket.cs` | TCJoinResponsePacket | ported | `TCJoinResponsePacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCOffsets.cs` | TCOffsets | merged | `TCOffsets` merged into `aaemu-game/src/stream/opcodes.rs` |
| `TCUccCharNamePacket.cs` | TCUccCharNamePacket | ported | `TCUccCharNamePacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCUccComplexCheckValidPacket.cs` | TCUccComplexCheckValidPacket | ported | `TCUccComplexCheckValidPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCUccComplexPacket.cs` | TCUccComplexPacket | ported | `TCUccComplexPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCUccPositionPacket.cs` | TCUccPositionPacket | ported | `TCUccPositionPacket` → `aaemu-game/src/stream/s2c.rs` |
| `TCUccStringPacket.cs` | TCUccStringPacket | ported | `TCUccStringPacket` → `aaemu-game/src/stream/s2c.rs` |

### `AAEmu.Game/GameData`

| File | Types | Status | Rust |
|---|---|---|---|
| `AchievementGameData.cs` | AchievementGameData | dropped | `AchievementGameData` dropped: loads private dictionaries, no getters / no outside references |
| `AiGameData.cs` | AiGameData | merged | `AiGameData` merged into `aaemu-game/src/systems/ai/data.rs` |
| `BattlefieldGameData.cs` | BattlefieldGameData | merged | `BattlefieldGameData` merged into `aaemu-game/src/systems/instances/data.rs` |
| `BuffGameData.cs` | BuffGameData | ported | `BuffGameData` → `aaemu-game/src/systems/skills/data/misc.rs` |
| `CommonFarmGameData.cs` | CommonFarmGameData | merged | `CommonFarmGameData` merged into `aaemu-game/src/systems/npcs/public_farm.rs` |
| `DamageModifiersGameData .cs` | DamageModifierGameData | dropped | `DamageModifierGameData` dropped: GetModifiersForBuff has zero callers |
| `FishDetailsGameData.cs` | FishDetailsGameData | merged | `FishDetailsGameData` merged into `aaemu-game/src/systems/economy/items_data.rs` |
| `GimmickGameData.cs` | GimmickGameData | merged | `GimmickGameData` merged into `aaemu-game/src/systems/npcs/gimmick/mod.rs` |
| `HousingGameData.cs` | HousingGameData | merged | `HousingGameData` merged into `aaemu-game/src/systems/housing/data.rs` |
| `IndunGameData.cs` | IndunGameData | merged | `IndunGameData` merged into `aaemu-game/src/systems/instances/data.rs` |
| `ItemConversionGameData.cs` | ItemConversionGameData | merged | `ItemConversionGameData` merged into `aaemu-game/src/systems/economy/items_data.rs` |
| `ItemGameData.cs` | ItemGameData | merged | `ItemGameData` merged into `aaemu-game/src/systems/economy/items_data.rs` |
| `LootGameData.cs` | LootGameData | merged | `LootGameData` merged into `aaemu-game/src/systems/economy/loot_data.rs` |
| `MateGameData.cs` | MateGameData | merged | `MateGameData` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `NpcGameData.cs` | NpcGameData | ported | `NpcGameData` → `aaemu-game/src/systems/npcs/npc/manager.rs` |
| `NpcGroupGameData.cs` | NpcGroupGameData | dropped | `NpcGroupGameData` dropped: loaded by reflection loader, getters never called |
| `SchedulesGameData.cs` | SchedulesGameData | merged | `SchedulesGameData` merged into `aaemu-game/src/systems/npcs/schedule.rs` |
| `SlaveGameData.cs` | SlaveGameData | merged | `SlaveGameData` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `SphereGameData.cs` | SphereGameData | merged | `SphereGameData` merged into `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `TagsGameData.cs` | TagsGameData, TagType | merged | `TagsGameData` merged into `aaemu-game/src/systems/skills/data/misc.rs`; `TagType` merged: systems/skills/data/mod.rs tag lookups |
| `TowerDefGameData.cs` | TowerDefGameData | merged | `TowerDefGameData` merged into `aaemu-game/src/systems/instances/data.rs` |
| `TransferGameData.cs` | TransferGameData | merged | `TransferGameData` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `UnitRequirementsGameData.cs` | UnitRequirementsGameData | merged | `UnitRequirementsGameData` merged into `aaemu-game/src/systems/skills/unit_reqs.rs` |

### `AAEmu.Game/GameData/Framework`

| File | Types | Status | Rust |
|---|---|---|---|
| `GameDataAttribute.cs` | GameDataAttribute | merged | `GameDataAttribute` merged: data/mod.rs explicit loads (reflection/DI) |
| `GameDataManager.cs` | GameDataManager | merged | `GameDataManager` merged: data/mod.rs explicit loads (reflection/DI) |
| `IGameDataLoader.cs` | IGameDataLoader | merged | `IGameDataLoader` merged: data/mod.rs explicit loads (reflection/DI) |
| `IGameDataManager.cs` | IGameDataManager | merged | `IGameDataManager` merged: data/mod.rs explicit loads (reflection/DI) |

### `AAEmu.Game/IO`

| File | Types | Status | Rust |
|---|---|---|---|
| `ClientDataConfig.cs` | ClientDataConfig | ported | `ClientDataConfig` → `aaemu-commons/src/client_files.rs` |
| `ClientFileManager.cs` | ClientFileManager | ported | `ClientFileManager` → `aaemu-commons/src/client_files.rs` |
| `ClientSource.cs` | ClientSource | ported | `ClientSource` → `aaemu-game/src/data/client_files.rs` |
| `ClientSourceType.cs` | ClientSourceType | ported | `ClientSourceType` → `aaemu-game/src/data/client_files.rs` |

### `AAEmu.Game/Models`

| File | Types | Status | Rust |
|---|---|---|---|
| `AccountPayment.cs` | AccountPayment, PaymentMethodType | ported | `AccountPayment` → `aaemu-game/src/net/connection.rs`; `PaymentMethodType` → `aaemu-game/src/net/connection.rs` |
| `AppConfiguration.cs` | AppConfiguration, NetworkConfig, DBConnections | merged | `AppConfiguration` merged into `aaemu-game/src/config.rs`; `NetworkConfig` → `aaemu-game/src/config.rs`; `DBConnections` → `config.rs` |
| `Unsubscriber.cs` | Unsubscriber | merged | `Unsubscriber` merged into `aaemu-game/src/managers/time.rs` |

### `AAEmu.Game/Models/Account`

| File | Types | Status | Rust |
|---|---|---|---|
| `AccountDetails.cs` | AccountDetails | ported | `AccountDetails` → `aaemu-game/src/db.rs` |

### `AAEmu.Game/Models/ClientData`

| File | Types | Status | Rust |
|---|---|---|---|
| `AABB.cs` | AABB | merged | `AABB` merged into `aaemu-game/src/systems/npcs/heightmap.rs` |
| `Hmap.cs` | Hmap | ported | `Hmap` → `aaemu-game/src/systems/npcs/heightmap.rs` |
| `NodeCell.cs` | NodeCell | ported | `NodeCell` → `aaemu-game/src/systems/npcs/heightmap.rs` |
| `VersionCalc.cs` | VersionCalc | dropped | `VersionCalc` dropped: unused |

### `AAEmu.Game/Models/CryEngine`

| File | Types | Status | Rust |
|---|---|---|---|
| `AreaMissionBai.cs` | AreaMissionBai, AreaMissionNode | dropped | `AreaMissionBai` dropped: no caller in the C# server (export/tooling); `AreaMissionNode` dropped: no caller in the C# server (export/tooling) |
| `AreaMissionType.cs` | AreaMissionType | dropped | `AreaMissionType` dropped: no caller in the C# server (export/tooling) |
| `CgfConverterStructsShim.cs` | Matrix3x4 | merged | `Matrix3x4` merged: systems/ai/physics/objects_file.rs (size-skipped, values unused) |
| `NetMissionBai.cs` | NetMissionBai, NetMissionNodeNavType, NetMissionNode, NetMissionEdge | dropped | `NetMissionBai` dropped: no caller in the C# server (export/tooling); `NetMissionNodeNavType` dropped: no caller in the C# server (export/tooling); `NetMissionNode` dropped: no caller in the C# server (export/tooling); `NetMissionEdge` dropped: no caller in the C# server (export/tooling) |
| `VertexMissionBai.cs` | VertexMissionBai, VertexMissionNode | dropped | `VertexMissionBai` dropped: no caller in the C# server (export/tooling); `VertexMissionNode` dropped: no caller in the C# server (export/tooling) |

### `AAEmu.Game/Models/CryEngine/Entities`

| File | Types | Status | Rust |
|---|---|---|---|
| `BBox.cs` | BBox | merged | `BBox` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `FlightLinkDescriptor.cs` | FlightLinkDescriptor | dropped | `FlightLinkDescriptor` dropped: no caller in the C# server (export/tooling) |
| `LinkDescriptor.cs` | LinkDescriptor | ported | `LinkDescriptor` → `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `LinkRecord.cs` | LinkRecord | dropped | `LinkRecord` dropped: no caller in the C# server (export/tooling) |
| `Navigation.cs` | Navigation | merged | `Navigation` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `NodeDescriptor.cs` | NodeDescriptor | ported | `NodeDescriptor` → `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `ObstacleDataDescriptor.cs` | ObstacleDataDescriptor | ported | `ObstacleDataDescriptor` → `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `Road.cs` | Road | merged | `Road` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `RoadNode.cs` | RoadNode | dropped | `RoadNode` dropped: no caller in the C# server (export/tooling) |
| `Spans.cs` | Spans | merged | `Spans` merged into `aaemu-game/src/systems/ai/physics/jitter.rs` |
| `WaypointSurfaceNavigation.cs` | WaypointSurfaceNavigation | dropped | `WaypointSurfaceNavigation` dropped: no caller in the C# server (export/tooling) |

### `AAEmu.Game/Models/CryEngine/Loaders`

| File | Types | Status | Rust |
|---|---|---|---|
| `BaseBaiLoader.cs` | BaseBaiLoader | ported | `BaseBaiLoader` → `aaemu-game/src/systems/ai/geodata/bai.rs` |

### `AAEmu.Game/Models/CryEngine/Mission`

| File | Types | Status | Rust |
|---|---|---|---|
| `AiLightLevel.cs` | AiLightLevel | merged | `AiLightLevel` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `AiShape.cs` | AiShape | ported | `AiShape` → `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `FlightNavRegion.cs` | FlightNavRegion | dropped | `FlightNavRegion` dropped: no caller in the C# server (export/tooling) |
| `ForbiddenAreasType.cs` | ForbiddenAreasType | merged | `ForbiddenAreasType` merged: systems/ai/geodata/bai.rs ForbiddenKind |
| `Mission.cs` | Mission | merged | `Mission` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `MissionType.cs` | MissionType | merged | `MissionType` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `NavigationType.cs` | NavigationType | merged | `NavigationType` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `PolygonArea.cs` | PolygonArea | ported | `PolygonArea` → `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `SpecialArea.cs` | SpecialArea | ported | `SpecialArea` → `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `WaypointConnections.cs` | WaypointConnections | merged | `WaypointConnections` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |

### `AAEmu.Game/Models/CryEngine/Objects`

| File | Types | Status | Rust |
|---|---|---|---|
| `AssetPath.cs` | AssetPath | merged | `AssetPath` merged: systems/ai/physics/objects_file.rs (size-skipped, values unused) |
| `MaterialsFile.cs` | MaterialsFile | dropped | `MaterialsFile` dropped: no caller in the C# server (export/tooling) |
| `MaterialsListFile.cs` | MaterialsListFile | dropped | `MaterialsListFile` dropped: no caller in the C# server (export/tooling) |
| `ObjectDataBase.cs` | ObjectDataBase | merged | `ObjectDataBase` merged into `aaemu-game/src/systems/ai/physics/objects_file.rs` |
| `ObjectDataType.cs` | ObjectDataType | merged | `ObjectDataType` merged into `aaemu-game/src/systems/ai/physics/objects_file.rs` |
| `ObjectDataType11Water.cs` | ObjectDataType11Water | merged | `ObjectDataType11Water` merged into `aaemu-game/src/systems/ai/physics/objects_file.rs` |
| `ObjectDataType13Road.cs` | ObjectDataType13Road | merged | `ObjectDataType13Road` merged: systems/ai/physics/objects_file.rs (size-skipped, values unused) |
| `ObjectDataType1Brush.cs` | ObjectDataType1Brush | merged | `ObjectDataType1Brush` merged: systems/ai/physics/objects_file.rs (size-skipped, values unused) |
| `ObjectDataType6Voxel.cs` | ObjectDataType6Voxel | merged | `ObjectDataType6Voxel` merged into `aaemu-game/src/systems/ai/physics/objects_file.rs` |
| `ObjectsFile.cs` | ObjectsFile | ported | `ObjectsFile` → `aaemu-game/src/systems/ai/physics/objects_file.rs` |
| `VoxelMeshProcessor.cs` | VoxelMeshProcessor | dropped | `VoxelMeshProcessor` dropped: no caller in the C# server (export/tooling) |
| `VoxelMeshReader.cs` | VoxelMeshReader, VoxelChunkEntry | dropped | `VoxelMeshReader` dropped: no caller in the C# server (export/tooling); `VoxelChunkEntry` dropped: no caller in the C# server (export/tooling) |
| `WaterObjectVolumeType.cs` | WaterObjectVolumeType | merged | `WaterObjectVolumeType` merged into `aaemu-game/src/systems/ai/physics/objects_file.rs` |

### `AAEmu.Game/Models/CryEngine/Readers`

| File | Types | Status | Rust |
|---|---|---|---|
| `AreasMissionReader.cs` | AreasMissionReader | merged | `AreasMissionReader` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `BaiReader.cs` | BaiReader | merged | `BaiReader` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `FlightMissionReader.cs` | FlightMissionReader | dropped | `FlightMissionReader` dropped: no caller in the C# server (export/tooling) |
| `NetMissionReader.cs` | NetMissionReader | merged | `NetMissionReader` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `RoadMissionReader.cs` | RoadMissionReader | dropped | `RoadMissionReader` dropped: no caller in the C# server (export/tooling) |
| `VertexMissionReader.cs` | VertexMissionReader | merged | `VertexMissionReader` merged into `aaemu-game/src/systems/ai/geodata/bai.rs` |
| `WaypointSurfaceNavigationReader.cs` | WaypointSurfaceNavigationReader | dropped | `WaypointSurfaceNavigationReader` dropped: no caller in the C# server (export/tooling) |

### `AAEmu.Game/Models/Game`

| File | Types | Status | Rust |
|---|---|---|---|
| `Blocked.cs` | Blocked | merged | `Blocked` merged into `aaemu-game/src/models/char_data.rs` |
| `Configurations.cs` | WindModelType, SeaWeatherModelType, Configurations, WorldConfig … | ported | `WindModelType` → `aaemu-game/src/config.rs`; `SeaWeatherModelType` → `aaemu-game/src/config.rs`; `Configurations` merged into `aaemu-game/src/config.rs`; `WorldConfig` → `aaemu-game/src/config.rs`; `DungeonLoadConfig` → `aaemu-game/src/systems/instances/config.rs`; `DungeonsConfig` → `aaemu-game/src/systems/instances/config.rs`; `AccountDeleteDelayTiming` → `aaemu-game/src/config.rs`; `AccountConfig` → `aaemu-game/src/config.rs`; `CurrencyValuesConfig` → `aaemu-game/src/config.rs`; `SpecialtyConfig` → `aaemu-game/src/systems/npcs/specialty.rs`; `ScriptsConfig` → `aaemu-game/src/config.rs`; `LoadStrategyType` → `aaemu-game/src/config.rs` |
| `DominionData.cs` | DominionData, DominionTerritoryData, DominionSiegeTimers, DominionUnkData | ported | `DominionData` → `aaemu-game/src/systems/social/dominion.rs`; `DominionTerritoryData` → `aaemu-game/src/systems/social/dominion.rs`; `DominionSiegeTimers` → `aaemu-game/src/systems/social/dominion.rs`; `DominionUnkData` → `aaemu-game/src/systems/social/dominion.rs` |
| `ErrorMessageType.cs` | ErrorMessageType | merged | `ErrorMessageType` merged into `aaemu-game/src/systems/economy/error_message.rs` |
| `ExperienceLevelTemplate.cs` | ExperienceLevelTemplate | merged | `ExperienceLevelTemplate` merged into `aaemu-game/src/data/experience.rs` |
| `Family.cs` | Family, FamilyMember | ported | `Family` → `aaemu-game/src/systems/social/family.rs`; `FamilyMember` → `aaemu-game/src/systems/social/family.rs` |
| `Friend.cs` | Friend | ported | `Friend` → `aaemu-game/src/systems/social/mod.rs` |
| `ICommand.cs` | ICommand | ported | `ICommand` → `aaemu-game/src/systems/services/commands/mod.rs` |
| `JusticeConfig.cs` | JusticeConfig, CourtRoomConfig, JailConfig | ported | `JusticeConfig` → `aaemu-game/src/systems/social/config.rs`; `CourtRoomConfig` → `aaemu-game/src/systems/social/config.rs`; `JailConfig` → `aaemu-game/src/systems/social/config.rs` |
| `Portal.cs` | Portal, VisitedDistrict, DistrictReturnPoints | ported | `Portal` → `aaemu-game/src/data/world_data.rs`; `VisitedDistrict` → `aaemu-game/src/models/char_data.rs`; `DistrictReturnPoints` merged into `aaemu-game/src/data/world_data.rs` |
| `ScheduleItem.cs` | ScheduleItem | ported | `ScheduleItem` → `aaemu-game/src/packets/g2c_misc.rs` |
| `SlaveEquipment.cs` | SlaveEquipment | merged | `SlaveEquipment` merged into `aaemu-game/src/systems/economy/auction.rs` |
| `TelescopeRegistrationEntry.cs` | TelescopeRegistrationEntry | ported | `TelescopeRegistrationEntry` → `aaemu-game/src/systems/services/radar.rs` |

### `AAEmu.Game/Models/Game/AI/AStar`

| File | Types | Status | Rust |
|---|---|---|---|
| `AiNavigation.cs` | AiNavigation | dropped | `AiNavigation` dropped: unused (only private uncalled FindClosest) |
| `AreasMission.cs` | AreasMission | dropped | `AreasMission` dropped: unused (only private uncalled FindClosest) |
| `AreasMissionPoints.cs` | AreasMissionPoints | dropped | `AreasMissionPoints` dropped: unused (only private uncalled FindClosest) |
| `PathNode.cs` | PathNode | ported | `PathNode` → `aaemu-game/src/systems/ai/geodata/astar.rs` |

### `AAEmu.Game/Models/Game/AI/Enums`

| File | Types | Status | Rust |
|---|---|---|---|
| `AIEnums.cs` | - | dropped | no type declarations (comments / global usings only) |
| `AiCommandCategory.cs` | AiCommandCategory | ported | `AiCommandCategory` → `aaemu-game/src/systems/ai/params.rs` |
| `AiParamType.cs` | AiParamType | ported | `AiParamType` → `aaemu-game/src/systems/ai/params.rs` |
| `NpcControlCategory.cs` | NpcControlCategory | merged | `NpcControlCategory` merged: systems/ai/hooks.rs raw ids |
| `PathType.cs` | PathType | merged | `PathType` merged: systems/quests/templates.rs raw u32 |

### `AAEmu.Game/Models/Game/AI/Utils`

| File | Types | Status | Rust |
|---|---|---|---|
| `AIUtils.cs` | AiUtils | merged | `AiUtils` merged into `aaemu-game/src/systems/ai/framework.rs` |

### `AAEmu.Game/Models/Game/AI/v2/AiCharacters`

| File | Types | Status | Rust |
|---|---|---|---|
| `AlmightyNpcAiCharacter.cs` | AlmightyNpcAiCharacter | merged | `AlmightyNpcAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `ArcherHoldPositionAiCharacter.cs` | ArcherHoldPositionAiCharacter | merged | `ArcherHoldPositionAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `ArcherRoamingAiCharacter.cs` | ArcherRoamingAiCharacter | merged | `ArcherRoamingAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `BigMonsterHoldPositionAiCharacter.cs` | BigMonsterHoldPositionAiCharacter | merged | `BigMonsterHoldPositionAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `BigMonsterRoamingAiCharacter.cs` | BigMonsterRoamingAiCharacter | merged | `BigMonsterRoamingAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `DefaultAiCharacter.cs` | DefaultAiCharacter | merged | `DefaultAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `DummyAiCharacter.cs` | DummyAiCharacter | merged | `DummyAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `FlytrapAiCharacter.cs` | FlytrapAiCharacter | merged | `FlytrapAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `HoldPositionAiCharacter.cs` | HoldPositionAiCharacter | merged | `HoldPositionAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `RoamingAiCharacter.cs` | RoamingAiCharacter | merged | `RoamingAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `TowerDefenseAttackerAiCharacter.cs` | TowerDefenseAttackerAiCharacter | merged | `TowerDefenseAttackerAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `WildBoarHoldPositionAiCharacter.cs` | WildBoarHoldPositionAiCharacter | merged | `WildBoarHoldPositionAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `WildBoarRoamingAiCharacter.cs` | WildBoarRoamingAiCharacter | merged | `WildBoarRoamingAiCharacter` merged into `aaemu-game/src/systems/ai/framework.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Behaviors`

| File | Types | Status | Rust |
|---|---|---|---|
| `BaseCombatBehavior.cs` | BaseCombatBehavior | merged | `BaseCombatBehavior` merged into `aaemu-game/src/systems/ai/behaviors.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Behaviors/Archer`

| File | Types | Status | Rust |
|---|---|---|---|
| `ArcherAttackBehavior.cs` | ArcherAttackBehavior | merged | `ArcherAttackBehavior` merged into `aaemu-game/src/systems/ai/behaviors.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Behaviors/BigMonster`

| File | Types | Status | Rust |
|---|---|---|---|
| `BigMonsterAttackBehavior.cs` | BigMonsterAttackBehavior | ported | `BigMonsterAttackBehavior` → `systems/ai/behaviors.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Behaviors/Common`

| File | Types | Status | Rust |
|---|---|---|---|
| `AlertBehavior.cs` | AlertBehavior | merged | `AlertBehavior` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `AlmightyAttackBehavior.cs` | AlmightyAttackBehavior | merged | `AlmightyAttackBehavior` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `AttackBehavior.cs` | AttackBehavior | merged | `AttackBehavior` merged into `aaemu-game/src/systems/ai/behaviors.rs` |
| `DeadBehavior.cs` | DeadBehavior | merged | `DeadBehavior` merged into `aaemu-game/src/systems/ai/npc_events.rs` |
| `DespawningBehavior.cs` | DespawningBehavior | ported | `DespawningBehavior` → `systems/ai/behaviors.rs` |
| `DoNothingBehavior.cs` | DoNothingBehavior | ported | `DoNothingBehavior` → `systems/ai/behaviors.rs` |
| `DummyBehavior.cs` | DummyBehavior | ported | `DummyBehavior` → `systems/ai/behaviors.rs` |
| `FollowPathBehavior.cs` | FollowPathBehavior | merged | `FollowPathBehavior` merged into `aaemu-game/src/systems/ai/behaviors.rs` |
| `FollowUnitBehavior.cs` | FollowUnitBehavior | ported | `FollowUnitBehavior` → `systems/ai/behaviors.rs` |
| `HoldPositionBehavior.cs` | HoldPositionBehavior | merged | `HoldPositionBehavior` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `IdleBehavior.cs` | IdleBehavior | merged | `IdleBehavior` merged into `aaemu-game/src/systems/npcs/heightmap.rs` |
| `ReturnStateBehavior.cs` | ReturnStateBehavior | merged | `ReturnStateBehavior` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `RoamingBehavior.cs` | RoamingBehavior | merged | `RoamingBehavior` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `RunCommandSetBehavior.cs` | RunCommandSetBehavior | merged | `RunCommandSetBehavior` merged into `aaemu-game/src/systems/ai/behaviors.rs` |
| `SpawningBehavior.cs` | SpawningBehavior | merged | `SpawningBehavior` merged into `aaemu-game/src/systems/ai/framework.rs` |
| `TalkBehavior.cs` | TalkBehavior | merged | `TalkBehavior` merged into `aaemu-game/src/systems/ai/behaviors.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Behaviors/Flytrap`

| File | Types | Status | Rust |
|---|---|---|---|
| `FlytrapAlertBehavior.cs` | FlytrapAlertBehavior | ported | `FlytrapAlertBehavior` → `systems/ai/behaviors.rs` |
| `FlytrapAttackBehavior.cs` | FlytrapAttackBehavior | merged | `FlytrapAttackBehavior` merged into `aaemu-game/src/systems/ai/behaviors.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Behaviors/WildBoar`

| File | Types | Status | Rust |
|---|---|---|---|
| `WildBoarAttackBehavior.cs` | WildBoarAttackBehavior | merged | `WildBoarAttackBehavior` merged into `aaemu-game/src/systems/ai/framework.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Controls`

| File | Types | Status | Rust |
|---|---|---|---|
| `AiPathHandler.cs` | AiPathHandler | ported | `AiPathHandler` → `aaemu-game/src/systems/ai/path_handler.rs` |
| `AiPathPoint.cs` | AiPathPoint | ported | `AiPathPoint` → `aaemu-game/src/systems/ai/path_handler.rs` |
| `AiPathPointAction.cs` | AiPathPointAction | ported | `AiPathPointAction` → `aaemu-game/src/systems/ai/path_handler.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Framework`

| File | Types | Status | Rust |
|---|---|---|---|
| `Behavior.cs` | Behavior | ported | `Behavior` → `aaemu-game/src/systems/skills/controllers.rs` |
| `BehaviorKind.cs` | BehaviorKind | ported | `BehaviorKind` → `aaemu-game/src/systems/ai/framework.rs` |
| `NpcAi.cs` | NpcAi | ported | `NpcAi` → `aaemu-game/src/systems/ai/framework.rs` |
| `Transition.cs` | Transition | ported | `Transition` → `aaemu-game/src/systems/ai/framework.rs` |
| `TransitionEvent.cs` | TransitionEvent | ported | `TransitionEvent` → `aaemu-game/src/systems/ai/framework.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Params`

| File | Types | Status | Rust |
|---|---|---|---|
| `AiCommandSets.cs` | AiCommandSets | ported | `AiCommandSets` → `aaemu-game/src/systems/ai/params.rs` |
| `AiCommands.cs` | AiCommands | ported | `AiCommands` → `aaemu-game/src/systems/ai/params.rs` |
| `AiEvent.cs` | AiEvent | ported | `AiEvent` → `aaemu-game/src/systems/ai/params.rs` |
| `AiEvents.cs` | AiEvents | merged | `AiEvents` merged into `aaemu-game/src/systems/ai/data.rs` |
| `AiLua.cs` | AiLua | merged | `AiLua` merged into `aaemu-game/src/systems/ai/lua.rs` |
| `AiParams.cs` | AiParams | ported | `AiParams` → `aaemu-game/src/systems/ai/params.rs` |
| `NpcChatBubble.cs` | NpcChatBubble | ported | `NpcChatBubble` → `aaemu-game/src/systems/ai/params.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Params/Almighty`

| File | Types | Status | Rust |
|---|---|---|---|
| `AiSkill.cs` | AiSkill | ported | `AiSkill` → `aaemu-game/src/systems/ai/params.rs` |
| `AiSkillList.cs` | AiSkillList | ported | `AiSkillList` → `aaemu-game/src/systems/ai/params.rs` |
| `AlmightyNpcAiParams.cs` | AlmightyNpcAiParams | merged | `AlmightyNpcAiParams` merged into `aaemu-game/src/systems/ai/params.rs` |
| `SkillList.cs` | SkillList | ported | `SkillList` → `aaemu-game/src/systems/ai/params.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Params/Archer`

| File | Types | Status | Rust |
|---|---|---|---|
| `ArcherAiParams.cs` | ArcherAiParams | merged | `ArcherAiParams` merged into `aaemu-game/src/systems/ai/params.rs` |
| `ArcherCombatSkill.cs` | ArcherCombatSkill | ported | `ArcherCombatSkill` → `aaemu-game/src/systems/ai/params.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Params/BigMonster`

| File | Types | Status | Rust |
|---|---|---|---|
| `BigMonsterAiParams.cs` | BigMonsterAiParams | merged | `BigMonsterAiParams` merged into `aaemu-game/src/systems/ai/params.rs` |
| `BigMonsterCombatSkill.cs` | BigMonsterCombatSkill | ported | `BigMonsterCombatSkill` → `aaemu-game/src/systems/ai/params.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Params/Flytrap`

| File | Types | Status | Rust |
|---|---|---|---|
| `FlytrapAiParams.cs` | FlytrapAiParams | merged | `FlytrapAiParams` merged into `aaemu-game/src/systems/ai/params.rs` |
| `FlytrapCombatSkill.cs` | FlytrapCombatSkill | ported | `FlytrapCombatSkill` → `aaemu-game/src/systems/ai/params.rs` |

### `AAEmu.Game/Models/Game/AI/v2/Params/WildBoar`

| File | Types | Status | Rust |
|---|---|---|---|
| `WildBoarAiParams.cs` | WildBoarAiParams | merged | `WildBoarAiParams` merged into `aaemu-game/src/systems/ai/params.rs` |
| `WildBoarAiSpurtSkill.cs` | WildBoarAiSpurtSkill | ported | `WildBoarAiSpurtSkill` → `aaemu-game/src/systems/ai/params.rs` |

### `AAEmu.Game/Models/Game/Achievement`

| File | Types | Status | Rust |
|---|---|---|---|
| `AchievementInfo.cs` | AchievementInfo | ported | `AchievementInfo` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementObjectives.cs` | AchievementObjectives | ported | `AchievementObjectives` → `aaemu-game/src/systems/social/achievement.rs` |
| `Achievements.cs` | Achievements | ported | `Achievements` → `aaemu-game/src/systems/social/achievement.rs` |
| `CharRecords.cs` | CharRecords | ported | `CharRecords` → `aaemu-game/src/systems/social/achievement.rs` |
| `PreCompletedAchievements.cs` | PreCompletedAchievements | ported | `PreCompletedAchievements` → `aaemu-game/src/systems/social/achievement.rs` |

### `AAEmu.Game/Models/Game/Achievement/Enums`

| File | Types | Status | Rust |
|---|---|---|---|
| `AchievementAdventureCategory.cs` | AchievementAdventureCategory | ported | `AchievementAdventureCategory` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementCategory.cs` | AchievementCategory | ported | `AchievementCategory` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementCombatCategory.cs` | AchievementCombatCategory | ported | `AchievementCombatCategory` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementEventCategory.cs` | AchievementEventCategory | ported | `AchievementEventCategory` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementFilter.cs` | AchievementFilter | ported | `AchievementFilter` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementKind.cs` | AchievementKind | ported | `AchievementKind` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementLevelCategory.cs` | AchievementLevelCategory | ported | `AchievementLevelCategory` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementLifeCategory.cs` | AchievementLifeCategory | ported | `AchievementLifeCategory` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementNormalCategory.cs` | AchievementNormalCategory | ported | `AchievementNormalCategory` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementSeasonCategory.cs` | AchievementSeasonCategory | ported | `AchievementSeasonCategory` → `aaemu-game/src/systems/social/achievement.rs` |
| `AchievementSocietyCategory.cs` | AchievementSocietyCategory | ported | `AchievementSocietyCategory` → `aaemu-game/src/systems/social/achievement.rs` |
| `CharRecordKind.cs` | CharRecordKind | ported | `CharRecordKind` → `aaemu-game/src/systems/social/achievement.rs` |
| `RewardItemReason.cs` | RewardItemReason | ported | `RewardItemReason` → `aaemu-game/src/systems/social/achievement.rs` |

### `AAEmu.Game/Models/Game/Animation`

| File | Types | Status | Rust |
|---|---|---|---|
| `Anim.cs` | Anim | ported | `Anim` → `aaemu-game/src/systems/skills/data/misc.rs` |
| `AnimCategory.cs` | AnimCategory | merged | `AnimCategory` merged into `systems/skills/data/misc.rs` |
| `AnimCombatSyncEvent.cs` | AnimCombatSyncEvent | merged | `AnimCombatSyncEvent` merged into `systems/skills/data/misc.rs` |
| `AnimDuration.cs` | AnimDuration | merged | `AnimDuration` merged into `systems/skills/data/misc.rs` |

### `AAEmu.Game/Models/Game/Auction`

| File | Types | Status | Rust |
|---|---|---|---|
| `AuctionBid.cs` | AuctionBid | ported | `AuctionBid` → `aaemu-game/src/systems/economy/auction.rs` |
| `AuctionDisplay.cs` | AuctionDisplay | merged | `AuctionDisplay` merged into `aaemu-game/src/systems/economy/auction.rs` |
| `AuctionDuration.cs` | AuctionDuration | ported | `AuctionDuration` → `aaemu-game/src/systems/economy/auction.rs` |
| `AuctionHouseTask.cs` | AuctionHouseTask | merged | `AuctionHouseTask` merged into `aaemu-game/src/systems/economy/auction.rs` |
| `AuctionLot.cs` | AuctionLot | ported | `AuctionLot` → `aaemu-game/src/systems/economy/auction.rs` |
| `AuctionSearchSortKind.cs` | AuctionSearchSortKind | merged | `AuctionSearchSortKind` merged into `aaemu-game/src/systems/economy/auction.rs` |
| `AuctionSearchSortOrder.cs` | AuctionSearchSortOrder | merged | `AuctionSearchSortOrder` merged into `aaemu-game/src/systems/economy/auction.rs` |

### `AAEmu.Game/Models/Game/Auction/Templates`

| File | Types | Status | Rust |
|---|---|---|---|
| `AuctionSearchTemplate.cs` | AuctionSearch | ported | `AuctionSearch` → `aaemu-game/src/systems/economy/auction.rs` |

### `AAEmu.Game/Models/Game/CashShop`

| File | Types | Status | Rust |
|---|---|---|---|
| `AuditIcsSale.cs` | AuditIcsSale | ported | `AuditIcsSale` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `IcsItem.cs` | IcsItem | ported | `IcsItem` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `IcsMenu.cs` | IcsMenu | ported | `IcsMenu` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `IcsSku.cs` | IcsSku | ported | `IcsSku` → `aaemu-game/src/systems/economy/cash_shop.rs` |
| `PremiumDetail.cs` | PremiumDetail | ported | `PremiumDetail` → `aaemu-game/src/systems/economy/cash_shop.rs` |

### `AAEmu.Game/Models/Game/Char`

| File | Types | Status | Rust |
|---|---|---|---|
| `Actability.cs` | Actability | ported | `Actability` → `aaemu-game/src/models/char_data.rs` |
| `ActabilityType.cs` | ActabilityType | ported | `ActabilityType` → `aaemu-game/src/systems/economy/item_effects.rs` |
| `ActionSlot.cs` | ActionSlot | ported | `ActionSlot` → `aaemu-game/src/models/char_data.rs` |
| `ActionSlotType.cs` | ActionSlotType | ported | `ActionSlotType` → `aaemu-game/src/models/char_data.rs` |
| `Character.cs` | Character | ported | `Character` → `aaemu-game/src/models/character.rs` |
| `CharacterAbilities.cs` | CharacterAbilities | ported | `CharacterAbilities` → `aaemu-game/src/models/char_data.rs` |
| `CharacterActability.cs` | CharacterActability | ported | `CharacterActability` → `aaemu-game/src/models/char_data.rs` |
| `CharacterAppellations.cs` | CharacterAppellations | ported | `CharacterAppellations` → `aaemu-game/src/models/char_data.rs` |
| `CharacterBlocked.cs` | CharacterBlocked, BlockedTemplate | merged | `CharacterBlocked` merged into `aaemu-game/src/systems/social/friend.rs`; `BlockedTemplate` merged: models/character.rs (owner, blocked) tuples |
| `CharacterCombat.cs` | Character, DurabilityLossTargets | ported | `Character` → `aaemu-game/src/models/character.rs`; `DurabilityLossTargets` → `aaemu-game/src/systems/economy/repair.rs` |
| `CharacterCounters.cs` | Character | ported | `Character` → `aaemu-game/src/models/character.rs` |
| `CharacterCraft.cs` | CharacterCraft | merged | `CharacterCraft` merged into `aaemu-game/src/systems/economy/craft.rs` |
| `CharacterEvents.cs` | CharacterEvents, OnTeamJoinArgs, OnTeamLeaveArgs, OnDungeonLeaveArgs … | ported | `CharacterEvents` → `aaemu-game/src/systems/instances/events.rs`; `OnTeamJoinArgs` merged: systems/instances/events.rs hooks raised from systems/social/team.rs, world/session.rs; `OnTeamLeaveArgs` merged: systems/instances/events.rs hooks raised from systems/social/team.rs, world/session.rs; `OnDungeonLeaveArgs` merged: systems/instances/events.rs hooks raised from systems/social/team.rs, world/session.rs; `OnDisconnectArgs` merged: systems/instances/events.rs hooks raised from systems/social/team.rs, world/session.rs |
| `CharacterFriends.cs` | CharacterFriends | merged | `CharacterFriends` merged into `aaemu-game/src/systems/social/friend.rs` |
| `CharacterMails.cs` | CharacterMails | merged | `CharacterMails` merged into `aaemu-game/src/systems/economy/mail_character.rs` |
| `CharacterMates.cs` | CharacterMates, MateDb | ported | `CharacterMates` → `aaemu-game/src/systems/vehicles/mates.rs`; `MateDb` → `aaemu-game/src/systems/vehicles/mates.rs` |
| `CharacterPortals.cs` | CharacterPortals | ported | `CharacterPortals` → `aaemu-game/src/models/char_data.rs` |
| `CharacterQuests.cs` | CharacterQuests | ported | `CharacterQuests` → `aaemu-game/src/models/char_data.rs` |
| `CharacterSkills.cs` | CharacterSkills, SkillType | ported | `CharacterSkills` → `aaemu-game/src/models/char_data.rs`; `SkillType` merged into `aaemu-game/src/db_character.rs` |
| `CharacterVisualOptions.cs` | CharacterVisualOptions | merged | `CharacterVisualOptions` merged into `aaemu-game/src/models/char_data.rs` |
| `ExpandExpertLimit.cs` | ExpandExpertLimit | ported | `ExpandExpertLimit` → `aaemu-game/src/systems/services/expands.rs` |
| `ExpertLimit.cs` | ExpertLimit | ported | `ExpertLimit` → `aaemu-game/src/data/character_templates.rs` |
| `Gender.cs` | Gender | ported | `Gender` → `aaemu-game/src/systems/quests/engine.rs` |
| `ICharacter.cs` | ICharacter | ported | `ICharacter` → `aaemu-game/src/systems/services/commands/output.rs` |
| `Inventory.cs` | Inventory, SwapAction | ported | `Inventory` → `aaemu-game/src/models/character.rs`; `SwapAction` → `aaemu-game/src/systems/economy/inventory.rs` |
| `Race.cs` | Race | ported | `Race` → `aaemu-game/src/systems/social/chat.rs` |
| `WeaponWieldKind.cs` | WeaponWieldKind | ported | `WeaponWieldKind` → `aaemu-game/src/systems/skills/plots.rs` |

### `AAEmu.Game/Models/Game/Char/Templates`

| File | Types | Status | Rust |
|---|---|---|---|
| `ActabilityCategoriesTemplate.cs` | ActabilityCategoriesTemplate | merged | `ActabilityCategoriesTemplate` merged: data/character_templates.rs id->group map |
| `ActabilityTemplate.cs` | ActabilityTemplate | ported | `ActabilityTemplate` → `aaemu-game/src/data/character_templates.rs` |
| `AppellationTemplate.cs` | AppellationTemplate | merged | `AppellationTemplate` merged: data/character_templates.rs id->buff map |
| `CharacterTemplate.cs` | CharacterTemplate | ported | `CharacterTemplate` → `aaemu-game/src/data/character_templates.rs` |
| `CharacterTemplateConfig.cs` | CharacterTemplateConfig | ported | `CharacterTemplateConfig` → `aaemu-game/src/data/character_templates.rs` |

### `AAEmu.Game/Models/Game/Chat`

| File | Types | Status | Rust |
|---|---|---|---|
| `ChatBubbleKind.cs` | ChatBubbleKind | merged | `ChatBubbleKind` merged into `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `ChatChannel.cs` | ChatChannel | ported | `ChatChannel` → `aaemu-game/src/systems/social/chat.rs` |
| `ChatType.cs` | ChatType | ported | `ChatType` → `aaemu-game/src/systems/social/chat.rs` |

### `AAEmu.Game/Models/Game/CommonFarm`

| File | Types | Status | Rust |
|---|---|---|---|
| `CommonFarm.cs` | CommonFarm | ported | `CommonFarm` → `aaemu-game/src/systems/npcs/public_farm.rs` |
| `FarmGroup.cs` | FarmGroup | ported | `FarmGroup` → `aaemu-game/src/systems/npcs/public_farm.rs` |
| `FarmGroupDoodads.cs` | FarmGroupDoodads | ported | `FarmGroupDoodads` → `aaemu-game/src/systems/npcs/public_farm.rs` |

### `AAEmu.Game/Models/Game/CommonFarm/Static`

| File | Types | Status | Rust |
|---|---|---|---|
| `FarmGroupKind.cs` | FarmGroupKind | ported | `FarmGroupKind` → `aaemu-game/src/systems/npcs/public_farm.rs` |

### `AAEmu.Game/Models/Game/Crafts`

| File | Types | Status | Rust |
|---|---|---|---|
| `Craft.cs` | Craft | ported | `Craft` → `aaemu-game/src/systems/economy/craft_data.rs` |
| `CraftMaterial.cs` | CraftMaterial | ported | `CraftMaterial` → `aaemu-game/src/systems/economy/craft_data.rs` |
| `CraftProduct.cs` | CraftProduct | ported | `CraftProduct` → `aaemu-game/src/systems/economy/craft_data.rs` |

### `AAEmu.Game/Models/Game/Crime`

| File | Types | Status | Rust |
|---|---|---|---|
| `BountyDescription.cs` | BountyDescription | ported | `BountyDescription` → `aaemu-game/src/systems/social/crime.rs` |
| `CourtRoomRegion.cs` | CourtRoomRegion | ported | `CourtRoomRegion` → `aaemu-game/src/systems/social/chat.rs` |
| `CrimeEvent.cs` | CrimeEvent | ported | `CrimeEvent` → `aaemu-game/src/systems/social/crime.rs` |
| `CrimeKind.cs` | CrimeKind | ported | `CrimeKind` → `aaemu-game/src/systems/social/crime.rs` |
| `TrialCourtRoom.cs` | TrialCourtRoom | merged | `TrialCourtRoom` merged into `aaemu-game/src/systems/social/trial.rs` |
| `TrialData.cs` | TrialData | ported | `TrialData` → `aaemu-game/src/systems/social/trial.rs` |
| `TrialJuryBox.cs` | TrialJuryBox | merged | `TrialJuryBox` merged into `aaemu-game/src/systems/social/trial.rs` |
| `TrialSentenceResult.cs` | TrialSentenceResult | merged | `TrialSentenceResult` merged into `aaemu-game/src/systems/social/trial.rs` |
| `TrialStep.cs` | TrialStep | ported | `TrialStep` → `aaemu-game/src/systems/social/trial.rs` |

### `AAEmu.Game/Models/Game/DoodadObj`

| File | Types | Status | Rust |
|---|---|---|---|
| `BondDoodad.cs` | BondDoodad | ported | `BondDoodad` → `aaemu-game/src/models/doodad.rs` |
| `Doodad.cs` | Doodad | ported | `Doodad` → `aaemu-game/src/models/doodad.rs` |
| `DoodadCoffer.cs` | DoodadCoffer | merged | `DoodadCoffer` merged into `aaemu-game/src/systems/npcs/doodad/coffer.rs` |
| `DoodadFunc.cs` | DoodadFunc | ported | `DoodadFunc` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncGroups.cs` | DoodadFuncGroups, DoodadFuncGroupKind | ported | `DoodadFuncGroups` → `aaemu-game/src/systems/npcs/doodad/templates.rs`; `DoodadFuncGroupKind` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadGroups.cs` | DoodadGroups | ported | `DoodadGroups` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadOwnerType.cs` | DoodadOwnerType | ported | `DoodadOwnerType` → `aaemu-game/src/models/doodad.rs` |
| `DoodadPhaseFunc.cs` | DoodadPhaseFunc | ported | `DoodadPhaseFunc` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadSpawner.cs` | DoodadSpawner | ported | `DoodadSpawner` → `aaemu-game/src/systems/npcs/doodad/spawner.rs` |
| `VehicleSeat.cs` | VehicleSeat | ported | `VehicleSeat` → `aaemu-game/src/models/doodad.rs` |

### `AAEmu.Game/Models/Game/DoodadObj/Details`

| File | Types | Status | Rust |
|---|---|---|---|
| `DoodadFuncConsumeChangerItem.cs` | DoodadFuncConsumeChangerItem | ported | `DoodadFuncConsumeChangerItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |

### `AAEmu.Game/Models/Game/DoodadObj/Funcs`

| File | Types | Status | Rust |
|---|---|---|---|
| `DoodadFuncAnimate.cs` | DoodadFuncAnimate | ported | `DoodadFuncAnimate` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncAreaTrigger.cs` | DoodadFuncAreaTrigger | ported | `DoodadFuncAreaTrigger` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncAttachment.cs` | DoodadFuncAttachment | ported | `DoodadFuncAttachment` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncAuctionUi.cs` | DoodadFuncAuctionUi | ported | `DoodadFuncAuctionUi` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncBankUi.cs` | DoodadFuncBankUi | ported | `DoodadFuncBankUi` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncBinding.cs` | DoodadFuncBinding | ported | `DoodadFuncBinding` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncBubble.cs` | DoodadFuncBubble | ported | `DoodadFuncBubble` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncBuff.cs` | DoodadFuncBuff | ported | `DoodadFuncBuff` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncButcher.cs` | DoodadFuncButcher | ported | `DoodadFuncButcher` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncBuyFish.cs` | DoodadFuncBuyFish | ported | `DoodadFuncBuyFish` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncBuyFishItem.cs` | DoodadFuncBuyFishItem | ported | `DoodadFuncBuyFishItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncBuyFishModel.cs` | DoodadFuncBuyFishModel | ported | `DoodadFuncBuyFishModel` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCatch.cs` | DoodadFuncCatch | ported | `DoodadFuncCatch` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCerealHarvest.cs` | DoodadFuncCerealHarvest | ported | `DoodadFuncCerealHarvest` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCleanupLogicLink.cs` | DoodadFuncCleanupLogicLink | ported | `DoodadFuncCleanupLogicLink` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncClimateReact.cs` | DoodadFuncClimateReact | ported | `DoodadFuncClimateReact` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncClimb.cs` | DoodadFuncClimb | ported | `DoodadFuncClimb` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncClout.cs` | DoodadFuncClout | ported | `DoodadFuncClout` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCoffer.cs` | DoodadFuncCoffer | ported | `DoodadFuncCoffer` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCofferPerm.cs` | DoodadFuncCofferPerm | ported | `DoodadFuncCofferPerm` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncConditionalUse.cs` | DoodadFuncConditionalUse | ported | `DoodadFuncConditionalUse` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncConsumeChanger.cs` | DoodadFuncConsumeChanger | ported | `DoodadFuncConsumeChanger` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncConsumeChangerModel.cs` | DoodadFuncConsumeChangerModel | ported | `DoodadFuncConsumeChangerModel` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncConsumeChangerModelItem.cs` | DoodadFuncConsumeChangerModelItem | ported | `DoodadFuncConsumeChangerModelItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncConsumeItem.cs` | DoodadFuncConsumeItem | ported | `DoodadFuncConsumeItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncConvertFish.cs` | DoodadFuncConvertFish | ported | `DoodadFuncConvertFish` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncConvertFishItem.cs` | DoodadFuncConvertFishItem | ported | `DoodadFuncConvertFishItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCraftAct.cs` | DoodadFuncCraftAct | ported | `DoodadFuncCraftAct` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCraftCancel.cs` | DoodadFuncCraftCancel | ported | `DoodadFuncCraftCancel` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCraftDirect.cs` | DoodadFuncCraftDirect | ported | `DoodadFuncCraftDirect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCraftGetItem.cs` | DoodadFuncCraftGetItem | ported | `DoodadFuncCraftGetItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCraftInfo.cs` | DoodadFuncCraftInfo | ported | `DoodadFuncCraftInfo` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCraftPack.cs` | DoodadFuncCraftPack | ported | `DoodadFuncCraftPack` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCraftStart.cs` | DoodadFuncCraftStart | ported | `DoodadFuncCraftStart` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCraftStartCraft.cs` | DoodadFuncCraftStartCraft | ported | `DoodadFuncCraftStartCraft` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCropHarvest.cs` | DoodadFuncCropHarvest | ported | `DoodadFuncCropHarvest` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCrystalCollect.cs` | DoodadFuncCrystalCollect | ported | `DoodadFuncCrystalCollect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCutdown.cs` | DoodadFuncCutdown | ported | `DoodadFuncCutdown` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncCutdowning.cs` | DoodadFuncCutdowning | ported | `DoodadFuncCutdowning` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncDairyCollect.cs` | DoodadFuncDairyCollect | ported | `DoodadFuncDairyCollect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncDeclareSiege.cs` | DoodadFuncDeclareSiege | ported | `DoodadFuncDeclareSiege` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncDig.cs` | DoodadFuncDig | ported | `DoodadFuncDig` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncDigTerrain.cs` | DoodadFuncDigTerrain | ported | `DoodadFuncDigTerrain` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncDyeingredientCollect.cs` | DoodadFuncDyeingredientCollect | ported | `DoodadFuncDyeingredientCollect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncEnterInstance.cs` | DoodadFuncEnterInstance | ported | `DoodadFuncEnterInstance` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncEnterSysInstance.cs` | DoodadFuncEnterSysInstance | ported | `DoodadFuncEnterSysInstance` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncEvidenceItemLoot.cs` | DoodadFuncEvidenceItemLoot | ported | `DoodadFuncEvidenceItemLoot` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncExchange.cs` | DoodadFuncExchange | ported | `DoodadFuncExchange` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncExitIndun.cs` | DoodadFuncExitIndun | ported | `DoodadFuncExitIndun` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncFakeUse.cs` | DoodadFuncFakeUse | ported | `DoodadFuncFakeUse` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncFeed.cs` | DoodadFuncFeed | ported | `DoodadFuncFeed` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncFiberCollect.cs` | DoodadFuncFiberCollect | ported | `DoodadFuncFiberCollect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncFinal.cs` | DoodadFuncFinal | ported | `DoodadFuncFinal` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncFishSchool.cs` | DoodadFuncFishSchool | ported | `DoodadFuncFishSchool` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncFruitPick.cs` | DoodadFuncFruitPick | ported | `DoodadFuncFruitPick` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncGassExtract.cs` | DoodadFuncGassExtract | ported | `DoodadFuncGassExtract` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncGrowth.cs` | DoodadFuncGrowth | ported | `DoodadFuncGrowth` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncHarvest.cs` | DoodadFuncHarvest | ported | `DoodadFuncHarvest` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncHouseFarm.cs` | DoodadFuncHouseFarm | ported | `DoodadFuncHouseFarm` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncHousingArea.cs` | DoodadFuncHousingArea | ported | `DoodadFuncHousingArea` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncHunger.cs` | DoodadFuncHunger | ported | `DoodadFuncHunger` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncInsertCounter.cs` | DoodadFuncInsertCounter | ported | `DoodadFuncInsertCounter` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncLogic.cs` | DoodadFuncLogic | ported | `DoodadFuncLogic` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncLogicFamilyProvider.cs` | DoodadFuncLogicFamilyProvider | ported | `DoodadFuncLogicFamilyProvider` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncLogicFamilySubscriber.cs` | DoodadFuncLogicFamilySubscriber | ported | `DoodadFuncLogicFamilySubscriber` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncLootItem.cs` | DoodadFuncLootItem | ported | `DoodadFuncLootItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncLootPack.cs` | DoodadFuncLootPack | ported | `DoodadFuncLootPack` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncMachinePartsCollect.cs` | DoodadFuncMachinePartsCollect | ported | `DoodadFuncMachinePartsCollect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncMedicalingredientMine.cs` | DoodadFuncMedicalingredientMine | ported | `DoodadFuncMedicalingredientMine` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncMow.cs` | DoodadFuncMow | ported | `DoodadFuncMow` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncNaviMarkPosToMap.cs` | DoodadFuncNaviMarkPosToMap | ported | `DoodadFuncNaviMarkPosToMap` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncNaviNaming.cs` | DoodadFuncNaviNaming | ported | `DoodadFuncNaviNaming` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncNaviOpenBounty.cs` | DoodadFuncNaviOpenBounty | ported | `DoodadFuncNaviOpenBounty` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncNaviOpenMailbox.cs` | DoodadFuncNaviOpenMailbox | ported | `DoodadFuncNaviOpenMailbox` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncNaviOpenPortal.cs` | DoodadFuncNaviOpenPortal | ported | `DoodadFuncNaviOpenPortal` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncNaviRemove.cs` | DoodadFuncNaviRemove | ported | `DoodadFuncNaviRemove` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncNaviRemoveTimer.cs` | DoodadFuncNaviRemoveTimer | ported | `DoodadFuncNaviRemoveTimer` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncNaviTeleport.cs` | DoodadFuncNaviTeleport | ported | `DoodadFuncNaviTeleport` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncOpenFarmInfo.cs` | DoodadFuncOpenFarmInfo | ported | `DoodadFuncOpenFarmInfo` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncOpenPaper.cs` | DoodadFuncOpenPaper | ported | `DoodadFuncOpenPaper` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncOreMine.cs` | DoodadFuncOreMine | ported | `DoodadFuncOreMine` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncParentInfo.cs` | DoodadFuncParentInfo | ported | `DoodadFuncParentInfo` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncParrot.cs` | DoodadFuncParrot | ported | `DoodadFuncParrot` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncPlantCollect.cs` | DoodadFuncPlantCollect | ported | `DoodadFuncPlantCollect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncPlayFlowGraph.cs` | DoodadFuncPlayFlowGraph | ported | `DoodadFuncPlayFlowGraph` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncPulse.cs` | DoodadFuncPulse | ported | `DoodadFuncPulse` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncPulseTrigger.cs` | DoodadFuncPulseTrigger | ported | `DoodadFuncPulseTrigger` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncPurchase.cs` | DoodadFuncPurchase | ported | `DoodadFuncPurchase` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncPuzzleIn.cs` | DoodadFuncPuzzleIn | ported | `DoodadFuncPuzzleIn` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncPuzzleOut.cs` | DoodadFuncPuzzleOut | ported | `DoodadFuncPuzzleOut` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncPuzzleRoll.cs` | DoodadFuncPuzzleRoll | ported | `DoodadFuncPuzzleRoll` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncQuest.cs` | DoodadFuncQuest | ported | `DoodadFuncQuest` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncRatioChange.cs` | DoodadFuncRatioChange | ported | `DoodadFuncRatioChange` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncRatioRespawn.cs` | DoodadFuncRatioRespawn | ported | `DoodadFuncRatioRespawn` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncRecoverItem.cs` | DoodadFuncRecoverItem | ported | `DoodadFuncRecoverItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncRemoveInstance.cs` | DoodadFuncRemoveInstance | ported | `DoodadFuncRemoveInstance` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncRemoveItem.cs` | DoodadFuncRemoveItem | ported | `DoodadFuncRemoveItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncRenewItem.cs` | DoodadFuncRenewItem | ported | `DoodadFuncRenewItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncRequireItem.cs` | DoodadFuncRequireItem | ported | `DoodadFuncRequireItem` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncRequireQuest.cs` | DoodadFuncRequireQuest | ported | `DoodadFuncRequireQuest` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncRespawn.cs` | DoodadFuncRespawn | ported | `DoodadFuncRespawn` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncRockMine.cs` | DoodadFuncRockMine | ported | `DoodadFuncRockMine` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncSeedCollect.cs` | DoodadFuncSeedCollect | ported | `DoodadFuncSeedCollect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncShear.cs` | DoodadFuncShear | ported | `DoodadFuncShear` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncSiegePeriod.cs` | DoodadFuncSiegePeriod | ported | `DoodadFuncSiegePeriod` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncSign.cs` | DoodadFuncSign | ported | `DoodadFuncSign` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncSkillHit.cs` | DoodadFuncSkillHit | ported | `DoodadFuncSkillHit` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncSkinOff.cs` | DoodadFuncSkinOff | ported | `DoodadFuncSkinOff` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncSoilCollect.cs` | DoodadFuncSoilCollect | ported | `DoodadFuncSoilCollect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncSpawnGimmick.cs` | DoodadFuncSpawnGimmick | ported | `DoodadFuncSpawnGimmick` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncSpawnMgmt.cs` | DoodadFuncSpawnMgmt | ported | `DoodadFuncSpawnMgmt` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncSpiceCollect.cs` | DoodadFuncSpiceCollect | ported | `DoodadFuncSpiceCollect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncStampMaker.cs` | DoodadFuncStampMaker | ported | `DoodadFuncStampMaker` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncStoreUi.cs` | DoodadFuncStoreUi | ported | `DoodadFuncStoreUi` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncTimer.cs` | DoodadFuncTimer | ported | `DoodadFuncTimer` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncTod.cs` | DoodadFuncTod | ported | `DoodadFuncTod` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncTreeByProductsCollect.cs` | DoodadFuncTreeByProductsCollect | ported | `DoodadFuncTreeByProductsCollect` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncUccImprint.cs` | DoodadFuncUccImprint | ported | `DoodadFuncUccImprint` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncUse.cs` | DoodadFuncUse | ported | `DoodadFuncUse` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncWaterVolume.cs` | DoodadFuncWaterVolume | ported | `DoodadFuncWaterVolume` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncZoneReact.cs` | DoodadFuncZoneReact | ported | `DoodadFuncZoneReact` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |

### `AAEmu.Game/Models/Game/DoodadObj/Static`

| File | Types | Status | Rust |
|---|---|---|---|
| `AttachPointKind.cs` | AttachPointKind | ported | `AttachPointKind` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `AttachUnitReason.cs` | AttachUnitReason | ported | `AttachUnitReason` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `BondKind.cs` | BondKind | ported | `BondKind` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncPermission.cs` | DoodadFuncPermission | ported | `DoodadFuncPermission` → `aaemu-game/src/systems/economy/craft.rs` |
| `ModelKind.cs` | ModelKind | ported | `ModelKind` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |

### `AAEmu.Game/Models/Game/DoodadObj/Templates`

| File | Types | Status | Rust |
|---|---|---|---|
| `DoodadCofferTemplate.cs` | DoodadCofferTemplate | merged | `DoodadCofferTemplate` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadFuncTemplate.cs` | DoodadFuncTemplate | merged | `DoodadFuncTemplate` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadPhaseFuncTemplate.cs` | DoodadPhaseFuncTemplate | merged | `DoodadPhaseFuncTemplate` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `DoodadTemplate.cs` | DoodadTemplate | ported | `DoodadTemplate` → `aaemu-game/src/systems/npcs/doodad/templates.rs` |

### `AAEmu.Game/Models/Game/Duels`

| File | Types | Status | Rust |
|---|---|---|---|
| `Duel.cs` | Duel | ported | `Duel` → `aaemu-game/src/systems/social/duel.rs` |
| `DuelDetType.cs` | DuelDetType | ported | `DuelDetType` → `aaemu-game/src/systems/social/duel.rs` |
| `DuelDistance.cs` | DuelDistance | ported | `DuelDistance` → `aaemu-game/src/systems/social/duel.rs` |
| `VictoryState.cs` | VictoryState | ported | `VictoryState` → `aaemu-game/src/systems/instances/instant_game.rs` |

### `AAEmu.Game/Models/Game/Emotion`

| File | Types | Status | Rust |
|---|---|---|---|
| `ExpressText.cs` | ExpressText | ported | `ExpressText` → `aaemu-game/src/systems/services/mod.rs` |

### `AAEmu.Game/Models/Game/Expeditions`

| File | Types | Status | Rust |
|---|---|---|---|
| `Expedition.cs` | Expedition | ported | `Expedition` → `aaemu-game/src/systems/social/expedition.rs` |
| `ExpeditionConfig.cs` | ExpeditionConfig, ExpeditionConfigCreate | ported | `ExpeditionConfig` → `aaemu-game/src/systems/social/config.rs`; `ExpeditionConfigCreate` merged into `aaemu-game/src/systems/social/config.rs` |
| `ExpeditionMember.cs` | ExpeditionMember | ported | `ExpeditionMember` → `aaemu-game/src/systems/social/expedition.rs` |
| `ExpeditionRolePolicy.cs` | ExpeditionRolePolicy | ported | `ExpeditionRolePolicy` → `aaemu-game/src/systems/social/expedition.rs` |

### `AAEmu.Game/Models/Game/Faction`

| File | Types | Status | Rust |
|---|---|---|---|
| `FactionRelation.cs` | FactionRelation | ported | `FactionRelation` → `aaemu-game/src/data/world_data.rs` |
| `RelationState.cs` | RelationState | ported | `RelationState` → `aaemu-game/src/systems/social/faction.rs` |
| `SystemFaction.cs` | SystemFaction | ported | `SystemFaction` → `aaemu-game/src/data/world_data.rs` |

### `AAEmu.Game/Models/Game/Features`

| File | Types | Status | Rust |
|---|---|---|---|
| `Feature.cs` | Feature | ported | `Feature` → `aaemu-game/src/data/features.rs` |
| `FeatureSet.cs` | FeatureSet | ported | `FeatureSet` → `aaemu-game/src/data/features.rs` |

### `AAEmu.Game/Models/Game/FishSchools`

| File | Types | Status | Rust |
|---|---|---|---|
| `FishDetails.cs` | FishDetails | ported | `FishDetails` → `aaemu-game/src/systems/npcs/fish_school.rs` |
| `FishSchool.cs` | FishSchool | ported | `FishSchool` → `aaemu-game/src/systems/npcs/mod.rs` |

### `AAEmu.Game/Models/Game/Formulas`

| File | Types | Status | Rust |
|---|---|---|---|
| `Formula.cs` | Formula | ported | `Formula` → `aaemu-game/src/data/formulas.rs` |
| `FormulaKind.cs` | FormulaKind | ported | `FormulaKind` → `aaemu-game/src/data/formulas.rs` |
| `FormulaOwnerType.cs` | FormulaOwnerType | ported | `FormulaOwnerType` → `aaemu-game/src/data/formulas.rs` |
| `UnitFormula.cs` | UnitFormula | ported | `UnitFormula` → `aaemu-game/src/data/formulas.rs` |
| `UnitFormulaKind.cs` | UnitFormulaKind | ported | `UnitFormulaKind` → `aaemu-game/src/data/formulas.rs` |
| `UnitFormulaVariable.cs` | UnitFormulaVariableType, UnitFormulaVariable | ported | `UnitFormulaVariableType` → `aaemu-game/src/data/formulas.rs`; `UnitFormulaVariable` → `aaemu-game/src/data/formulas.rs` |
| `WearableFormula.cs` | WearableFormula | ported | `WearableFormula` → `aaemu-game/src/data/formulas.rs` |
| `WearableFormulaType.cs` | WearableFormulaType | ported | `WearableFormulaType` → `aaemu-game/src/data/formulas.rs` |

### `AAEmu.Game/Models/Game/Gimmicks`

| File | Types | Status | Rust |
|---|---|---|---|
| `Gimmick.cs` | Gimmick | ported | `Gimmick` → `aaemu-game/src/models/gimmick.rs` |
| `GimmickCoordinateTypes.cs` | OffsetCoordinateType, VelocityCoordinateType, AngVelCoordinateType | merged | `OffsetCoordinateType` merged into `aaemu-game/src/systems/npcs/gimmick/spawner.rs`; `VelocityCoordinateType` merged into `aaemu-game/src/systems/npcs/gimmick/spawner.rs`; `AngVelCoordinateType` merged into `aaemu-game/src/systems/npcs/gimmick/spawner.rs` |
| `GimmickMovementElevator.cs` | GimmickMovementElevator | merged | `GimmickMovementElevator` merged into `aaemu-game/src/systems/npcs/gimmick/movement.rs` |
| `GimmickMovementFloatToSurface.cs` | GimmickMovementFloatToSurface | merged | `GimmickMovementFloatToSurface` merged into `aaemu-game/src/systems/npcs/gimmick/movement.rs` |
| `GimmickMovementHandler.cs` | GimmickMovementHandler | merged | `GimmickMovementHandler` merged into `aaemu-game/src/systems/npcs/gimmick/mod.rs` |
| `GimmickSpawner.cs` | GimmickSpawner | ported | `GimmickSpawner` → `aaemu-game/src/systems/npcs/gimmick/spawner.rs` |
| `GimmickTemplate.cs` | GimmickTemplate | ported | `GimmickTemplate` → `aaemu-game/src/systems/npcs/gimmick/mod.rs` |

### `AAEmu.Game/Models/Game/Housing`

| File | Types | Status | Rust |
|---|---|---|---|
| `House.cs` | House | ported | `House` → `aaemu-game/src/models/house.rs` |
| `HouseTax.cs` | HouseTax | dropped | `HouseTax` dropped: only referenced in comments |
| `HousingAreas.cs` | HousingAreas | dropped | `HousingAreas` dropped: only referenced in comments |
| `HousingBindingDoodad.cs` | HousingBindingDoodad | ported | `HousingBindingDoodad` → `aaemu-game/src/systems/housing/data.rs` |
| `HousingBindingTemplate.cs` | HousingBindingTemplate | ported | `HousingBindingTemplate` → `aaemu-game/src/systems/housing/data.rs` |
| `HousingBuildStep.cs` | HousingBuildStep | ported | `HousingBuildStep` → `aaemu-game/src/systems/housing/data.rs` |
| `HousingDecoration.cs` | HousingDecoration | ported | `HousingDecoration` → `aaemu-game/src/systems/housing/data.rs` |
| `HousingItemHousings.cs` | HousingItemHousings | ported | `HousingItemHousings` → `aaemu-game/src/systems/housing/data.rs` |
| `HousingPermission.cs` | HousingPermission | ported | `HousingPermission` → `aaemu-game/src/models/house.rs` |
| `HousingTemplate.cs` | HousingTemplate | ported | `HousingTemplate` → `aaemu-game/src/systems/housing/data.rs` |
| `ItemHousingDecoration.cs` | ItemHousingDecoration | ported | `ItemHousingDecoration` → `aaemu-game/src/systems/housing/data.rs` |

### `AAEmu.Game/Models/Game/Indun`

| File | Types | Status | Rust |
|---|---|---|---|
| `Dungeon.cs` | Dungeon | ported | `Dungeon` → `aaemu-game/src/systems/instances/dungeon.rs` |
| `DungeonLoaderTask.cs` | DungeonLoaderTask | merged | `DungeonLoaderTask` merged into `aaemu-game/src/systems/instances/dungeon.rs` |
| `IndunRoom.cs` | IndunRoom | ported | `IndunRoom` → `aaemu-game/src/systems/instances/data.rs` |
| `IndunZone.cs` | IndunZone | ported | `IndunZone` → `aaemu-game/src/systems/instances/data.rs` |

### `AAEmu.Game/Models/Game/Indun/Actions`

| File | Types | Status | Rust |
|---|---|---|---|
| `IndunAction.cs` | IndunAction | ported | `IndunAction` → `aaemu-game/src/systems/instances/data.rs` |
| `IndunActionChangeDoodadPhases.cs` | IndunActionChangeDoodadPhases | merged | `IndunActionChangeDoodadPhases` merged into `aaemu-game/src/systems/instances/data.rs` |
| `IndunActionNpcSpawner.cs` | IndunActionNpcSpawner | merged | `IndunActionNpcSpawner` merged into `aaemu-game/src/systems/instances/data.rs` |
| `IndunActionRemoveTaggedNpcs.cs` | IndunActionRemoveTaggedNpcs | merged | `IndunActionRemoveTaggedNpcs` merged into `aaemu-game/src/systems/instances/data.rs` |
| `IndunActionSetRoomCleareds.cs` | IndunActionSetRoomCleareds | merged | `IndunActionSetRoomCleareds` merged into `aaemu-game/src/systems/instances/data.rs` |

### `AAEmu.Game/Models/Game/Indun/Events`

| File | Types | Status | Rust |
|---|---|---|---|
| `IndunEvent.cs` | IndunEvent | ported | `IndunEvent` → `aaemu-game/src/systems/instances/data.rs` |
| `IndunEventDoodadSpawneds.cs` | IndunEventDoodadSpawneds | merged | `IndunEventDoodadSpawneds` merged into `aaemu-game/src/systems/instances/events.rs` |
| `IndunEventNoAliveChInRooms.cs` | IndunEventNoAliveChInRooms | merged | `IndunEventNoAliveChInRooms` merged into `aaemu-game/src/systems/instances/events.rs` |
| `IndunEventNpcCombatEndeds.cs` | IndunEventNpcCombatEndeds | merged | `IndunEventNpcCombatEndeds` merged into `aaemu-game/src/systems/instances/data.rs` |
| `IndunEventNpcCombatStarteds.cs` | IndunEventNpcCombatStarteds | merged | `IndunEventNpcCombatStarteds` merged into `aaemu-game/src/systems/instances/data.rs` |
| `IndunEventNpcKilleds.cs` | IndunEventNpcKilleds | merged | `IndunEventNpcKilleds` merged into `aaemu-game/src/systems/instances/events.rs` |
| `IndunEventNpcSpawneds.cs` | IndunEventNpcSpawneds | merged | `IndunEventNpcSpawneds` merged into `aaemu-game/src/systems/instances/events.rs` |

### `AAEmu.Game/Models/Game/InstantGame`

| File | Types | Status | Rust |
|---|---|---|---|
| `Battlefield.cs` | Battlefield | ported | `Battlefield` → `aaemu-game/src/systems/instances/data.rs` |
| `BattlefieldSpawns.cs` | BattlefieldSpawns | ported | `BattlefieldSpawns` → `aaemu-game/src/systems/instances/data.rs` |
| `GameRuleSet.cs` | GameRuleSet | ported | `GameRuleSet` → `aaemu-game/src/systems/instances/data.rs` |
| `InstantGame.cs` | InstantGame | ported | `InstantGame` → `aaemu-game/src/systems/instances/instant_game.rs` |
| `InstantGameEvents.cs` | InstantGame | ported | `InstantGame` → `aaemu-game/src/systems/instances/instant_game.rs` |
| `InstantGameTeamMember.cs` | InstantGameTeamMember | merged | `InstantGameTeamMember` merged into `aaemu-game/src/systems/instances/instant_game.rs` |
| `InstantGameTeamResult.cs` | InstantGameTeamResult | merged | `InstantGameTeamResult` merged into `aaemu-game/src/systems/instances/instant_game.rs` |
| `MatchmakingApplicant.cs` | MatchmakingApplicant | merged | `MatchmakingApplicant` merged into `aaemu-game/src/systems/instances/instant_game.rs` |

### `AAEmu.Game/Models/Game/InstantGame/Static`

| File | Types | Status | Rust |
|---|---|---|---|
| `BattlefieldEndingReason.cs` | BattlefieldEndingReason | merged | `BattlefieldEndingReason` merged into `aaemu-game/src/systems/instances/instant_game.rs` |
| `GameScoreEventKind.cs` | GameScoreEventKind | dropped | `GameScoreEventKind` dropped: unused |
| `InstantCorps.cs` | InstantCorps | merged | `InstantCorps` merged into `aaemu-game/src/systems/instances/instant_game.rs` |
| `InstantGameType.cs` | InstantGameType | merged | `InstantGameType` merged into `aaemu-game/src/systems/instances/instant_game.rs` |
| `VictoryState.cs` | VictoryState | ported | `VictoryState` → `aaemu-game/src/systems/instances/instant_game.rs` |

### `AAEmu.Game/Models/Game/Items`

| File | Types | Status | Rust |
|---|---|---|---|
| `AbilityItems.cs` | AbilityItems | ported | `AbilityItems` → `aaemu-game/src/data/character_templates.rs` |
| `AbilitySupplyItem.cs` | AbilitySupplyItem | ported | `AbilitySupplyItem` → `aaemu-game/src/data/character_templates.rs` |
| `Accessory.cs` | Accessory | merged | `Accessory` merged into `aaemu-game/src/models/item.rs` |
| `Armor.cs` | Armor | merged | `Armor` merged into `aaemu-game/src/models/item.rs` |
| `ArmorGradeBuff.cs` | ArmorGradeBuff | ported | `ArmorGradeBuff` → `aaemu-game/src/data/items.rs` |
| `ArmorType.cs` | ArmorType | ported | `ArmorType` → `aaemu-game/src/systems/economy/bridges.rs` |
| `AuctionSettings.cs` | AuctionSettings | merged | `AuctionSettings` merged into `aaemu-game/src/systems/economy/auction.rs` |
| `Backpack.cs` | Backpack | ported | `Backpack` → `aaemu-game/src/systems/npcs/specialty.rs` |
| `BigFish.cs` | BigFish | merged | `BigFish` merged into `aaemu-game/src/models/item.rs` |
| `BodyPart.cs` | BodyPart | ported | `BodyPart` → `aaemu-game/src/systems/npcs/npc/manager.rs` |
| `EquipItem.cs` | EquipItem | ported | `EquipItem` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `EquipItemsTemplate.cs` | EquipItemsTemplate | ported | `EquipItemsTemplate` → `aaemu-game/src/systems/npcs/npc/data.rs` |
| `EquipSlotEnchantingCost.cs` | EquipSlotEnchantingCost | ported | `EquipSlotEnchantingCost` → `aaemu-game/src/data/items.rs` |
| `EquipmentItemSlot.cs` | EquipmentItemSlot | merged | `EquipmentItemSlot` merged into `aaemu-game/src/models/container.rs` |
| `EquipmentItemSlotType.cs` | EquipmentItemSlotType | ported | `EquipmentItemSlotType` → `aaemu-game/src/data/items.rs` |
| `Expand.cs` | Expand | ported | `Expand` → `aaemu-game/src/systems/services/expands.rs` |
| `GradeDistributions.cs` | GradeDistributions | ported | `GradeDistributions` → `aaemu-game/src/data/items.rs` |
| `Holdable.cs` | Holdable | ported | `Holdable` → `aaemu-game/src/data/items.rs` |
| `Item.cs` | Item | ported | `Item` → `aaemu-game/src/models/item.rs` |
| `ItemAndLocation.cs` | ItemAndLocation | ported | `ItemAndLocation` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `ItemCapScale.cs` | ItemCapScale | ported | `ItemCapScale` → `aaemu-game/src/data/items.rs` |
| `ItemCategory.cs` | ItemCategory | ported | `ItemCategory` → `aaemu-game/src/data/items.rs` |
| `ItemConfig.cs` | ItemConfig | ported | `ItemConfig` → `aaemu-game/src/data/items.rs` |
| `ItemConversionProduct.cs` | ItemConversionProduct | ported | `ItemConversionProduct` → `aaemu-game/src/systems/economy/items_data.rs` |
| `ItemConversionReagent.cs` | ItemConversionReagent | ported | `ItemConversionReagent` → `aaemu-game/src/systems/economy/items_data.rs` |
| `ItemCreationDefinition.cs` | ItemCreationDefinition | ported | `ItemCreationDefinition` → `aaemu-game/src/systems/economy/mail_kinds.rs` |
| `ItemDetailType.cs` | ItemDetailType | merged | `ItemDetailType` merged into `aaemu-game/src/models/item.rs` |
| `ItemFlag.cs` | ItemFlag | merged | `ItemFlag` merged into `aaemu-game/src/models/item.rs` |
| `ItemGrade.cs` | ItemGrade | ported | `ItemGrade` → `aaemu-game/src/data/items.rs` |
| `ItemGradeEnchantingSupport.cs` | ItemGradeEnchantingSupport | ported | `ItemGradeEnchantingSupport` → `aaemu-game/src/data/items.rs` |
| `ItemIdAndLocation.cs` | ItemIdAndLocation | ported | `ItemIdAndLocation` → `aaemu-game/src/systems/economy/mail_packets.rs` |
| `ItemLocation.cs` | ItemLocation | dropped | `ItemLocation` dropped: unused |
| `ItemLookConvert.cs` | ItemLookConvert | ported | `ItemLookConvert` → `aaemu-game/src/data/items.rs` |
| `MusicSheetItem.cs` | MusicSheetItem | merged | `MusicSheetItem` merged into `aaemu-game/src/models/item.rs` |
| `Rune.cs` | Rune | merged | `Rune` merged into `aaemu-game/src/models/item.rs` |
| `ShopCurrencyType.cs` | ShopCurrencyType | ported | `ShopCurrencyType` → `aaemu-game/src/systems/economy/store.rs` |
| `SlotType.cs` | SlotType | ported | `SlotType` → `aaemu-game/src/models/container.rs` |
| `SummonMate.cs` | SummonMate | merged | `SummonMate` merged into `aaemu-game/src/models/item.rs` |
| `SummonSlave.cs` | SummonSlave | merged | `SummonSlave` merged into `aaemu-game/src/models/item.rs` |
| `TreasureMap.cs` | TreasureMap | merged | `TreasureMap` merged into `aaemu-game/src/models/item.rs` |
| `UccItem.cs` | UccItem | merged | `UccItem` merged into `aaemu-game/src/models/item.rs` |
| `Weapon.cs` | Weapon | ported | `Weapon` → `aaemu-game/src/systems/skills/stats.rs` |
| `Wearable.cs` | Wearable | ported | `Wearable` → `aaemu-game/src/data/items.rs` |
| `WearableKind.cs` | WearableKind | ported | `WearableKind` → `aaemu-game/src/data/items.rs` |
| `WearableSlot.cs` | WearableSlot | ported | `WearableSlot` → `aaemu-game/src/data/items.rs` |

### `AAEmu.Game/Models/Game/Items/Actions`

| File | Types | Status | Rust |
|---|---|---|---|
| `AAPointUpdate.cs` | AAPointUpdate | merged | `AAPointUpdate` merged into `aaemu-game/src/models/item_task.rs` |
| `ChangeAutoUseAAPoint.cs` | ChangeAutoUseAAPoint | merged | `ChangeAutoUseAAPoint` merged into `aaemu-game/src/models/item_task.rs` |
| `ChangeBankAAPoint.cs` | ChangeBankAAPoint | merged | `ChangeBankAAPoint` merged into `aaemu-game/src/models/item_task.rs` |
| `ChangeGamePoint.cs` | ChangeGamePoint | merged | `ChangeGamePoint` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemAction.cs` | ItemAction | ported | `ItemAction` → `aaemu-game/src/models/item_task.rs` |
| `ItemAdd.cs` | ItemAdd | merged | `ItemAdd` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemAddNew.cs` | ItemAddNew | merged | `ItemAddNew` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemBuyback.cs` | ItemBuyback | merged | `ItemBuyback` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemCountUpdate.cs` | ItemCountUpdate | merged | `ItemCountUpdate` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemGradeChange.cs` | ItemGradeChange | merged | `ItemGradeChange` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemMove.cs` | ItemMove | merged | `ItemMove` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemRemove.cs` | ItemRemove | merged | `ItemRemove` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemRemoveCrafting.cs` | ItemRemoveCrafting | merged | `ItemRemoveCrafting` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemRemoveSlot.cs` | ItemRemoveSlot | merged | `ItemRemoveSlot` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemTask.cs` | ItemTask | ported | `ItemTask` → `aaemu-game/src/models/item_task.rs` |
| `ItemTaskType.cs` | ItemTaskType | ported | `ItemTaskType` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `ItemUpdate.cs` | ItemUpdate | ported | `ItemUpdate` → `aaemu-game/src/systems/vehicles/packets.rs` |
| `ItemUpdateBits.cs` | ItemUpdateBits | merged | `ItemUpdateBits` merged into `aaemu-game/src/models/item_task.rs` |
| `ItemUpdateSecurity.cs` | ItemUpdateSecurity | merged | `ItemUpdateSecurity` merged into `aaemu-game/src/models/item_task.rs` |
| `MoneyChange.cs` | MoneyChange | merged | `MoneyChange` merged into `aaemu-game/src/models/item_task.rs` |
| `MoneyChangeBank.cs` | MoneyChangeBank | merged | `MoneyChangeBank` merged into `aaemu-game/src/models/item_task.rs` |

### `AAEmu.Game/Models/Game/Items/Containers`

| File | Types | Status | Rust |
|---|---|---|---|
| `ChestType.cs` | ChestType | merged | `ChestType` merged into `aaemu-game/src/models/container.rs` |
| `CofferContainer.cs` | CofferContainer | merged | `CofferContainer` merged into `aaemu-game/src/models/container.rs` |
| `EquipmentContainer.cs` | EquipmentContainer | merged | `EquipmentContainer` merged into `aaemu-game/src/models/container.rs` |
| `ItemContainer.cs` | ItemContainer | ported | `ItemContainer` → `aaemu-game/src/models/container.rs` |
| `LootingContainer.cs` | LootingContainer | ported | `LootingContainer` → `aaemu-game/src/systems/economy/looting.rs` |
| `MateEquipmentContainer.cs` | MateEquipmentContainer | merged | `MateEquipmentContainer` merged into `aaemu-game/src/models/container.rs` |

### `AAEmu.Game/Models/Game/Items/Loots`

| File | Types | Status | Rust |
|---|---|---|---|
| `Loot.cs` | Loot | ported | `Loot` → `aaemu-game/src/systems/economy/loot_data.rs` |
| `LootActabilityGroups.cs` | LootActabilityGroups | ported | `LootActabilityGroups` → `aaemu-game/src/systems/economy/loot_data.rs` |
| `LootGroups.cs` | LootGroups | ported | `LootGroups` → `aaemu-game/src/systems/economy/loot_data.rs` |
| `LootOwnerType.cs` | LootOwnerType | ported | `LootOwnerType` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `LootPack.cs` | LootPack | ported | `LootPack` → `aaemu-game/src/systems/economy/loot_data.rs` |
| `LootingContainerItemEntry.cs` | LootingContainerItemEntry | merged | `LootingContainerItemEntry` merged into `aaemu-game/src/systems/economy/looting.rs` |
| `lootPackConvertFish.cs` | LootPackConvertFish | ported | `LootPackConvertFish` → `aaemu-game/src/data/items.rs` |
| `lootPackDroppingNpc.cs` | LootPackDroppingNpc | ported | `LootPackDroppingNpc` → `aaemu-game/src/data/items.rs` |

### `AAEmu.Game/Models/Game/Items/Procs`

| File | Types | Status | Rust |
|---|---|---|---|
| `ItemProc.cs` | ItemProc | ported | `ItemProc` → `aaemu-game/src/models/item.rs` |
| `ItemProcTemplate.cs` | ItemProcTemplate | ported | `ItemProcTemplate` → `aaemu-game/src/data/items.rs` |
| `ProcChanceKind.cs` | ProcChanceKind | ported | `ProcChanceKind` → `aaemu-game/src/data/items.rs` |

### `AAEmu.Game/Models/Game/Items/Templates`

| File | Types | Status | Rust |
|---|---|---|---|
| `AccessoryTemplate.cs` | AccessoryTemplate | merged | `AccessoryTemplate` merged into `aaemu-game/src/data/items.rs` |
| `ArmorTemplate.cs` | ArmorTemplate | merged | `ArmorTemplate` merged into `aaemu-game/src/data/items.rs` |
| `AttributeModifiers.cs` | AttributeModifiers | ported | `AttributeModifiers` → `aaemu-game/src/data/items.rs` |
| `BackpackTemplate.cs` | BackpackTemplate | ported | `BackpackTemplate` → `aaemu-game/src/data/items.rs` |
| `BackpackType.cs` | BackpackType | ported | `BackpackType` → `aaemu-game/src/data/items.rs` |
| `BodyPartTemplate.cs` | BodyPartTemplate | ported | `BodyPartTemplate` → `aaemu-game/src/systems/npcs/npc/data.rs` |
| `EquipBodyPartPack.cs` | EquipBodyPartPack | ported | `EquipBodyPartPack` → `aaemu-game/src/systems/npcs/npc/data.rs` |
| `EquipItemSet.cs` | EquipItemSet | ported | `EquipItemSet` → `aaemu-game/src/data/items.rs` |
| `EquipItemSetBonus.cs` | EquipItemSetBonus | ported | `EquipItemSetBonus` → `aaemu-game/src/data/items.rs` |
| `EquipItemTemplate.cs` | EquipItemTemplate | merged | `EquipItemTemplate` merged into `aaemu-game/src/data/items.rs` |
| `GradeTemplate.cs` | GradeTemplate | ported | `GradeTemplate` → `aaemu-game/src/data/items.rs` |
| `ItemBindType.cs` | ItemBindType | merged | `ItemBindType` merged into `aaemu-game/src/data/items.rs` |
| `ItemDoodadTemplate.cs` | ItemDoodadTemplate | merged | `ItemDoodadTemplate` merged: data/items.rs template kinds |
| `ItemSet.cs` | ItemSet, ItemSetItem | ported | `ItemSet` → `aaemu-game/src/data/items.rs`; `ItemSetItem` → `aaemu-game/src/data/items.rs` |
| `ItemTemplate.cs` | ItemTemplate | ported | `ItemTemplate` → `aaemu-game/src/data/items.rs` |
| `MusicSheetTemplate.cs` | MusicSheetTemplate | merged | `MusicSheetTemplate` merged: data/items.rs template kinds |
| `RuneTemplate.cs` | RuneTemplate | ported | `RuneTemplate` → `aaemu-game/src/data/items.rs` |
| `SummonMateTemplate.cs` | SummonMateTemplate | merged | `SummonMateTemplate` merged into `aaemu-game/src/systems/vehicles/mates.rs` |
| `SummonSlaveTemplate.cs` | SummonSlaveTemplate | merged | `SummonSlaveTemplate` merged: data/items.rs template kinds |
| `TreasureMapTemplate.cs` | TreasureMapTemplate | merged | `TreasureMapTemplate` merged: data/items.rs template kinds |
| `UccTemplate.cs` | UccTemplate | merged | `UccTemplate` merged: data/items.rs template kinds |
| `WeaponTemplate.cs` | WeaponTemplate | merged | `WeaponTemplate` merged into `aaemu-game/src/data/items.rs` |

### `AAEmu.Game/Models/Game/Mails`

| File | Types | Status | Rust |
|---|---|---|---|
| `BaseMail.cs` | BaseMail | ported | `BaseMail` → `aaemu-game/src/systems/economy/mail.rs` |
| `CommercialMail.cs` | CommercialMail | ported | `CommercialMail` → `aaemu-game/src/systems/economy/mail_kinds.rs` |
| `CommercialMailTypes.cs` | CommercialMailTypes | ported | `CommercialMailTypes` → `aaemu-game/src/systems/economy/mail.rs` |
| `CountUnreadMail.cs` | CountUnreadMail | ported | `CountUnreadMail` → `aaemu-game/src/models/char_data.rs` |
| `MailBody.cs` | MailBody | ported | `MailBody` → `aaemu-game/src/systems/economy/mail.rs` |
| `MailForAuction.cs` | MailForAuction | ported | `MailForAuction` → `aaemu-game/src/systems/economy/mail_kinds.rs` |
| `MailForSpeciality.cs` | MailForSpeciality | ported | `MailForSpeciality` → `aaemu-game/src/systems/economy/mail_kinds.rs` |
| `MailForTax.cs` | MailForTax | ported | `MailForTax` → `aaemu-game/src/systems/economy/mail_kinds.rs` |
| `MailHeader.cs` | MailHeader | ported | `MailHeader` → `aaemu-game/src/systems/economy/mail.rs` |
| `MailPlayerToPlayer.cs` | MailPlayerToPlayer | ported | `MailPlayerToPlayer` → `aaemu-game/src/systems/economy/mail_kinds.rs` |
| `MailResult.cs` | MailResult | ported | `MailResult` → `aaemu-game/src/systems/economy/mail.rs` |
| `MailStatus.cs` | MailStatus | ported | `MailStatus` → `aaemu-game/src/systems/economy/mail.rs` |
| `MailType.cs` | MailType | ported | `MailType` → `aaemu-game/src/systems/economy/mail.rs` |
| `MiaMailTypes.cs` | MiaMailTypes | ported | `MiaMailTypes` → `aaemu-game/src/systems/economy/mail.rs` |

### `AAEmu.Game/Models/Game/Mate`

| File | Types | Status | Rust |
|---|---|---|---|
| `MountAttachedSkills.cs` | MountAttachedSkills | ported | `MountAttachedSkills` → `aaemu-game/src/systems/vehicles/data.rs` |
| `MountSkills.cs` | MountSkills | ported | `MountSkills` → `aaemu-game/src/systems/vehicles/data.rs` |
| `NpcMountSkills.cs` | NpcMountSkills | ported | `NpcMountSkills` → `aaemu-game/src/systems/vehicles/data.rs` |

### `AAEmu.Game/Models/Game/Merchant`

| File | Types | Status | Rust |
|---|---|---|---|
| `MerchantGoods.cs` | MerchantGoods, MerchantGoodsItem | ported | `MerchantGoods` → `aaemu-game/src/systems/npcs/npc/data.rs`; `MerchantGoodsItem` → `aaemu-game/src/systems/npcs/npc/data.rs` |

### `AAEmu.Game/Models/Game/Models`

| File | Types | Status | Rust |
|---|---|---|---|
| `ActorModel.cs` | ActorModel | ported | `ActorModel` → `aaemu-game/src/systems/ai/data.rs` |
| `GameStance.cs` | GameStance | ported | `GameStance` → `aaemu-game/src/systems/services/models.rs` |
| `GameStanceType.cs` | GameStanceType | ported | `GameStanceType` → `aaemu-game/src/systems/services/models.rs` |
| `Model.cs` | Model | ported | `Model` → `aaemu-game/src/systems/ai/physics/ship.rs` |
| `ModelType.cs` | ModelType | ported | `ModelType` → `aaemu-game/src/systems/npcs/npc/data.rs` |
| `ShipModelV1.cs` | ShipModelV1 | merged | `ShipModelV1` merged into `aaemu-game/src/systems/ai/data.rs` |
| `VehicleModel.cs` | VehicleModel | ported | `VehicleModel` → `aaemu-game/src/systems/services/models.rs` |

### `AAEmu.Game/Models/Game/Music`

| File | Types | Status | Rust |
|---|---|---|---|
| `SongData.cs` | SongData | ported | `SongData` → `aaemu-game/src/systems/services/music.rs` |

### `AAEmu.Game/Models/Game/NPChar`

| File | Types | Status | Rust |
|---|---|---|---|
| `Aggro.cs` | Aggro | ported | `Aggro` → `aaemu-game/src/systems/skills/state.rs` |
| `AggroExtensions.cs` | AggroExtensions | dropped | `AggroExtensions` dropped: only caller is commented out (Npc.cs) |
| `AggroKind.cs` | AggroKind | ported | `AggroKind` → `aaemu-game/src/systems/skills/state.rs` |
| `AggroLinkSpecialRuleKind.cs` | AggroLinkSpecialRuleKind | merged | `AggroLinkSpecialRuleKind` merged into `aaemu-game/src/systems/npcs/npc/data.rs` |
| `Npc.cs` | Npc | ported | `Npc` → `aaemu-game/src/models/npc.rs` |
| `NpcEvents.cs` | Npc | ported | `Npc` → `aaemu-game/src/models/npc.rs` |
| `NpcGradeType.cs` | NpcGradeType | merged | `NpcGradeType` merged into `aaemu-game/src/systems/npcs/npc/data.rs` |
| `NpcGroupAggroRuleKind.cs` | NpcGroupAggroRuleKind | dropped | `NpcGroupAggroRuleKind` dropped: unused enum |
| `NpcKindType.cs` | NpcKindType | merged | `NpcKindType` merged into `aaemu-game/src/systems/npcs/npc/data.rs` |
| `NpcPassiveBuff.cs` | NpcPassiveBuff | ported | `NpcPassiveBuff` → `aaemu-game/src/systems/npcs/npc/data.rs` |
| `NpcPosture.cs` | NpcPosture | ported | `NpcPosture` → `aaemu-game/src/systems/npcs/npc/data.rs` |
| `NpcSkills.cs` | NpcSkill | ported | `NpcSkill` → `systems/npcs/npc/data.rs` |
| `NpcSpawnType.cs` | NpcSpawnType | dropped | `NpcSpawnType` dropped: unused enum |
| `NpcSpawner.cs` | NpcSpawner, SpawnerPlayerInRadiusCache, SpawnerPlayerCountCache, SpawnerNpcsInZoneCache | ported | `NpcSpawner` → `aaemu-game/src/systems/npcs/npc/spawner.rs`; `SpawnerPlayerInRadiusCache` merged: systems/npcs/npc/spawner.rs cache tuples; `SpawnerPlayerCountCache` merged: systems/npcs/npc/spawner.rs cache tuples; `SpawnerNpcsInZoneCache` merged: systems/npcs/npc/spawner.rs cache tuples |
| `NpcSpawnerCategory.cs` | NpcSpawnerCategory | ported | `NpcSpawnerCategory` → `aaemu-game/src/systems/npcs/npc/spawner_data.rs` |
| `NpcSpawnerNpc.cs` | NpcSpawnerNpc | ported | `NpcSpawnerNpc` → `aaemu-game/src/systems/npcs/npc/spawner_data.rs` |
| `NpcSpawnerTemplate.cs` | NpcSpawnerTemplate | ported | `NpcSpawnerTemplate` → `aaemu-game/src/systems/npcs/npc/spawner_data.rs` |
| `NpcTemplate.cs` | NpcTemplate | ported | `NpcTemplate` → `aaemu-game/src/systems/npcs/npc/data.rs` |
| `NpcTemplateType.cs` | NpcTemplateType | merged | `NpcTemplateType` merged into `aaemu-game/src/systems/npcs/npc/data.rs` |
| `Tagging.cs` | Tagging | ported | `Tagging` → `aaemu-game/src/systems/skills/state.rs` |
| `TotalCharacterCustom.cs` | TotalCharacterCustom | ported | `TotalCharacterCustom` → `aaemu-game/src/systems/npcs/npc/data.rs` |

### `AAEmu.Game/Models/Game/NpcGroup`

| File | Types | Status | Rust |
|---|---|---|---|
| `NpcGroup.cs` | NpcGroup | merged | `NpcGroup` merged into `aaemu-game/src/systems/npcs/npc/spawner.rs` |
| `NpcGroupMember.cs` | NpcGroupMember | dropped | `NpcGroupMember` dropped: loaded by reflection loader, getters never called |

### `AAEmu.Game/Models/Game/OpenPortal`

| File | Types | Status | Rust |
|---|---|---|---|
| `OpenPortalEffects.cs` | OpenPortalEffects | dropped | `OpenPortalEffects` dropped: unused |
| `OpenPortalReagents.cs` | OpenPortalReagents | ported | `OpenPortalReagents` → `aaemu-game/src/systems/npcs/portal/data.rs` |

### `AAEmu.Game/Models/Game/Quests`

| File | Types | Status | Rust |
|---|---|---|---|
| `CompletedQuest.cs` | CompletedQuest | ported | `CompletedQuest` → `aaemu-game/src/models/char_data.rs` |
| `IQuestAct.cs` | IQuestAct | merged | `IQuestAct` merged into `aaemu-game/src/systems/quests/engine.rs` |
| `IQuestComponent.cs` | IQuestComponent | merged | `IQuestComponent` merged into `aaemu-game/src/systems/quests/engine.rs` |
| `NewQuestCode.cs` | Quest | ported | `Quest` → `aaemu-game/src/systems/quests/state.rs` |
| `Quest.cs` | Quest | ported | `Quest` → `aaemu-game/src/systems/quests/state.rs` |
| `QuestAct.cs` | QuestAct | merged | `QuestAct` merged into `aaemu-game/src/systems/quests/engine.rs` |
| `QuestComponent.cs` | QuestComponent | ported | `QuestComponent` → `aaemu-game/src/systems/quests/bridges.rs` |
| `QuestStep.cs` | QuestStep | ported | `QuestStep` → `aaemu-game/src/systems/quests/unit_reqs.rs` |
| `QuestSupplies.cs` | QuestSupplies | ported | `QuestSupplies` → `aaemu-game/src/systems/quests/model.rs` |

### `AAEmu.Game/Models/Game/Quests/Acts`

| File | Types | Status | Rust |
|---|---|---|---|
| `QuestActCheckCompleteComponent.cs` | QuestActCheckCompleteComponent | merged | `QuestActCheckCompleteComponent` merged into `aaemu-game/src/systems/quests/acts.rs` |
| `QuestActCheckGuard.cs` | QuestActCheckGuard | merged | `QuestActCheckGuard` merged into `aaemu-game/src/systems/quests/acts.rs` |
| `QuestActCheckSphere.cs` | QuestActCheckSphere | merged | `QuestActCheckSphere` merged into `aaemu-game/src/systems/quests/tests.rs` |
| `QuestActCheckTimer.cs` | QuestActCheckTimer | merged | `QuestActCheckTimer` merged into `aaemu-game/src/systems/quests/acts.rs` |
| `QuestActConAcceptComponent.cs` | QuestActConAcceptComponent | merged | `QuestActConAcceptComponent` merged into `aaemu-game/src/systems/quests/tests.rs` |
| `QuestActConAcceptDoodad.cs` | QuestActConAcceptDoodad | merged | `QuestActConAcceptDoodad` merged into `aaemu-game/src/systems/quests/tests.rs` |
| `QuestActConAcceptItem.cs` | QuestActConAcceptItem | merged | `QuestActConAcceptItem` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConAcceptItemGain.cs` | QuestActConAcceptItemGain | merged | `QuestActConAcceptItemGain` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConAcceptLevelUp.cs` | QuestActConAcceptLevelUp | merged | `QuestActConAcceptLevelUp` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConAcceptNpc.cs` | QuestActConAcceptNpc | merged | `QuestActConAcceptNpc` merged into `aaemu-game/src/systems/quests/tests.rs` |
| `QuestActConAcceptNpcEmotion.cs` | QuestActConAcceptNpcEmotion | merged | `QuestActConAcceptNpcEmotion` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConAcceptNpcKill.cs` | QuestActConAcceptNpcKill | merged | `QuestActConAcceptNpcKill` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConAcceptSkill.cs` | QuestActConAcceptSkill | merged | `QuestActConAcceptSkill` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConAcceptSphere.cs` | QuestActConAcceptSphere | merged | `QuestActConAcceptSphere` merged into `aaemu-game/src/systems/npcs/hooks.rs` |
| `QuestActConAutoComplete.cs` | QuestActConAutoComplete | merged | `QuestActConAutoComplete` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConReportDoodad.cs` | QuestActConReportDoodad | merged | `QuestActConReportDoodad` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConReportJournal.cs` | QuestActConReportJournal | merged | `QuestActConReportJournal` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConReportNpc.cs` | QuestActConReportNpc | merged | `QuestActConReportNpc` merged into `aaemu-game/src/systems/quests/tests.rs` |
| `QuestActEtcItemObtain.cs` | QuestActEtcItemObtain | merged | `QuestActEtcItemObtain` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjAbilityLevel.cs` | QuestActObjAbilityLevel | merged | `QuestActObjAbilityLevel` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjAggro.cs` | QuestActObjAggro | merged | `QuestActObjAggro` merged into `aaemu-game/src/systems/quests/events.rs` |
| `QuestActObjAlias.cs` | QuestActObjAlias | dropped | `QuestActObjAlias` dropped: never built by a loader, RunAct returns true |
| `QuestActObjCinema.cs` | QuestActObjCinema | merged | `QuestActObjCinema` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjCompleteQuest.cs` | QuestActObjCompleteQuest | merged | `QuestActObjCompleteQuest` merged into `aaemu-game/src/systems/quests/acts.rs` |
| `QuestActObjCraft.cs` | QuestActObjCraft | merged | `QuestActObjCraft` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjDistance.cs` | QuestActObjDistance | merged | `QuestActObjDistance` merged into `aaemu-game/src/systems/quests/acts.rs` |
| `QuestActObjExpressFire.cs` | QuestActObjExpressFire | merged | `QuestActObjExpressFire` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjInteraction.cs` | QuestActObjInteraction | merged | `QuestActObjInteraction` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjItemGather.cs` | QuestActObjItemGather | merged | `QuestActObjItemGather` merged into `aaemu-game/src/systems/quests/acts.rs` |
| `QuestActObjItemUse.cs` | QuestActObjItemUse | merged | `QuestActObjItemUse` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjLevel.cs` | QuestActObjLevel | merged | `QuestActObjLevel` merged into `aaemu-game/src/systems/quests/acts.rs` |
| `QuestActObjMateLevel.cs` | QuestActObjMateLevel | merged | `QuestActObjMateLevel` merged into `aaemu-game/src/systems/quests/acts.rs` |
| `QuestActObjMonsterGroupHunt.cs` | QuestActObjMonsterGroupHunt | merged | `QuestActObjMonsterGroupHunt` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjMonsterHunt.cs` | QuestActObjMonsterHunt | merged | `QuestActObjMonsterHunt` merged into `aaemu-game/src/systems/quests/manager_tests.rs` |
| `QuestActObjSphere.cs` | QuestActObjSphere | merged | `QuestActObjSphere` merged into `aaemu-game/src/systems/quests/tests.rs` |
| `QuestActObjTalk.cs` | QuestActObjTalk | merged | `QuestActObjTalk` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjTalkNpcGroup.cs` | QuestActObjTalkNpcGroup | merged | `QuestActObjTalkNpcGroup` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjZoneKill.cs` | QuestActObjZoneKill | merged | `QuestActObjZoneKill` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjZoneMonsterHunt.cs` | QuestActObjZoneMonsterHunt | merged | `QuestActObjZoneMonsterHunt` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyAppellation.cs` | QuestActSupplyAppellation | merged | `QuestActSupplyAppellation` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyCopper.cs` | QuestActSupplyCopper | merged | `QuestActSupplyCopper` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyCrimePoint.cs` | QuestActSupplyCrimePoint | merged | `QuestActSupplyCrimePoint` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyExp.cs` | QuestActSupplyExp | merged | `QuestActSupplyExp` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyHonorPoint.cs` | QuestActSupplyHonorPoint | merged | `QuestActSupplyHonorPoint` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyItem.cs` | QuestActSupplyItem | merged | `QuestActSupplyItem` merged into `aaemu-game/src/systems/quests/tests.rs` |
| `QuestActSupplyJuryPoint.cs` | QuestActSupplyJuryPoint | merged | `QuestActSupplyJuryPoint` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyLivingPoint.cs` | QuestActSupplyLivingPoint | merged | `QuestActSupplyLivingPoint` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyLp.cs` | QuestActSupplyLp | merged | `QuestActSupplyLp` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyRemoveItem.cs` | QuestActSupplyRemoveItem | merged | `QuestActSupplyRemoveItem` merged into `aaemu-game/src/systems/quests/acts.rs` |
| `QuestActSupplySelectiveItem.cs` | QuestActSupplySelectiveItem | merged | `QuestActSupplySelectiveItem` merged into `aaemu-game/src/systems/quests/data.rs` |

### `AAEmu.Game/Models/Game/Quests/Static`

| File | Types | Status | Rust |
|---|---|---|---|
| `QuestAcceptorType.cs` | QuestAcceptorType | merged | `QuestAcceptorType` merged into `aaemu-game/src/systems/quests/model.rs` |
| `QuestActObjInviteType.cs` | QuestActObjInviteType | dropped | `QuestActObjInviteType` dropped: zero references |
| `QuestCompleteKind.cs` | QuestCompleteKind | dropped | `QuestCompleteKind` dropped: zero references |
| `QuestComponentKind.cs` | QuestComponentKind | merged | `QuestComponentKind` merged into `aaemu-game/src/systems/quests/model.rs` |
| `QuestConditionObj.cs` | QuestConditionObj | merged | `QuestConditionObj` merged into `aaemu-game/src/systems/quests/model.rs` |
| `QuestContextStatus.cs` | QuestContextStatus | merged | `QuestContextStatus` merged into `aaemu-game/src/systems/quests/model.rs` |
| `QuestContextTextKind.cs` | QuestContextTextKind | dropped | `QuestContextTextKind` dropped: zero references |
| `QuestDetail.cs` | QuestDetail | merged | `QuestDetail` merged into `aaemu-game/src/systems/quests/model.rs` |
| `QuestItemGroups.cs` | QuestItemGroups | dropped | `QuestItemGroups` dropped: zero references |
| `QuestMarkState.cs` | QuestMarkState | dropped | `QuestMarkState` dropped: zero references |
| `QuestNameKind.cs` | QuestNameKind | dropped | `QuestNameKind` dropped: zero references |
| `QuestNpcAiName.cs` | QuestNpcAiName | merged | `QuestNpcAiName` merged into `aaemu-game/src/systems/quests/hooks.rs` |
| `QuestObjectiveStatus.cs` | QuestObjectiveStatus | merged | `QuestObjectiveStatus` merged into `aaemu-game/src/systems/quests/model.rs` |
| `QuestPattern.cs` | QuestPattern | dropped | `QuestPattern` dropped: zero references |
| `QuestStatus.cs` | QuestStatus | merged | `QuestStatus` merged into `aaemu-game/src/systems/quests/model.rs` |
| `QuestStatusFailed.cs` | QuestStatusFailed | dropped | `QuestStatusFailed` dropped: zero references |
| `QuestTrigger.cs` | QuestTrigger | merged | `QuestTrigger` merged into `aaemu-game/src/systems/npcs/sphere_quest.rs` |

### `AAEmu.Game/Models/Game/Quests/Templates`

| File | Types | Status | Rust |
|---|---|---|---|
| `IQuestActGenericItem.cs` | IQuestActGenericItem | merged | `IQuestActGenericItem` merged into `aaemu-game/src/systems/quests/templates.rs` |
| `IQuestTemplate.cs` | IQuestTemplate | ported | `IQuestTemplate` → `aaemu-game/src/systems/quests/templates.rs` |
| `QuestActTemplate.cs` | QuestActTemplate | ported | `QuestActTemplate` → `aaemu-game/src/systems/quests/templates.rs` |
| `QuestComponentTemplate.cs` | QuestComponentTemplate | ported | `QuestComponentTemplate` → `aaemu-game/src/systems/quests/templates.rs` |
| `QuestTemplate.cs` | QuestTemplate | ported | `QuestTemplate` → `aaemu-game/src/systems/quests/templates.rs` |

### `AAEmu.Game/Models/Game/Quests/UnusedActs`

| File | Types | Status | Rust |
|---|---|---|---|
| `QuestActCheckDistance.cs` | QuestActCheckDistance | merged | `QuestActCheckDistance` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConAcceptBuff.cs` | QuestActConAcceptBuff | merged | `QuestActConAcceptBuff` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConAcceptItemEquip.cs` | QuestActConAcceptItemEquip | merged | `QuestActConAcceptItemEquip` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActConFail.cs` | QuestActConFail | merged | `QuestActConFail` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjCondition.cs` | QuestActObjCondition | merged | `QuestActObjCondition` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjDoodadPhaseCheck.cs` | QuestActObjDoodadPhaseCheck | merged | `QuestActObjDoodadPhaseCheck` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjEffectFire.cs` | QuestActObjEffectFire | merged | `QuestActObjEffectFire` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjItemGroupGather.cs` | QuestActObjItemGroupGather | merged | `QuestActObjItemGroupGather` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjItemGroupUse.cs` | QuestActObjItemGroupUse | merged | `QuestActObjItemGroupUse` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjSendMail.cs` | QuestActObjSendMail | merged | `QuestActObjSendMail` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjZoneNpcTalk.cs` | QuestActObjZoneNpcTalk | merged | `QuestActObjZoneNpcTalk` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActObjZoneQuestComplete.cs` | QuestActObjZoneQuestComplete | merged | `QuestActObjZoneQuestComplete` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyAaPoint.cs` | QuestActSupplyAaPoint | merged | `QuestActSupplyAaPoint` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplyInteraction.cs` | QuestActSupplyInteraction | merged | `QuestActSupplyInteraction` merged into `aaemu-game/src/systems/quests/data.rs` |
| `QuestActSupplySkill.cs` | QuestActSupplySkill | merged | `QuestActSupplySkill` merged into `aaemu-game/src/systems/quests/data.rs` |

### `AAEmu.Game/Models/Game/Schedules`

| File | Types | Status | Rust |
|---|---|---|---|
| `DayOfWeek.cs` | DayOfWeek | ported | `DayOfWeek` → `aaemu-game/src/systems/npcs/schedule.rs` |
| `GameScheduleDoodads.cs` | GameScheduleDoodads | merged | `GameScheduleDoodads` merged into `aaemu-game/src/systems/npcs/schedule.rs` |
| `GameScheduleQuests.cs` | GameScheduleQuests | merged | `GameScheduleQuests` merged into `aaemu-game/src/systems/npcs/schedule.rs` |
| `GameScheduleSpawners.cs` | GameScheduleSpawners | merged | `GameScheduleSpawners` merged into `aaemu-game/src/systems/npcs/schedule.rs` |
| `GameSchedules.cs` | GameSchedules | merged | `GameSchedules` merged into `aaemu-game/src/systems/npcs/schedule.rs` |

### `AAEmu.Game/Models/Game/Shipyard`

| File | Types | Status | Rust |
|---|---|---|---|
| `Shipyard.cs` | Shipyard | ported | `Shipyard` → `aaemu-game/src/models/shipyard.rs` |
| `ShipyardData.cs` | ShipyardData | ported | `ShipyardData` → `aaemu-game/src/models/shipyard.rs` |
| `ShipyardSteps.cs` | ShipyardSteps | merged | `ShipyardSteps` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `ShipyardsTemplate.cs` | ShipyardsTemplate | merged | `ShipyardsTemplate` merged into `aaemu-game/src/systems/vehicles/data.rs` |

### `AAEmu.Game/Models/Game/Skills`

| File | Types | Status | Rust |
|---|---|---|---|
| `Ability.cs` | AbilityType, Ability | merged | `AbilityType` merged into `aaemu-game/src/data/skills.rs`; `Ability` → `aaemu-game/src/models/char_data.rs` |
| `Bonus.cs` | Bonus | ported | `Bonus` → `aaemu-game/src/models/unit.rs` |
| `Buff.cs` | EffectState, Buff | ported | `EffectState` → `aaemu-game/src/models/buff.rs`; `Buff` → `aaemu-game/src/models/buff.rs` |
| `BuffConstants.cs` | BuffConstants | ported | `BuffConstants` → `aaemu-game/src/systems/economy/bridges.rs` |
| `BuffKind.cs` | BuffKind | ported | `BuffKind` → `aaemu-game/src/data/skills.rs` |
| `BuffModifier.cs` | BuffModifier | ported | `BuffModifier` → `aaemu-game/src/models/unit.rs` |
| `BuffModifiers.cs` | BuffModifiers | ported | `BuffModifiers` → `aaemu-game/src/models/unit.rs` |
| `CastAction.cs` | CastType, CastAction, CastSkill, CastPlot … | merged | `CastType` merged: systems/skills/types.rs CastAction discriminants; `CastAction` → `aaemu-game/src/systems/skills/types.rs`; `CastSkill` merged into `aaemu-game/src/systems/housing/ops.rs`; `CastPlot` merged into `aaemu-game/src/systems/skills/types.rs`; `CastBuff` merged into `aaemu-game/src/systems/npcs/hooks.rs`; `CastUnk2` merged into `aaemu-game/src/systems/skills/types.rs`; `CastUnk3` merged into `aaemu-game/src/systems/skills/types.rs` |
| `DamageType.cs` | DamageType | merged | `DamageType` merged into `aaemu-game/src/systems/skills/skill.rs` |
| `DefaultSkill.cs` | DefaultSkill | ported | `DefaultSkill` → `aaemu-game/src/data/skills.rs` |
| `DynamicBonus.cs` | DynamicBonus | ported | `DynamicBonus` → `aaemu-game/src/models/unit.rs` |
| `EffectType.cs` | EffectType | ported | `EffectType` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `PassiveBuff.cs` | PassiveBuff | ported | `PassiveBuff` → `aaemu-game/src/data/skills.rs` |
| `Skill.cs` | Skill | ported | `Skill` → `aaemu-game/src/models/char_data.rs` |
| `SkillCastTarget.cs` | SkillCastTargetType, SkillCastTarget, SkillCastUnitTarget, SkillCastPositionTarget … | merged | `SkillCastTargetType` merged into `aaemu-game/src/systems/skills/types.rs`; `SkillCastTarget` → `aaemu-game/src/systems/skills/types.rs`; `SkillCastUnitTarget` merged into `aaemu-game/src/systems/npcs/hooks.rs`; `SkillCastPositionTarget` merged into `aaemu-game/src/systems/npcs/hooks.rs`; `SkillCastPosition2Target` merged into `aaemu-game/src/systems/skills/types.rs`; `SkillCastItemTarget` merged into `aaemu-game/src/systems/economy/item_effects.rs`; `SkillCastDoodadTarget` merged into `aaemu-game/src/systems/npcs/hooks.rs`; `SkillCastPosition3Target` merged into `aaemu-game/src/systems/skills/types.rs` |
| `SkillCaster.cs` | SkillCasterType, SkillCaster, SkillCasterUnit, SkillCasterUnk1 … | merged | `SkillCasterType` merged into `aaemu-game/src/systems/skills/types.rs`; `SkillCaster` → `aaemu-game/src/systems/skills/types.rs`; `SkillCasterUnit` merged into `aaemu-game/src/systems/npcs/hooks.rs`; `SkillCasterUnk1` merged into `aaemu-game/src/systems/skills/types.rs`; `SkillItem` → `aaemu-game/src/systems/economy/item_effects.rs`; `SkillCasterMount` merged into `aaemu-game/src/systems/skills/types.rs`; `SkillDoodad` merged into `aaemu-game/src/systems/npcs/hooks.rs` |
| `SkillCategory.cs` | SkillCategory | dropped | `SkillCategory` dropped: written to SkillTemplate.Category at load, never read |
| `SkillConstants.cs` | SkillConstants | ported | `SkillConstants` → `aaemu-game/src/systems/skills/controllers.rs` |
| `SkillEffect.cs` | SkillEffect | ported | `SkillEffect` → `aaemu-game/src/systems/skills/data/templates.rs` |
| `SkillHitType.cs` | SkillHitType | merged | `SkillHitType` merged into `aaemu-game/src/systems/skills/skill.rs` |
| `SkillModifier.cs` | SkillModifier | ported | `SkillModifier` → `aaemu-game/src/models/unit.rs` |
| `SkillModifiers.cs` | SkillModifiers | ported | `SkillModifiers` → `aaemu-game/src/models/unit.rs` |
| `SkillObject.cs` | SkillObjectType, SkillObject, SkillObjectPortalInfo, SkillObjectSavePortalInfo … | ported | `SkillObjectType` → `aaemu-game/src/systems/skills/types.rs`; `SkillObject` → `aaemu-game/src/systems/skills/types.rs`; `SkillObjectPortalInfo` ported: aaemu-game/src/systems/skills/types.rs SkillObject::PortalInfo / SavePortalInfo; `SkillObjectSavePortalInfo` ported: aaemu-game/src/systems/skills/types.rs SkillObject::PortalInfo / SavePortalInfo; `SkillObjectText` ported: aaemu-game/src/systems/skills/types.rs SkillObject variants; `SkillObjectPosition` ported: aaemu-game/src/systems/skills/types.rs SkillObject variants; `SkillObjectUnk5` ported: aaemu-game/src/systems/skills/types.rs SkillObject variants; `SkillObjectUnk6` ported: aaemu-game/src/systems/skills/types.rs SkillObject variants; `SkillObjectItemGradeEnchantingSupport` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `SkillProduct.cs` | SkillProduct | merged | `SkillProduct` merged into `aaemu-game/src/systems/skills/data/mod.rs` |
| `SkillReagent.cs` | SkillReagent | merged | `SkillReagent` merged into `aaemu-game/src/systems/skills/data/mod.rs` |
| `SkillTargetRelation.cs` | SkillTargetRelation | ported | `SkillTargetRelation` → `aaemu-game/src/systems/npcs/area_trigger.rs` |
| `SkillTargetSelection.cs` | SkillTargetSelection | merged | `SkillTargetSelection` merged into `aaemu-game/src/systems/skills/skill.rs` |
| `SkillTargetType.cs` | SkillTargetType | merged | `SkillTargetType` merged into `aaemu-game/src/systems/skills/skill.rs` |
| `TickEffect.cs` | TickEffect | ported | `TickEffect` → `aaemu-game/src/systems/skills/data/templates.rs` |

### `AAEmu.Game/Models/Game/Skills/Buffs`

| File | Types | Status | Rust |
|---|---|---|---|
| `BuffEventTriggerKind.cs` | BuffEventTriggerKind | merged | `BuffEventTriggerKind` merged into `aaemu-game/src/systems/skills/buffs.rs` |
| `BuffEvents.cs` | BuffEvents, OnBuffStartedArgs, OnDispelledArgs, OnTimeoutArgs | merged | `BuffEvents` merged: systems/skills/buffs.rs fire_buff_trigger (empty arg classes); `OnBuffStartedArgs` merged: systems/skills/buffs.rs fire_buff_trigger (empty arg classes); `OnDispelledArgs` merged: systems/skills/buffs.rs fire_buff_trigger (empty arg classes); `OnTimeoutArgs` merged: systems/skills/buffs.rs fire_buff_trigger (empty arg classes) |
| `BuffRemoveOn.cs` | BuffRemoveOn | merged | `BuffRemoveOn` merged into `aaemu-game/src/systems/skills/effects/combat.rs` |
| `BuffStackRule.cs` | BuffStackRule | merged | `BuffStackRule` merged into `aaemu-game/src/data/skills.rs` |
| `BuffTolerance.cs` | BuffTolerance | ported | `BuffTolerance` → `aaemu-game/src/systems/skills/data/misc.rs` |
| `BuffToleranceCounter.cs` | BuffToleranceCounter | merged | `BuffToleranceCounter` merged into `aaemu-game/src/systems/skills/state.rs` |
| `BuffToleranceStep.cs` | BuffToleranceStep | merged | `BuffToleranceStep` merged: systems/skills/data/misc.rs BuffTolerance.steps |
| `BuffTriggerTemplate.cs` | BuffTriggerTemplate | ported | `BuffTriggerTemplate` → `aaemu-game/src/systems/skills/data/mod.rs` |
| `BuffTriggersHandler.cs` | BuffTriggersHandler | merged | `BuffTriggersHandler` merged into `aaemu-game/src/systems/skills/buffs.rs` |
| `CombatBuffTemplate.cs` | CombatBuffTemplate | ported | `CombatBuffTemplate` → `aaemu-game/src/models/unit.rs` |
| `ManaRegenTemplate.cs` | ManaRegenTemplate | merged | `ManaRegenTemplate` merged into `aaemu-game/src/systems/skills/regen.rs` |

### `AAEmu.Game/Models/Game/Skills/Buffs/Triggers`

| File | Types | Status | Rust |
|---|---|---|---|
| `AttackBuffTrigger.cs` | AttackBuffTrigger | merged | `AttackBuffTrigger` merged into `aaemu-game/src/systems/skills/buffs.rs` |
| `BuffTrigger.cs` | BuffTrigger | ported | `BuffTrigger` ported: systems/skills/buffs.rs execute_trigger/TriggerSub |
| `DamageBuffTrigger.cs` | DamageBuffTrigger | merged | `DamageBuffTrigger` merged into `aaemu-game/src/systems/skills/buffs.rs` |
| `DamagedBuffTrigger.cs` | DamagedBuffTrigger | merged | `DamagedBuffTrigger` merged into `aaemu-game/src/systems/skills/buffs.rs` |
| `DeathBuffTrigger.cs` | DeathBuffTrigger | dropped | `DeathBuffTrigger` dropped: never instantiated (BuffTriggersHandler uses base BuffTrigger) |
| `DispelledBuffTrigger.cs` | DispelledBuffTrigger | dropped | `DispelledBuffTrigger` dropped: never instantiated (BuffTriggersHandler uses base BuffTrigger) |
| `StartedBuffTrigger.cs` | StartedBuffTrigger | dropped | `StartedBuffTrigger` dropped: never instantiated (BuffTriggersHandler uses base BuffTrigger) |
| `TimeoutBuffTrigger.cs` | TimeoutBuffTrigger | dropped | `TimeoutBuffTrigger` dropped: never instantiated (BuffTriggersHandler uses base BuffTrigger) |

### `AAEmu.Game/Models/Game/Skills/Effects`

| File | Types | Status | Rust |
|---|---|---|---|
| `AcceptQuestEffect.cs` | AcceptQuestEffect | ported | `AcceptQuestEffect` → `aaemu-game/src/systems/quests/events.rs` |
| `AccountAttributeEffect.cs` | AccountAttributeEffect | ported | `AccountAttributeEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `AggroEffect.cs` | AggroEffect | ported | `AggroEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `BubbleEffect.cs` | BubbleEffect | merged | `BubbleEffect` merged into `aaemu-game/src/systems/skills/data/effects.rs` |
| `BuffEffect.cs` | BuffEffect | ported | `BuffEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `CinemalEffect.cs` | CinemalEffect | merged | `CinemalEffect` merged into `aaemu-game/src/systems/quests/bridges.rs` |
| `CleanupUccEffect.cs` | CleanupUccEffect | merged | `CleanupUccEffect` merged into `aaemu-game/src/systems/skills/data/effects.rs` |
| `ConversionEffect.cs` | ConversionEffect | ported | `ConversionEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `CraftEffect.cs` | CraftEffect | ported | `CraftEffect` → `aaemu-game/src/systems/economy/bridges.rs` |
| `DamageEffect.cs` | DamageEffect | ported | `DamageEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `DispelEffect.cs` | DispelEffect | ported | `DispelEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `EffectSource.cs` | EffectSource | ported | `EffectSource` → `aaemu-game/src/systems/skills/effects/mod.rs` |
| `FlyingStateChangeEffect.cs` | FlyingStateChangeEffect | merged | `FlyingStateChangeEffect` merged into `aaemu-game/src/systems/skills/data/effects.rs` |
| `GainLootPackItemEffect.cs` | GainLootPackItemEffect | ported | `GainLootPackItemEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `HealEffect.cs` | HealEffect | ported | `HealEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `ImprintUccEffect.cs` | ImprintUccEffect | merged | `ImprintUccEffect` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `ImpulseEffect.cs` | ImpulseEffect | ported | `ImpulseEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `InteractionEffect.cs` | InteractionEffect | ported | `InteractionEffect` → `aaemu-game/src/systems/npcs/bridges.rs` |
| `KillNpcWithoutCorpseEffect.cs` | KillNpcWithoutCorpseEffect | ported | `KillNpcWithoutCorpseEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `ManaBurnEffect.cs` | ManaBurnEffect | ported | `ManaBurnEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `MoveToRezPointEffect.cs` | MoveToRezPointEffect | merged | `MoveToRezPointEffect` merged into `aaemu-game/src/systems/skills/data/effects.rs` |
| `NpcControlEffect.cs` | NpcControlEffect | ported | `NpcControlEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `NpcSpawnerDespawnEffect.cs` | NpcSpawnerDespawnEffect | merged | `NpcSpawnerDespawnEffect` merged into `aaemu-game/src/systems/skills/data/effects.rs` |
| `NpcSpawnerSpawnEffect.cs` | NpcSpawnerSpawnEffect | ported | `NpcSpawnerSpawnEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `OpenPortalEffect.cs` | OpenPortalEffect | ported | `OpenPortalEffect` → `aaemu-game/src/systems/npcs/bridges.rs` |
| `PhysicalExplosionEffect.cs` | PhysicalExplosionEffect | ported | `PhysicalExplosionEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `PutDownBackpackEffect.cs` | PutDownBackpackEffect | merged | `PutDownBackpackEffect` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `RecoverExpEffect.cs` | RecoverExpEffect | ported | `RecoverExpEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `RepairSlaveEffect.cs` | RepairSlaveEffect | merged | `RepairSlaveEffect` merged into `aaemu-game/src/systems/vehicles/effects.rs` |
| `ReportCrimeEffect.cs` | ReportCrimeEffect | merged | `ReportCrimeEffect` merged into `aaemu-game/src/systems/skills/data/effects.rs` |
| `ResetAoeDiminishingEffect.cs` | ResetAoeDiminishingEffect | merged | `ResetAoeDiminishingEffect` merged into `aaemu-game/src/systems/skills/data/effects.rs` |
| `RestoreManaEffect.cs` | RestoreManaEffect | ported | `RestoreManaEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `ScopedFEffect.cs` | ScopedFEffect | ported | `ScopedFEffect` → `aaemu-game/src/systems/npcs/bridges.rs` |
| `SpawnEffect.cs` | SpawnEffect | ported | `SpawnEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `SpawnFishEffect.cs` | SpawnFishEffect | ported | `SpawnFishEffect` → `aaemu-game/src/systems/npcs/bridges.rs` |
| `SpawnGimmickEffect.cs` | SpawnGimmickEffect | ported | `SpawnGimmickEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `SpecialEffect.cs` | SpecialEffect | ported | `SpecialEffect` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `SpecialEffectAction.cs` | SpecialEffectAction | merged | `SpecialEffectAction` merged into `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `SpecialEffectType.cs` | SpecialType | merged | `SpecialType` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `TrainCraftEffect.cs` | TrainCraftEffect | merged | `TrainCraftEffect` merged into `aaemu-game/src/systems/skills/data/effects.rs` |
| `TrainCraftRankEffect.cs` | TrainCraftRankEffect | merged | `TrainCraftRankEffect` merged into `aaemu-game/src/systems/skills/data/effects.rs` |

### `AAEmu.Game/Models/Game/Skills/Effects/Enums`

| File | Types | Status | Rust |
|---|---|---|---|
| `MateState.cs` | MateState | merged | `MateState` merged: systems/skills/data/effects.rs mate_state_id (C# only stores it) |
| `SkillEffectApplicationMethod.cs` | SkillEffectApplicationMethod | merged | `SkillEffectApplicationMethod` merged into `aaemu-game/src/systems/skills/types.rs` |

### `AAEmu.Game/Models/Game/Skills/Effects/SpecialEffects`

| File | Types | Status | Rust |
|---|---|---|---|
| `ActivateSavedAbilitySet.cs` | ActivateSavedAbilitySet | merged | `ActivateSavedAbilitySet` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `AddBreath.cs` | AddBreath | ported | `AddBreath` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `AddCharacterSlot.cs` | AddCharacterSlot | merged | `AddCharacterSlot` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `AddExp.cs` | AddExp | ported | `AddExp` → `aaemu-game/src/systems/npcs/npc/death.rs` |
| `AddExpeditionContributionPoint.cs` | AddExpeditionContributionPoint | merged | `AddExpeditionContributionPoint` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `AddExpeditionExp.cs` | AddExpeditionExp | merged | `AddExpeditionExp` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `AddFamilyExp.cs` | AddFamilyExp | merged | `AddFamilyExp` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `AddFxToProjectile.cs` | AddFxToProjectile | merged | `AddFxToProjectile` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `AddLaborPower.cs` | AddLaborPower | ported | `AddLaborPower` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `AddPStat.cs` | AddPStat | merged | `AddPStat` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `AggroCopy.cs` | AggroCopy | merged | `AggroCopy` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `AggroReset.cs` | AggroReset | merged | `AggroReset` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `Anim.cs` | Anim | ported | `Anim` → `aaemu-game/src/systems/skills/data/misc.rs` |
| `ApplyBotTrial.cs` | ApplyBotTrial | merged | `ApplyBotTrial` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ApplyReagents.cs` | ApplyReagents | ported | `ApplyReagents` → `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `ArrestBot.cs` | ArrestBot | merged | `ArrestBot` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `AttachTo.cs` | AttachTo | ported | `AttachTo` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `AuctionPostAuthority.cs` | AuctionPostAuthority | merged | `AuctionPostAuthority` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `AutoAttack.cs` | AutoAttack | ported | `AutoAttack` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `Blink.cs` | Blink | ported | `Blink` → `aaemu-game/src/systems/skills/effects/special/movement.rs` |
| `BuffSteal.cs` | BuffSteal | merged | `BuffSteal` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `CancelOngoingBuff.cs` | CancelOngoingBuff | merged | `CancelOngoingBuff` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `CancelStealth.cs` | CancelStealth | ported | `CancelStealth` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `CapturePet.cs` | CapturePet | ported | `CapturePet` → `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `ChangeSkillActiveType.cs` | ChangeSkillActiveType | merged | `ChangeSkillActiveType` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ChangeTarget.cs` | ChangeTarget | merged | `ChangeTarget` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `Charge.cs` | Charge | ported | `Charge` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `CleanupLookConvert.cs` | CleanupLookConvert | ported | `CleanupLookConvert` → `aaemu-game/src/systems/economy/bridges.rs` |
| `ClearProjectile.cs` | ClearProjectile | merged | `ClearProjectile` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `CombatDice.cs` | CombatDice | merged | `CombatDice` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `CombatText.cs` | CombatText | merged | `CombatText` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `Combo.cs` | Combo | merged | `Combo` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ConsumeLaborPower.cs` | ConsumeLaborPower | ported | `ConsumeLaborPower` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `Cooldown.cs` | Cooldown | ported | `Cooldown` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `DeclareDominion.cs` | DeclareDominion | ported | `DeclareDominion` → `aaemu-game/src/systems/social/bridges.rs` |
| `DeclareIndependence.cs` | DeclareIndependence | merged | `DeclareIndependence` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `DestroyAndSpawnSlave.cs` | DestroyAndSpawnSlave | merged | `DestroyAndSpawnSlave` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `Detach.cs` | Detach, DetachUnitReason | ported | `Detach` → `aaemu-game/src/systems/instances/worlds.rs`; `DetachUnitReason` dropped: used only in commented-out code |
| `DisturbCasting.cs` | DisturbCasting | ported | `DisturbCasting` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `DominionTaxInKind.cs` | DominionTaxInKind | merged | `DominionTaxInKind` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `Dyeing.cs` | Dyeing | ported | `Dyeing` → `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `EngraveOnGuardTower.cs` | EngraveOnGuardTower | merged | `EngraveOnGuardTower` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `EnterBeautyshop.cs` | EnterBeautyshop | ported | `EnterBeautyshop` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `Escape.cs` | Escape | ported | `Escape` → `aaemu-game/src/systems/skills/effects/special/movement.rs` |
| `EscapeMySlave.cs` | EscapeMySlave | ported | `EscapeMySlave` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `ExitArchemall.cs` | ExitArchemall | ported | `ExitArchemall` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `ExpToItem.cs` | ExpToItem | merged | `ExpToItem` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `ExpandDecoLimit.cs` | ExpandDecoLimit | merged | `ExpandDecoLimit` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ExpeditionLevelChange.cs` | ExpeditionLevelChange | merged | `ExpeditionLevelChange` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ExpeditionSummon.cs` | ExpeditionSummon | merged | `ExpeditionSummon` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ExplodeBuff.cs` | ExplodeBuff | merged | `ExplodeBuff` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `FakeDeath.cs` | FakeDeath | merged | `FakeDeath` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `FinishChanneling.cs` | FinishChanneling | ported | `FinishChanneling` → `aaemu-game/src/systems/skills/plots.rs` |
| `FishingLoot.cs` | FishingLoot | ported | `FishingLoot` → `aaemu-game/src/systems/economy/item_effects.rs` |
| `FxGroup.cs` | FxGroup | merged | `FxGroup` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `FxGroupAnim.cs` | FxGroupAnim | merged | `FxGroupAnim` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `GainGachaLootPackItem.cs` | GainGachaLootPackItem | merged | `GainGachaLootPackItem` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `GainItem.cs` | GainItem | ported | `GainItem` → `aaemu-game/src/systems/economy/item_effects.rs` |
| `GainItemWithEmblemImprint.cs` | GainItemWithEmblemImprint | ported | `GainItemWithEmblemImprint` → `aaemu-game/src/systems/economy/item_effects.rs` |
| `GainItemWithPosImprint.cs` | GainItemWithPosImprint | merged | `GainItemWithPosImprint` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `GenderTransfer.cs` | GenderTransfer | merged | `GenderTransfer` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `GetSiegeTicket.cs` | GetSiegeTicket | merged | `GetSiegeTicket` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `GiveAppellation.cs` | GiveAppellation | ported | `GiveAppellation` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `GiveBmMileage.cs` | GiveBmMileage | ported | `GiveBmMileage` → `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `GiveCashPoint.cs` | GiveCashPoint | ported | `GiveCashPoint` → `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `GiveCrimePoint.cs` | GiveCrimePoint | ported | `GiveCrimePoint` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `GiveHonorPoint.cs` | GiveHonorPoint | ported | `GiveHonorPoint` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `GiveLivingPoint.cs` | GiveLivingPoint | ported | `GiveLivingPoint` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `GlobalCooldown.cs` | GlobalCooldown | ported | `GlobalCooldown` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `GradeEnchant.cs` | GradeEnchant, GradeEnchantResult | ported | `GradeEnchant` → `aaemu-game/src/systems/economy/bridges.rs`; `GradeEnchantResult` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `HealPet.cs` | HealPet | merged | `HealPet` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `HealSlave.cs` | HealSlave | merged | `HealSlave` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `HudAuctionAuthority.cs` | HudAuctionAuthority | merged | `HudAuctionAuthority` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `HudBattlefieldAuthority.cs` | HudBattlefieldAuthority | merged | `HudBattlefieldAuthority` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `IncreaseFavoritePortalLimit.cs` | IncreaseFavoritePortalLimit | merged | `IncreaseFavoritePortalLimit` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `Interaction.cs` | Interaction | merged | `Interaction` merged into `aaemu-game/src/systems/quests/events.rs` |
| `ItemCapScale.cs` | ItemCapScale | ported | `ItemCapScale` → `aaemu-game/src/data/items.rs` |
| `ItemCapScaleReset.cs` | ItemCapScaleReset | ported | `ItemCapScaleReset` → `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `ItemConversion.cs` | ItemConversion | ported | `ItemConversion` → `aaemu-game/src/systems/economy/bridges.rs` |
| `ItemEvolving.cs` | ItemEvolving | merged | `ItemEvolving` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `ItemEvolvingReRoll.cs` | ItemEvolvingReRoll | merged | `ItemEvolvingReRoll` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `ItemGradeEnchanting.cs` | ItemGradeEnchanting, GradeEnchantResult | merged | `ItemGradeEnchanting` dropped: special effect not in SpecialType enum, logs only; `GradeEnchantResult` → `aaemu-game/src/systems/economy/item_effect_packets.rs` |
| `ItemRefurbishment.cs` | ItemRefurbishment | merged | `ItemRefurbishment` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `ItemSmelting.cs` | ItemSmelting | merged | `ItemSmelting` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ItemSocketing.cs` | ItemSocketing | ported | `ItemSocketing` → `aaemu-game/src/systems/economy/bridges.rs` |
| `KnockBack.cs` | KnockBack | ported | `KnockBack` → `aaemu-game/src/systems/skills/effects/special/movement.rs` |
| `LearnSpecialAbility.cs` | LearnSpecialAbility | merged | `LearnSpecialAbility` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `LoseTarget.cs` | LoseTarget | merged | `LoseTarget` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `LoseTargetingTheTarget.cs` | LoseTargetingTheTarget | merged | `LoseTargetingTheTarget` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ManaCost.cs` | ManaCost | ported | `ManaCost` → `aaemu-game/src/systems/skills/skill.rs` |
| `MateMakeGetUp.cs` | MateMakeGetUp | merged | `MateMakeGetUp` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `MoveToGround.cs` | MoveToGround | merged | `MoveToGround` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `NotifyQuest.cs` | NotifyQuest | merged | `NotifyQuest` merged into `aaemu-game/src/systems/skills/ext.rs` |
| `NpcDespawn.cs` | NpcDespawn | merged | `NpcDespawn` merged into `aaemu-game/src/systems/skills/ext.rs` |
| `OpacityControl.cs` | OpacityControl | merged | `OpacityControl` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `OpenPortal.cs` | OpenPortal | ported | `OpenPortal` → `aaemu-game/src/systems/npcs/portal/mod.rs` |
| `PauseUserMusic.cs` | PauseUserMusic | ported | `PauseUserMusic` → `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `PhysicalEnchantArmor.cs` | PhysicalEnchantArmor | merged | `PhysicalEnchantArmor` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `PhysicalEnchantWeapon.cs` | PhysicalEnchantWeapon | merged | `PhysicalEnchantWeapon` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `PlayAttachmentAnim.cs` | PlayAttachmentAnim | merged | `PlayAttachmentAnim` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `PlaySkillControllerAttachmentAnim.cs` | PlaySkillControllerAttachmentAnim | merged | `PlaySkillControllerAttachmentAnim` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `PlayUserMusic.cs` | PlayUserMusic | ported | `PlayUserMusic` → `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `Projectile.cs` | Projectile | merged | `Projectile` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ProjectileAnim.cs` | ProjectileAnim | merged | `ProjectileAnim` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ProtectionForExpedition.cs` | ProtectionForExpedition | merged | `ProtectionForExpedition` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `RebuildHousing.cs` | RebuildHousing | merged | `RebuildHousing` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ReceiveLuluLeaflet.cs` | ReceiveLuluLeaflet | dropped | `ReceiveLuluLeaflet` dropped: not in SpecialType enum, unreachable, logs only |
| `RechargeItemBuff.cs` | RechargeItemBuff | merged | `RechargeItemBuff` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `RechargeItemRndAttrUnitModifier.cs` | RechargeItemRndAttrUnitModifier | merged | `RechargeItemRndAttrUnitModifier` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `RechargeItemSkill.cs` | RechargeItemSkill | merged | `RechargeItemSkill` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `RedeemBuff.cs` | RedeemBuff | merged | `RedeemBuff` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `RemoveAllDoodad.cs` | RemoveAllDoodad | merged | `RemoveAllDoodad` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `RemoveDoodad.cs` | RemoveDoodad | ported | `RemoveDoodad` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `RemoveDoodadGroup.cs` | RemoveDoodadGroup | merged | `RemoveDoodadGroup` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `RenewEquipment.cs` | RenewEquipment | merged | `RenewEquipment` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `RepairAuthorityInBag.cs` | RepairAuthorityInBag | merged | `RepairAuthorityInBag` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `ReportBot.cs` | ReportBot | ported | `ReportBot` → `aaemu-game/src/systems/social/bridges.rs` |
| `ReportBotArrested.cs` | ReportBotArrested | merged | `ReportBotArrested` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `ReportBotExpired.cs` | ReportBotExpired | ported | `ReportBotExpired` → `aaemu-game/src/systems/social/bridges.rs` |
| `ResetCooldown.cs` | ResetCooldown | ported | `ResetCooldown` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `ResidentServicePoint.cs` | ResidentServicePoint | merged | `ResidentServicePoint` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `Resurrection.cs` | Resurrection | ported | `Resurrection` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `RetrieveProjectile.cs` | RetrieveProjectile | merged | `RetrieveProjectile` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `Return.cs` | Return | merged | `Return` merged into `aaemu-game/src/systems/skills/effects/special/movement.rs` |
| `RevertItemLook.cs` | RevertItemLook | merged | `RevertItemLook` merged into `aaemu-game/src/systems/economy/item_effects.rs` |
| `SavePortal.cs` | SavePortal | ported | `SavePortal` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `SetVariable.cs` | SetVariable | ported | `SetVariable` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `SextantPos.cs` | SextantPos | merged | `SextantPos` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `SkillUse.cs` | SkillUse | ported | `SkillUse` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `Skinize.cs` | Skinize | ported | `Skinize` → `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `SpawnBomb.cs` | SpawnBomb | merged | `SpawnBomb` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `SpawnDoodad.cs` | SpawnDoodad | ported | `SpawnDoodad` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `SpawnPet.cs` | SpawnPet | ported | `SpawnPet` → `aaemu-game/src/systems/skills/effects/special/misc.rs` |
| `SpawnSlave.cs` | SpawnSlave | ported | `SpawnSlave` → `aaemu-game/src/systems/ai/physics/tests.rs` |
| `StartDominionNonPvpDuration.cs` | StartDominionNonPvpDuration | merged | `StartDominionNonPvpDuration` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `StopChanneling.cs` | StopChanneling | merged | `StopChanneling` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `StopManaRegen.cs` | StopManaRegen | ported | `StopManaRegen` → `aaemu-game/src/systems/skills/effects/special/combat.rs` |
| `TeleportToSiegeHq.cs` | TeleportToSiegeHq | merged | `TeleportToSiegeHq` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |
| `TeleportToUnit.cs` | TeleportToUnit | ported | `TeleportToUnit` → `aaemu-game/src/systems/skills/effects/special/movement.rs` |
| `Track.cs` | Track | dropped | `Track` dropped: never instantiated outside Patrol itself (systems/ai/simulation.rs note) |
| `UserMusicSaveNotes.cs` | UserMusicSaveNotes | ported | `UserMusicSaveNotes` → `aaemu-game/src/systems/skills/effects/special/items.rs` |
| `WeaponDisplay.cs` | WeaponDisplay | merged | `WeaponDisplay` merged into `aaemu-game/src/systems/skills/effects/special/mod.rs` |

### `AAEmu.Game/Models/Game/Skills/Plots`

| File | Types | Status | Rust |
|---|---|---|---|
| `Plot.cs` | Plot | ported | `Plot` → `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotAoeCondition.cs` | PlotAoeCondition | ported | `PlotAoeCondition` → `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotCondition.cs` | PlotCondition | ported | `PlotCondition` → `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotConditionType.cs` | PlotConditionType, PlotVariableType | merged | `PlotConditionType` merged: systems/skills/plots.rs condition_check (numeric kinds); `PlotVariableType` dropped: no usages |
| `PlotEventCondition.cs` | PlotEventCondition | ported | `PlotEventCondition` → `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotEventEffect.cs` | PlotEventEffect | ported | `PlotEventEffect` → `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotEventTemplate.cs` | PlotEventTemplate | ported | `PlotEventTemplate` → `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotNextEvent.cs` | PlotNextEvent | ported | `PlotNextEvent` → `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotObject.cs` | PlotObjectType, PlotObject | merged | `PlotObjectType` merged: systems/skills/types.rs PlotObject enum; `PlotObject` → `aaemu-game/src/systems/skills/types.rs` |
| `PlotStep.cs` | PlotStep | dropped | `PlotStep` dropped: no references |

### `AAEmu.Game/Models/Game/Skills/Plots/Tree`

| File | Types | Status | Rust |
|---|---|---|---|
| `PlotBuilder.cs` | PlotBuilder | merged | `PlotBuilder` merged into `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotNode.cs` | PlotNode | ported | `PlotNode` → `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotState.cs` | PlotState | ported | `PlotState` → `aaemu-game/src/systems/skills/plots.rs` |
| `PlotTargetInfo.cs` | PlotTargetInfo | ported | `PlotTargetInfo` → `aaemu-game/src/systems/skills/plots.rs` |
| `PlotTree.cs` | PlotTree | ported | `PlotTree` → `aaemu-game/src/systems/skills/data/plots.rs` |

### `AAEmu.Game/Models/Game/Skills/Plots/Type`

| File | Types | Status | Rust |
|---|---|---|---|
| `PlotEffectSource.cs` | PlotEffectSource | merged | `PlotEffectSource` merged into `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotEffectTarget.cs` | PlotEffectTarget | merged | `PlotEffectTarget` merged into `aaemu-game/src/systems/skills/data/plots.rs` |
| `PlotSourceUpdateMethodType.cs` | PlotSourceUpdateMethodType | merged | `PlotSourceUpdateMethodType` merged: systems/skills/plots.rs update_target_info (numeric ids) |
| `PlotTargetUpdateMethodType.cs` | PlotTargetUpdateMethodType | merged | `PlotTargetUpdateMethodType` merged: systems/skills/plots.rs update_target_info (numeric ids) |

### `AAEmu.Game/Models/Game/Skills/Plots/UpdateTargetMethods`

| File | Types | Status | Rust |
|---|---|---|---|
| `IPlotTargetParams.cs` | IPlotTargetParams | merged | `IPlotTargetParams` merged: systems/skills/plots.rs AreaArgs |
| `PlotTargetAreaParams.cs` | PlotTargetAreaParams | merged | `PlotTargetAreaParams` merged into `aaemu-game/src/systems/skills/plots.rs` |
| `PlotTargetRandomAreaParams.cs` | PlotTargetRandomAreaParams | merged | `PlotTargetRandomAreaParams` merged into `aaemu-game/src/systems/skills/plots.rs` |
| `PlotTargetRandomUnitParams.cs` | PlotTargetRandomUnitParams | merged | `PlotTargetRandomUnitParams` merged into `aaemu-game/src/systems/skills/plots.rs` |

### `AAEmu.Game/Models/Game/Skills/SkillControllers`

| File | Types | Status | Rust |
|---|---|---|---|
| `DashSkillController.cs` | DashSkillController, DashDirection | merged | `DashSkillController` merged into `aaemu-game/src/systems/skills/controllers.rs`; `DashDirection` merged into `aaemu-game/src/systems/skills/controllers.rs` |
| `FloatingSkillController.cs` | FloatingSkillController | merged | `FloatingSkillController` merged into `aaemu-game/src/systems/skills/controllers.rs` |
| `LeapSkillController.cs` | LeapSkillController, LeapDirection | merged | `LeapSkillController` merged into `aaemu-game/src/systems/skills/controllers.rs`; `LeapDirection` merged into `aaemu-game/src/systems/skills/controllers.rs` |
| `ShipHarpoonRopeController.cs` | ShipHarpoonRopeController, RopeTensionHistory | merged | `ShipHarpoonRopeController` merged into `aaemu-game/src/systems/ai/physics/harpoon.rs`; `RopeTensionHistory` merged: systems/vehicles/harpoon.rs history map |
| `SkillController.cs` | SkillController, SCState | ported | `SkillController` → `aaemu-game/src/systems/skills/controllers.rs`; `SCState` merged into `aaemu-game/src/systems/skills/controllers.rs` |
| `SkillControllerKind.cs` | SkillControllerKind | ported | `SkillControllerKind` → `aaemu-game/src/systems/skills/controllers.rs` |
| `WanderingSkillController.cs` | WanderingSkillController | merged | `WanderingSkillController` merged into `aaemu-game/src/systems/skills/controllers.rs` |

### `AAEmu.Game/Models/Game/Skills/Static`

| File | Types | Status | Rust |
|---|---|---|---|
| `BuffAttribute.cs` | BuffAttribute | merged | `BuffAttribute` merged into `aaemu-game/src/models/unit.rs` |
| `BuffTriggerEventKind.cs` | BuffTriggerEventKind | dropped | `BuffTriggerEventKind` dropped: no usages; BuffEventTriggerKind ported in systems/skills/types.rs |
| `HealHitType.cs` | HealHitType | merged | `HealHitType` merged into `aaemu-game/src/systems/skills/effects/combat.rs` |
| `HealType.cs` | HealType | merged | `HealType` merged into `aaemu-game/src/systems/skills/packets.rs` |
| `SkillAttribute.cs` | SkillAttribute | merged | `SkillAttribute` merged into `aaemu-game/src/models/unit.rs` |
| `SkillResult.cs` | SkillResult, SkillResultKeys, SkillResultHelper | ported | `SkillResult` → `aaemu-game/src/systems/skills/types.rs`; `SkillResultKeys` merged: systems/skills/unit_reqs.rs string-key table; `SkillResultHelper` merged into `aaemu-game/src/systems/skills/unit_reqs.rs` |
| `SkillUseConditionKind.cs` | SkillUseConditionKind | ported | `SkillUseConditionKind` → `aaemu-game/src/systems/ai/skills.rs` |

### `AAEmu.Game/Models/Game/Skills/Templates`

| File | Types | Status | Rust |
|---|---|---|---|
| `BonusTemplate.cs` | BonusTemplate | ported | `BonusTemplate` → `aaemu-game/src/models/unit.rs` |
| `BuffTemplate.cs` | BuffTemplate | ported | `BuffTemplate` → `aaemu-game/src/systems/skills/data/templates.rs` |
| `DynamicBonusTemplate.cs` | DynamicBonusTemplate | ported | `DynamicBonusTemplate` → `aaemu-game/src/models/unit.rs` |
| `EffectTemplate.cs` | EffectTemplate | ported | `EffectTemplate` → `aaemu-game/src/systems/skills/data/effects.rs` |
| `LinearFuncTemplate.cs` | LinearFuncTemplate | ported | `LinearFuncTemplate` → `aaemu-game/src/models/unit.rs` |
| `PassiveBuffTemplate.cs` | PassiveBuffTemplate | ported | `PassiveBuffTemplate` → `aaemu-game/src/data/skills.rs` |
| `SkillControllerTemplate.cs` | SkillControllerTemplate | ported | `SkillControllerTemplate` → `aaemu-game/src/systems/skills/data/templates.rs` |
| `SkillTemplate.cs` | SkillTemplate | ported | `SkillTemplate` → `aaemu-game/src/data/skills.rs` |

### `AAEmu.Game/Models/Game/Skills/Utils`

| File | Types | Status | Rust |
|---|---|---|---|
| `SkillTargetingUtil.cs` | SkillTargetingUtil | merged | `SkillTargetingUtil` merged into `aaemu-game/src/systems/skills/relation.rs` |

### `AAEmu.Game/Models/Game/Slaves`

| File | Types | Status | Rust |
|---|---|---|---|
| `SlaveBindings.cs` | SlaveBindings | merged | `SlaveBindings` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `SlaveDoodadBindings.cs` | SlaveDoodadBindings | merged | `SlaveDoodadBindings` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `SlaveDropDoodad.cs` | SlaveDropDoodad | ported | `SlaveDropDoodad` → `aaemu-game/src/systems/vehicles/data.rs` |
| `SlaveInitialBuffs.cs` | SlaveInitialBuffs | merged | `SlaveInitialBuffs` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `SlaveInitialItems.cs` | SlaveInitialItems, SlaveInitialItemPacks | merged | `SlaveInitialItems` merged into `aaemu-game/src/systems/vehicles/data.rs`; `SlaveInitialItemPacks` dropped: enum with no references (pack ids are read from the data) |
| `SlaveKind.cs` | SlaveKind | ported | `SlaveKind` → `aaemu-game/src/systems/ai/physics/mod.rs` |
| `SlaveModelAttachPoint.cs` | SlaveModelAttachPoint | merged | `SlaveModelAttachPoint` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `SlaveMountSkills.cs` | SlaveMountSkills | merged | `SlaveMountSkills` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `SlavePassiveBuffs.cs` | SlavePassiveBuffs | merged | `SlavePassiveBuffs` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `SlaveSpawner.cs` | SlaveSpawner | ported | `SlaveSpawner` → `aaemu-game/src/systems/vehicles/slaves.rs` |
| `SlaveTemplate.cs` | SlaveTemplate | ported | `SlaveTemplate` → `aaemu-game/src/systems/vehicles/data.rs` |

### `AAEmu.Game/Models/Game/Static`

| File | Types | Status | Rust |
|---|---|---|---|
| `EnvSource.cs` | EnvSource | ported | `EnvSource` → `aaemu-game/src/systems/skills/packets.rs` |

### `AAEmu.Game/Models/Game/Taxations`

| File | Types | Status | Rust |
|---|---|---|---|
| `Taxation.cs` | Taxation | ported | `Taxation` → `aaemu-game/src/systems/housing/data.rs` |

### `AAEmu.Game/Models/Game/Team`

| File | Types | Status | Rust |
|---|---|---|---|
| `LootingRule.cs` | LootingRule | ported | `LootingRule` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `LootingRuleMethod.cs` | LootingRuleMethod | ported | `LootingRuleMethod` → `aaemu-game/src/systems/economy/loot_packets.rs` |
| `MemberRole.cs` | MemberRole | ported | `MemberRole` → `aaemu-game/src/systems/social/team.rs` |
| `OverHeadMark.cs` | OverHeadMark | merged | `OverHeadMark` merged into `aaemu-game/src/systems/social/team.rs` |
| `RiskyAction.cs` | RiskyAction | ported | `RiskyAction` → `aaemu-game/src/systems/social/team.rs` |
| `Team.cs` | Team | ported | `Team` → `aaemu-game/src/systems/social/team.rs` |
| `TeamMember.cs` | TeamMember | ported | `TeamMember` → `aaemu-game/src/systems/social/team.rs` |
| `TeamRole.cs` | TeamRole | dropped | `TeamRole` dropped: no references |

### `AAEmu.Game/Models/Game/Teleport`

| File | Types | Status | Rust |
|---|---|---|---|
| `TeleportReason.cs` | TeleportReason | ported | `TeleportReason` → `aaemu-game/src/systems/social/packets/trial.rs` |

### `AAEmu.Game/Models/Game/TowerDefs`

| File | Types | Status | Rust |
|---|---|---|---|
| `TowerDef.cs` | TowerDef | ported | `TowerDef` → `aaemu-game/src/systems/instances/data.rs` |
| `TowerDefInfo.cs` | TowerDefInfo | ported | `TowerDefInfo` → `aaemu-game/src/systems/instances/packets.rs` |
| `TowerDefKey.cs` | TowerDefKey | ported | `TowerDefKey` → `aaemu-game/src/systems/instances/packets.rs` |
| `TowerDefProg.cs` | TowerDefProg | ported | `TowerDefProg` → `aaemu-game/src/systems/instances/data.rs` |
| `TowerDefProgKillTarget.cs` | TowerDefProgKillTarget | ported | `TowerDefProgKillTarget` → `aaemu-game/src/systems/instances/data.rs` |
| `TowerDefProgSpawnTarget.cs` | TowerDefProgSpawnTarget | ported | `TowerDefProgSpawnTarget` → `aaemu-game/src/systems/instances/data.rs` |

### `AAEmu.Game/Models/Game/Trading`

| File | Types | Status | Rust |
|---|---|---|---|
| `Specialty.cs` | Specialty | ported | `Specialty` → `aaemu-game/src/systems/npcs/specialty.rs` |
| `SpecialtyBundleItem.cs` | SpecialtyBundleItem | ported | `SpecialtyBundleItem` → `aaemu-game/src/systems/npcs/specialty.rs` |
| `SpecialtyNpc.cs` | SpecialtyNpc | ported | `SpecialtyNpc` → `aaemu-game/src/systems/npcs/specialty.rs` |

### `AAEmu.Game/Models/Game/Transfers`

| File | Types | Status | Rust |
|---|---|---|---|
| `TransferBindingDoodads.cs` | TransferBindingDoodads | merged | `TransferBindingDoodads` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `TransferBindings.cs` | TransferBindings | merged | `TransferBindings` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `TransferPaths.cs` | TransferPaths | merged | `TransferPaths` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `TransferRoads.cs` | TransferRoads | merged | `TransferRoads` merged into `aaemu-game/src/systems/vehicles/data.rs` |
| `TransferSpawner.cs` | TransferSpawner | ported | `TransferSpawner` → `aaemu-game/src/systems/vehicles/transfers.rs` |
| `TransferTemplate.cs` | TransferTemplate | ported | `TransferTemplate` → `aaemu-game/src/systems/vehicles/data.rs` |

### `AAEmu.Game/Models/Game/Units`

| File | Types | Status | Rust |
|---|---|---|---|
| `BaseUnit.cs` | BaseUnit | merged | `BaseUnit` merged into `aaemu-game/src/models/unit.rs` |
| `BaseUnitType.cs` | BaseUnitType | ported | `BaseUnitType` → `aaemu-game/src/models/unit.rs` |
| `Buffs.cs` | Buffs | ported | `Buffs` → `aaemu-game/src/models/buff.rs` |
| `Combat.cs` | Combat | dropped | `Combat` dropped: never instantiated outside Patrol itself (systems/ai/simulation.rs note) |
| `CombatBuffs.cs` | CombatBuffs | ported | `CombatBuffs` → `aaemu-game/src/models/unit.rs` |
| `IBaseUnit.cs` | IBaseUnit | merged | `IBaseUnit` merged into `aaemu-game/src/models/unit.rs` |
| `IBuffs.cs` | IBuffs | ported | `IBuffs` → `aaemu-game/src/models/buff.rs` |
| `IUnit.cs` | IUnit | merged | `IUnit` merged into `aaemu-game/src/models/unit.rs` |
| `Mate.cs` | MatePassengerInfo, Mate | ported | `MatePassengerInfo` → `aaemu-game/src/models/mate.rs`; `Mate` → `aaemu-game/src/models/mate.rs` |
| `ModelPostureType.cs` | ModelPostureType | ported | `ModelPostureType` → `aaemu-game/src/models/unit.rs` |
| `Patrol.cs` | Patrol | dropped | `Patrol` dropped: never instantiated outside Patrol itself (systems/ai/simulation.rs note) |
| `Portal.cs` | Portal | ported | `Portal` → `aaemu-game/src/data/world_data.rs` |
| `ShipHarpoonRopeState.cs` | ShipHarpoonRopeState | ported | `ShipHarpoonRopeState` → `aaemu-game/src/models/slave.rs` |
| `Slave.cs` | Slave | ported | `Slave` → `aaemu-game/src/models/slave.rs` |
| `Transfer.cs` | Transfer | ported | `Transfer` → `aaemu-game/src/models/transfer.rs` |
| `Unit.cs` | Unit | ported | `Unit` → `aaemu-game/src/models/unit.rs` |
| `UnitAttribute.cs` | UnitAttribute | ported | `UnitAttribute` → `aaemu-game/src/systems/skills/data/templates.rs` |
| `UnitAttributeAttribute.cs` | UnitAttributeAttribute | merged | `UnitAttributeAttribute` merged: systems/skills/progress.rs calculate_with_bonuses (reflection) |
| `UnitCooldowns.cs` | UnitCooldowns | ported | `UnitCooldowns` → `aaemu-game/src/models/unit.rs` |
| `UnitCustomModelParams.cs` | UnitCustomModelType, FixedDecalAsset, FaceModel, UnitCustomModelParams | ported | `UnitCustomModelType` → `aaemu-game/src/models/unit_model.rs`; `FixedDecalAsset` → `aaemu-game/src/models/unit_model.rs`; `FaceModel` → `aaemu-game/src/models/unit_model.rs`; `UnitCustomModelParams` → `aaemu-game/src/models/unit_model.rs` |
| `UnitEvents.cs` | UnitEvents, OnMonsterHuntArgs, OnMonsterGroupHuntArgs, OnItemGatherArgs … | merged | `UnitEvents` merged into `aaemu-game/src/systems/instances/events.rs`; `OnMonsterHuntArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnMonsterGroupHuntArgs` merged into `aaemu-game/src/systems/quests/events.rs`; `OnItemGatherArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnItemGroupGatherArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnTalkMadeArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnTalkNpcGroupMadeArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnAggroArgs` dropped: raised in C# but nothing subscribes; `OnItemUseArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnItemGroupUseArgs` dropped: never raised in C#; `OnInteractionArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnCraftArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnExpressFireArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnLevelUpArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnMateLevelUpArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnAbilityLevelUpArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnEnterSphereArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnExitSphereArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnZoneKillArgs` merged into `aaemu-game/src/systems/quests/engine.rs`; `OnCinemaStartedArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnCinemaEndedArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnReportNpcArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnAcceptDoodadArgs` dropped: never raised in C#; `OnReportDoodadArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnReportJournalArgs` dropped: never raised in C#; `OnQuestCompleteArgs` dropped: never raised in C#; `OnAttackArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnAttackedArgs` dropped: raised in C# but nothing subscribes; `OnDamageArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnDamagedArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnDamagedMeleeArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnDamagedRangedArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnDamagedSpellArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnDamagedSiegeArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnLandingArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnMovementArgs` dropped: raised in C# but nothing subscribes; `OnChannelingCancelArgs` dropped: raised in C# but nothing subscribes; `OnRemoveOnDamagedArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnDeathArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnUnmountArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnKillArgs` merged into `aaemu-game/src/systems/quests/events.rs`; `OnDamagedCollisionArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnImmortalityArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnTimeArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnKillAnyArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnHealedArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnCombatStartedArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `InIdleArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `InAlertArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `InDeadArgs` dropped: never raised in C#; `OnSpawnArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnDespawnArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnTimerExpiredArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnQuestStepChangedArgs` merged: event args become hook / BuffEventTriggerKind parameters (systems/quests/events.rs, systems/skills/buffs.rs, systems/ai/npc_events.rs); `OnAlertArgs` dropped: never raised in C# |
| `UnitModifierType.cs` | UnitModifierType | ported | `UnitModifierType` → `aaemu-game/src/models/unit.rs` |
| `UnitProcs.cs` | UnitProcs | dropped | `UnitProcs` dropped: C# RollProcsForKind never fires (LastProc=MinValue => skipped), documented systems/economy/bridges.rs |
| `UnitReqs.cs` | UnitReqs | ported | `UnitReqs` → `aaemu-game/src/systems/quests/mod.rs` |
| `UnitReqsValidationResult.cs` | UnitReqsValidationResult | merged | `UnitReqsValidationResult` merged into `aaemu-game/src/systems/skills/unit_reqs.rs` |
| `UnitTypeFlag.cs` | UnitTypeFlag | ported | `UnitTypeFlag` → `aaemu-game/src/models/unit.rs` |

### `AAEmu.Game/Models/Game/Units/Movements`

| File | Types | Status | Rust |
|---|---|---|---|
| `DefaultMoveType.cs` | DefaultMoveType | merged | `DefaultMoveType` merged into `aaemu-game/src/models/movement.rs` |
| `MoveType.cs` | MoveTypeEnum, MoveType | ported | `MoveTypeEnum` → `aaemu-game/src/models/movement.rs`; `MoveType` → `aaemu-game/src/models/movement.rs` |
| `MoveTypeAlertness.cs` | MoveTypeAlertness | ported | `MoveTypeAlertness` → `aaemu-game/src/models/movement.rs` |
| `ShipMoveType.cs` | ShipMoveType | merged | `ShipMoveType` merged into `aaemu-game/src/models/movement.rs` |
| `ShipRequestMoveType.cs` | ShipRequestMoveType | merged | `ShipRequestMoveType` merged into `aaemu-game/src/models/movement.rs` |
| `TransferData.cs` | TransferData | ported | `TransferData` → `aaemu-game/src/systems/vehicles/flow_tests.rs` |
| `UnitMoveType.cs` | UnitMoveType | merged | `UnitMoveType` merged into `aaemu-game/src/systems/vehicles/movement.rs` |
| `VehicleMoveType.cs` | VehicleMoveType | merged | `VehicleMoveType` merged into `aaemu-game/src/models/movement.rs` |

### `AAEmu.Game/Models/Game/Units/Route`

| File | Types | Status | Rust |
|---|---|---|---|
| `Line.cs` | Line | dropped | `Line` dropped: never instantiated outside Patrol itself (systems/ai/simulation.rs note) |
| `Simulation.cs` | Simulation | ported | `Simulation` → `aaemu-game/src/systems/ai/simulation.rs` |
| `Track.cs` | Track | dropped | `Track` dropped: never instantiated outside Patrol itself (systems/ai/simulation.rs note) |

### `AAEmu.Game/Models/Game/Units/Static`

| File | Types | Status | Rust |
|---|---|---|---|
| `KillReason.cs` | KillReason | ported | `KillReason` → `aaemu-game/src/systems/economy/repair.rs` |
| `UnitReqsKindType.cs` | UnitReqsKindType | merged | `UnitReqsKindType` merged into `aaemu-game/src/systems/quests/unit_reqs.rs` |

### `AAEmu.Game/Models/Game/World`

| File | Types | Status | Rust |
|---|---|---|---|
| `AreaShape.cs` | AreaShape | ported | `AreaShape` → `aaemu-game/src/systems/npcs/area_trigger.rs` |
| `AreaShapeType.cs` | AreaShapeType | ported | `AreaShapeType` → `aaemu-game/src/systems/skills/data/misc.rs` |
| `AreaTrigger.cs` | AreaTrigger | ported | `AreaTrigger` → `aaemu-game/src/systems/npcs/area_trigger.rs` |
| `GameObject.cs` | GameObject | merged | `GameObject` merged into `aaemu-game/src/world/object.rs` |
| `IGameObject.cs` | IGameObject | merged | `IGameObject` merged into `aaemu-game/src/world/object.rs` |
| `Point.cs` | Point | ported | `Point` → `aaemu-game/src/systems/instances/data.rs` |
| `Region.cs` | Region | ported | `Region` → `aaemu-game/src/world/mod.rs` |
| `ShipStaticBarrierZones.cs` | ShipStaticBarrierZones, ShipStaticBarrier, ShipStaticBarrierEntryDto | merged | `ShipStaticBarrierZones` merged into `aaemu-game/src/systems/ai/physics/barriers.rs`; `ShipStaticBarrier` → `aaemu-game/src/systems/ai/physics/barriers.rs`; `ShipStaticBarrierEntryDto` merged into `aaemu-game/src/systems/ai/physics/barriers.rs` |
| `Spawner.cs` | Spawner | ported | `Spawner` → `aaemu-game/src/systems/npcs/gimmick/movement.rs` |
| `SphereQuest.cs` | AreaSphereTriggerCondition, SphereQuest, SphereQuestTrigger, SphereQuestStarter | merged | `AreaSphereTriggerCondition` merged into `aaemu-game/src/systems/npcs/sphere_quest.rs`; `SphereQuest` → `aaemu-game/src/systems/npcs/sphere_quest.rs`; `SphereQuestTrigger` → `aaemu-game/src/systems/npcs/sphere_quest.rs`; `SphereQuestStarter` → `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `WaterBodies.cs` | WaterBodies | ported | `WaterBodies` → `aaemu-game/src/systems/ai/physics/water.rs` |
| `WaterBodyArea.cs` | WaterBodyArea | ported | `WaterBodyArea` → `aaemu-game/src/systems/ai/physics/water.rs` |
| `WaterBodyAreaType.cs` | WaterBodyAreaType | ported | `WaterBodyAreaType` → `aaemu-game/src/systems/ai/physics/water.rs` |
| `WorldCell.cs` | WorldCell | merged | `WorldCell` merged into `aaemu-game/src/systems/npcs/heightmap.rs` |
| `WorldEvents.cs` | WorldEvents, OnUnitKilledArgs, OnUnitSpawnArgs, OnUnitCombatStartArgs … | merged | `WorldEvents` merged into `aaemu-game/src/systems/instances/data.rs`; `OnUnitKilledArgs` merged: systems/instances/events.rs world events; `OnUnitSpawnArgs` merged: systems/instances/events.rs world events; `OnUnitCombatStartArgs` merged: systems/instances/events.rs world events; `OnUnitCombatEndArgs` merged: systems/instances/events.rs world events; `OnAreaClearArgs` merged: systems/instances/events.rs world events; `OnDoodadSpawnArgs` merged: systems/instances/events.rs world events |
| `WorldInstance.ShipBarrierScriptApi.cs` | WorldInstance | ported | `WorldInstance` → `aaemu-game/src/world/mod.rs` |
| `WorldInstance.cs` | WorldInstance | ported | `WorldInstance` → `aaemu-game/src/world/mod.rs` |
| `WorldInteraction.cs` | IWorldInteraction, WorldInteractionType | merged | `IWorldInteraction` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs`; `WorldInteractionType` → `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `WorldInteractionGroup.cs` | WorldInteractionGroup | ported | `WorldInteractionGroup` → `aaemu-game/src/systems/npcs/area_trigger.rs` |
| `WorldPos.cs` | WorldPos | ported | `WorldPos` → `aaemu-game/src/models/movement.rs` |
| `WorldSpawnLocation.cs` | WorldSpawnLocation | ported | `WorldSpawnLocation` → `aaemu-game/src/data/world_templates.rs` |
| `WorldTemplate.cs` | WorldTemplate | ported | `WorldTemplate` → `aaemu-game/src/data/world_templates.rs` |
| `ZoneConfig.cs` | ZoneConfig, CellConfig, SectorConfig | dropped | `ZoneConfig` dropped: unused; `CellConfig` dropped: unused; `SectorConfig` dropped: unused |
| `ZoneInstanceId.cs` | ZoneInstanceId | ported | `ZoneInstanceId` → `aaemu-game/src/systems/instances/packets.rs` |

### `AAEmu.Game/Models/Game/World/Interactions`

| File | Types | Status | Rust |
|---|---|---|---|
| `Alchemy.cs` | Alchemy | merged | `Alchemy` merged into `aaemu-game/src/managers/unit_character_manager_tests.rs` |
| `Binding.cs` | Binding | ported | `Binding` → `aaemu-game/src/systems/npcs/doodad/funcs/world.rs` |
| `Butcher.cs` | Butcher | merged | `Butcher` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `BuySiegeTicket.cs` | BuySiegeTicket | merged | `BuySiegeTicket` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Cancel.cs` | Cancel | ported | `Cancel` → `aaemu-game/src/systems/skills/state.rs` |
| `Catch.cs` | Catch | merged | `Catch` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `CheckGrowth.cs` | CheckGrowth | ported | `CheckGrowth` → `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `CheckWater.cs` | CheckWater | merged | `CheckWater` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `CompleteQuest.cs` | CompleteQuest | merged | `CompleteQuest` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `CraftAct.cs` | CraftAct | merged | `CraftAct` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `CraftStart.cs` | CraftStart | merged | `CraftStart` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `CropHarvest.cs` | CropHarvest | ported | `CropHarvest` → `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Cutdown.cs` | Cutdown | merged | `Cutdown` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `Demolish.cs` | Demolish | ported | `Demolish` → `aaemu-game/src/systems/housing/ops.rs` |
| `DigMine.cs` | DigMine | merged | `DigMine` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `DirectLoot.cs` | DirectLoot | merged | `DirectLoot` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Dooring.cs` | Dooring | merged | `Dooring` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Error.cs` | Error | ported | `Error` → `aaemu-game/src/data/formulas.rs` |
| `Feeding.cs` | Feeding | merged | `Feeding` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `FiberCollect.cs` | FiberCollect | merged | `FiberCollect` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `FruitPick.cs` | FruitPick | merged | `FruitPick` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `GiveQuest.cs` | GiveQuest | merged | `GiveQuest` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Hang.cs` | Hang | ported | `Hang` → `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Harvest.cs` | Harvest | merged | `Harvest` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `LineSpray.cs` | LineSpray | merged | `LineSpray` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Looting.cs` | Looting | ported | `Looting` → `aaemu-game/src/systems/economy/mod.rs` |
| `MagicalEnchant.cs` | MagicalEnchant | ported | `MagicalEnchant` → `aaemu-game/src/systems/economy/bridges.rs` |
| `OreMine.cs` | OreMine | merged | `OreMine` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `PlantCollect.cs` | PlantCollect | merged | `PlantCollect` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `RecoverItem.cs` | RecoverItem | ported | `RecoverItem` → `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Remove.cs` | Remove | ported | `Remove` → `aaemu-game/src/systems/npcs/spawn_manager.rs` |
| `RepairHouse.cs` | RepairHouse | merged | `RepairHouse` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Seeding.cs` | Seeding | merged | `Seeding` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `SellBackpack.cs` | SellBackpack | merged | `SellBackpack` merged into `aaemu-game/src/systems/npcs/specialty.rs` |
| `SkinOff.cs` | SkinOff | merged | `SkinOff` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `Spray.cs` | Spray | merged | `Spray` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `SummonDoodad.cs` | SummonDoodad | ported | `SummonDoodad` → `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `SummonDoodadWithUcc.cs` | SummonDoodadWithUcc | merged | `SummonDoodadWithUcc` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Use.cs` | Use | merged | `Use` merged into `aaemu-game/src/systems/npcs/doodad/phase.rs` |
| `WaterLevel.cs` | WaterLevel | merged | `WaterLevel` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |
| `Watering.cs` | Watering | merged | `Watering` merged into `aaemu-game/src/systems/npcs/doodad/interactions.rs` |

### `AAEmu.Game/Models/Game/World/Transform`

| File | Types | Status | Rust |
|---|---|---|---|
| `PositionAndRotation.cs` | PositionAndRotation | ported | `PositionAndRotation` → `aaemu-game/src/models/transform.rs` |
| `Transform.cs` | Transform | ported | `Transform` → `aaemu-game/src/models/transform.rs` |
| `WorldSpawnPosition.cs` | WorldSpawnPosition | ported | `WorldSpawnPosition` → `aaemu-game/src/data/world_templates.rs` |

### `AAEmu.Game/Models/Game/World/Xml`

| File | Types | Status | Rust |
|---|---|---|---|
| `XmlWorld.cs` | XmlWorld | ported | `XmlWorld` → `aaemu-game/src/data/world_templates.rs` |
| `XmlWorldCell.cs` | XmlWorldCell | ported | `XmlWorldCell` → `aaemu-game/src/data/world_templates.rs` |
| `XmlWorldSector.cs` | XmlWorldSector | ported | `XmlWorldSector` → `aaemu-game/src/data/world_templates.rs` |
| `XmlWorldZone.cs` | XmlWorldZone | ported | `XmlWorldZone` → `aaemu-game/src/data/world_templates.rs` |

### `AAEmu.Game/Models/Game/World/Zones`

| File | Types | Status | Rust |
|---|---|---|---|
| `Area.cs` | Area | ported | `Area` → `aaemu-game/src/systems/npcs/subzone.rs` |
| `Climate.cs` | Climate | merged | `Climate` merged into `aaemu-game/src/systems/npcs/doodad/phase.rs` |
| `Zone.cs` | Zone | ported | `Zone` → `aaemu-tool-worldconverter/src/world.rs` |
| `ZoneClimateElem.cs` | ZoneClimateElem | merged | `ZoneClimateElem` merged: systems/npcs/doodad/manager.rs climate pairs |
| `ZoneConflict.cs` | ZoneConflict | ported | `ZoneConflict` ported: aaemu-game/src/systems/social/zone_conflict.rs (runtime) + aaemu-game/src/data/world_data.rs (data) |
| `ZoneConflictType.cs` | ZoneConflictType | merged | `ZoneConflictType` merged into `aaemu-game/src/data/world_data.rs` |
| `ZoneGroup.cs` | ZoneGroup | ported | `ZoneGroup` → `aaemu-game/src/data/world_data.rs` |
| `ZoneGroupBannedTag.cs` | ZoneGroupBannedTag | dropped | `ZoneGroupBannedTag` dropped: loaded, never read |

### `AAEmu.Game/Models/Json`

| File | Types | Status | Rust |
|---|---|---|---|
| `JsonDoodadSpawns.cs` | JsonDoodadSpawns | ported | `JsonDoodadSpawns` → `aaemu-game/src/utils/json_models.rs` |
| `JsonNpcPath.cs` | JsonNpcPath | dropped | `JsonNpcPath` dropped: unused |
| `JsonNpcSpawns.cs` | JsonNpcSpawns | ported | `JsonNpcSpawns` → `aaemu-game/src/utils/json_models.rs` |
| `JsonPosition.cs` | JsonPosition | ported | `JsonPosition` → `aaemu-game/src/utils/json_models.rs` |
| `JsonQuestSphere.cs` | JsonQuestSphere | ported | `JsonQuestSphere` → `aaemu-game/src/utils/json_models.rs` |
| `JsonSlaveSpawns.cs` | JsonSlaveSpawns | dropped | `JsonSlaveSpawns` dropped: only used by the GM command SlaveSaveSubCommand (GM commands port) |

### `AAEmu.Game/Models/Observers`

| File | Types | Status | Rust |
|---|---|---|---|
| `TimeOfDayObserver.cs` | TimeOfDayObserver | merged | `TimeOfDayObserver` merged into `aaemu-game/src/managers/time.rs` |

### `AAEmu.Game/Models/Spheres`

| File | Types | Status | Rust |
|---|---|---|---|
| `SphereAcceptQuestQuests.cs` | SphereAcceptQuestQuests | ported | `SphereAcceptQuestQuests` → `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `SphereAcceptQuests.cs` | SphereAcceptQuests | merged | `SphereAcceptQuests` merged into `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `SphereBubbles.cs` | SphereBubbles | merged | `SphereBubbles` merged into `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `SphereBuffs.cs` | SphereBuffs | ported | `SphereBuffs` → `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `SphereChatBubbles.cs` | SphereChatBubbles | ported | `SphereChatBubbles` → `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `SphereDoodadInteracts.cs` | SphereDoodadInteracts | ported | `SphereDoodadInteracts` → `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `SphereQuests.cs` | SphereQuests | ported | `SphereQuests` → `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `SphereSkills.cs` | SphereSkills | ported | `SphereSkills` → `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `SphereSounds.cs` | SphereSounds | ported | `SphereSounds` → `aaemu-game/src/systems/npcs/sphere_quest.rs` |
| `Spheres.cs` | Spheres | ported | `Spheres` → `aaemu-game/src/systems/quests/spheres.rs` |

### `AAEmu.Game/Models/StaticValues`

| File | Types | Status | Rust |
|---|---|---|---|
| `BuffSaveRuleType.cs` | BuffSaveRuleType | merged | `BuffSaveRuleType` merged into `aaemu-game/src/data/skills.rs` |
| `CashShopCmdUiType.cs` | CashShopCmdUiType | merged | `CashShopCmdUiType` merged into `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CashShopCurrencyType.cs` | CashShopCurrencyType | merged | `CashShopCurrencyType` merged into `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CashShopLimitType.cs` | CashShopLimitType | merged | `CashShopLimitType` merged into `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CashShopRestrictSaleType.cs` | CashShopRestrictSaleType | merged | `CashShopRestrictSaleType` merged into `aaemu-game/src/systems/economy/cash_shop.rs` |
| `CharacterCreateError.cs` | CharacterCreateError | ported | `CharacterCreateError` → `aaemu-game/src/managers/name.rs` |
| `DoodadConstants.cs` | DoodadConstants | merged | `DoodadConstants` merged into `aaemu-game/src/systems/npcs/fish_school.rs` |
| `FactionsEnum.cs` | FactionsEnum | merged | `FactionsEnum` merged into `aaemu-game/src/systems/npcs/doodad/templates.rs` |
| `GamePointKind.cs` | GamePointKind | ported | `GamePointKind` → `aaemu-game/src/systems/economy/inventory.rs` |
| `ItemImplEnum.cs` | ItemImplEnum | merged | `ItemImplEnum` merged into `aaemu-game/src/data/items.rs` |
| `LeaveWorldTargetType.cs` | LeaveWorldTargetType | ported | `LeaveWorldTargetType` → `aaemu-game/src/packets/c2g.rs` |
| `MoveTypeFlags.cs` | MoveTypeFlags, MoveTypeActorFlags | ported | `MoveTypeFlags` → `aaemu-game/src/models/movement.rs`; `MoveTypeActorFlags` → `aaemu-game/src/models/movement.rs` |
| `SkillsEnum.cs` | SkillsEnum | ported | `SkillsEnum` → `aaemu-game/src/systems/npcs/npc/packets.rs` |
| `TagsEnum.cs` | TagsEnum | merged | `TagsEnum` merged into `aaemu-game/src/systems/ai/npc.rs` |

### `AAEmu.Game/Models/Stream`

| File | Types | Status | Rust |
|---|---|---|---|
| `CustomUcc.cs` | CustomUcc | ported | `CustomUcc` → `aaemu-game/src/stream/tests.rs` |
| `DefaultUcc.cs` | DefaultUcc | merged | `DefaultUcc` merged into `aaemu-game/src/stream/models.rs` |
| `Ucc.cs` | Ucc, UccType | ported | `Ucc` → `aaemu-game/src/stream/models.rs`; `UccType` → `aaemu-game/src/stream/models.rs` |
| `UccPart.cs` | UccPart | ported | `UccPart` → `aaemu-game/src/stream/models.rs` |
| `UccUploadHandle.cs` | UccUploadHandle | ported | `UccUploadHandle` → `aaemu-game/src/stream/models.rs` |

### `AAEmu.Game/Models/Tasks`

| File | Types | Status | Rust |
|---|---|---|---|
| `ShutdownTask.cs` | ShutdownTask | ported | `ShutdownTask` → `aaemu-game/src/systems/services/shutdown.rs` |
| `Task.cs` | Task | merged | `Task` merged into `aaemu-game/src/systems/services/tasks.rs` |
| `UnitPointsRegenTask.cs` | UnitPointsRegenTask | dropped | `UnitPointsRegenTask` dropped: never scheduled; regen in systems/skills/regen.rs |

### `AAEmu.Game/Models/Tasks/AreaTriggers`

| File | Types | Status | Rust |
|---|---|---|---|
| `AreaTriggerTickTask.cs` | AreaTriggerTickTask | dropped | `AreaTriggerTickTask` dropped: never created; tick in systems/npcs/area_trigger.rs |

### `AAEmu.Game/Models/Tasks/CashShop`

| File | Types | Status | Rust |
|---|---|---|---|
| `CashShopBuyTask.cs` | CashShopBuyTask | merged | `CashShopBuyTask` merged into `aaemu-game/src/systems/economy/cash_shop.rs` |

### `AAEmu.Game/Models/Tasks/Characters`

| File | Types | Status | Rust |
|---|---|---|---|
| `CharacterDeleteTask.cs` | CharacterDeleteTask | ported | `CharacterDeleteTask` → `aaemu-game/src/managers/character.rs` |
| `CharacterOnlineTrackingTask.cs` | CharacterOnlineTrackingTask | ported | `CharacterOnlineTrackingTask` → `aaemu-game/src/systems/services/character.rs` |

### `AAEmu.Game/Models/Tasks/Crime`

| File | Types | Status | Rust |
|---|---|---|---|
| `TrialUpdateTask.cs` | TrialUpdateTask | merged | `TrialUpdateTask` merged into `aaemu-game/src/systems/social/trial.rs` |

### `AAEmu.Game/Models/Tasks/Doodads`

| File | Types | Status | Rust |
|---|---|---|---|
| `DoodadDeleteTask.cs` | DoodadDeleteTask | merged | `DoodadDeleteTask` merged into `aaemu-game/src/systems/npcs/doodad/tasks.rs` |
| `DoodadFuncCloutTask.cs` | DoodadFuncCloutTask | merged | `DoodadFuncCloutTask` merged into `aaemu-game/src/systems/npcs/doodad/tasks.rs` |
| `DoodadFuncFinalTask.cs` | DoodadFuncFinalTask | merged | `DoodadFuncFinalTask` merged into `aaemu-game/src/systems/npcs/doodad/tasks.rs` |
| `DoodadFuncGrowthTask.cs` | DoodadFuncGrowthTask | merged | `DoodadFuncGrowthTask` merged into `aaemu-game/src/systems/npcs/doodad/tasks.rs` |
| `DoodadFuncTask.cs` | DoodadFuncTask | merged | `DoodadFuncTask` merged: systems/npcs/doodad/tasks.rs FuncTaskKind |
| `DoodadFuncTimerTask.cs` | DoodadFuncTimerTask | merged | `DoodadFuncTimerTask` merged into `aaemu-game/src/systems/npcs/doodad/tasks.rs` |
| `DoodadFuncTodTask.cs` | DoodadFuncTodTask | merged | `DoodadFuncTodTask` merged into `aaemu-game/src/systems/npcs/doodad/tasks.rs` |

### `AAEmu.Game/Models/Tasks/Duels`

| File | Types | Status | Rust |
|---|---|---|---|
| `DuelDistanceСheckTask.cs` | DuelDistanceСheckTask | ported | `DuelDistanceСheckTask` → `systems/social/duel.rs` |
| `DuelEndTimerTask.cs` | DuelEndTimerTask | merged | `DuelEndTimerTask` merged into `aaemu-game/src/systems/social/duel.rs` |
| `DuelFuncTask.cs` | DuelFuncTask | dropped | `DuelFuncTask` dropped: abstract, no subclasses |
| `DuelResultСheckTask.cs` | DuelResultСheckTask | ported | `DuelResultСheckTask` → `systems/social/duel.rs` |
| `DuelStartTask.cs` | DuelStartTask | merged | `DuelStartTask` merged into `aaemu-game/src/systems/social/duel.rs` |

### `AAEmu.Game/Models/Tasks/Gimmicks`

| File | Types | Status | Rust |
|---|---|---|---|
| `GimmickTask.cs` | GimmickTask | dropped | `GimmickTask` dropped: abstract base without subclasses |

### `AAEmu.Game/Models/Tasks/Housing`

| File | Types | Status | Rust |
|---|---|---|---|
| `HousingTaxTask.cs` | HousingTaxTask | merged | `HousingTaxTask` merged into `aaemu-game/src/systems/housing/manager.rs` |

### `AAEmu.Game/Models/Tasks/Item`

| File | Types | Status | Rust |
|---|---|---|---|
| `ItemTimerTask.cs` | ItemTimerTask | merged | `ItemTimerTask` merged into `aaemu-game/src/systems/economy/item_timers.rs` |

### `AAEmu.Game/Models/Tasks/Mails`

| File | Types | Status | Rust |
|---|---|---|---|
| `MailDeliveryTask.cs` | MailDeliveryTask | merged | `MailDeliveryTask` merged into `aaemu-game/src/systems/economy/mail.rs` |

### `AAEmu.Game/Models/Tasks/Mate`

| File | Types | Status | Rust |
|---|---|---|---|
| `MateXpUpdateTask.cs` | MateXpUpdateTask | ported | `MateXpUpdateTask` → `aaemu-game/src/systems/vehicles/mates.rs` |

### `AAEmu.Game/Models/Tasks/Npcs`

| File | Types | Status | Rust |
|---|---|---|---|
| `NpcDeleteTask.cs` | NpcDeleteTask | merged | `NpcDeleteTask` merged into `aaemu-game/src/systems/ai/simulation.rs` |

### `AAEmu.Game/Models/Tasks/PublicFarm`

| File | Types | Status | Rust |
|---|---|---|---|
| `PublicFarmTickStartTask.cs` | PublicFarmTickStartTask | merged | `PublicFarmTickStartTask` merged into `aaemu-game/src/systems/npcs/public_farm.rs` |

### `AAEmu.Game/Models/Tasks/Quests`

| File | Types | Status | Rust |
|---|---|---|---|
| `QuestDailyResetTask.cs` | QuestDailyResetTask | merged | `QuestDailyResetTask` merged into `aaemu-game/src/systems/quests/mod.rs` |
| `QuestManagerRunQueueTask.cs` | QuestManagerRunQueueTask | merged | `QuestManagerRunQueueTask` merged into `aaemu-game/src/systems/quests/mod.rs` |
| `QuestTimeoutTask.cs` | QuestTimeoutTask | merged | `QuestTimeoutTask` merged into `aaemu-game/src/systems/quests/mod.rs` |

### `AAEmu.Game/Models/Tasks/SaveTask`

| File | Types | Status | Rust |
|---|---|---|---|
| `SaveTickStartTask.cs` | SaveTickStartTask | merged | `SaveTickStartTask` merged into `managers/save.rs` |

### `AAEmu.Game/Models/Tasks/ServerLoad`

| File | Types | Status | Rust |
|---|---|---|---|
| `LoadTask.cs` | LoadTask | merged | `LoadTask` merged into `aaemu-game/src/login/mod.rs` |

### `AAEmu.Game/Models/Tasks/Shipyard`

| File | Types | Status | Rust |
|---|---|---|---|
| `ShipyardTickTask.cs` | ShipyardTickTask, ShipyardCompleteTask | merged | `ShipyardTickTask` merged into `aaemu-game/src/systems/vehicles/shipyards.rs`; `ShipyardCompleteTask` merged into `aaemu-game/src/systems/vehicles/shipyards.rs` |

### `AAEmu.Game/Models/Tasks/Skills`

| File | Types | Status | Rust |
|---|---|---|---|
| `ApplySkillTask.cs` | ApplySkillTask | merged | `ApplySkillTask` merged into `aaemu-game/src/systems/skills/skill.rs` |
| `CastTask.cs` | CastTask | merged | `CastTask` merged into `aaemu-game/src/systems/skills/skill.rs` |
| `CraftTask.cs` | CraftTask | ported | `CraftTask` → `aaemu-game/src/systems/economy/craft.rs` |
| `DespawnTask.cs` | DespawnTask | dropped | `DespawnTask` dropped: never created |
| `DispelTask.cs` | DispelTask | ported | `DispelTask` → `aaemu-game/src/systems/skills/buffs.rs` |
| `EndChannelingTask.cs` | EndChannelingTask | merged | `EndChannelingTask` merged into `aaemu-game/src/systems/skills/state.rs` |
| `MeleeCastTask.cs` | MeleeCastTask | dropped | `MeleeCastTask` dropped: only used in commented-out code (Skill.cs) |
| `SkillTask.cs` | SkillTask | merged | `SkillTask` merged into `aaemu-game/src/systems/skills/state.rs` |
| `UseAutoAttackSkillTask.cs` | UseAutoAttackSkillTask | merged | `UseAutoAttackSkillTask` merged into `aaemu-game/src/systems/skills/skill.rs` |
| `UseSkillTask.cs` | UseSkillTask | merged | `UseSkillTask` merged into `aaemu-game/src/systems/npcs/hooks.rs` |

### `AAEmu.Game/Models/Tasks/Slave`

| File | Types | Status | Rust |
|---|---|---|---|
| `SendMySlaveTask.cs` | SendMySlaveTask | merged | `SendMySlaveTask` merged into `aaemu-game/src/systems/vehicles/mod.rs` |

### `AAEmu.Game/Models/Tasks/Specialty`

| File | Types | Status | Rust |
|---|---|---|---|
| `SpecialtyRatioConsumeTask.cs` | SpecialtyRatioConsumeTask | merged | `SpecialtyRatioConsumeTask` merged into `aaemu-game/src/systems/npcs/specialty.rs` |
| `SpecialtyRatioRegenTask.cs` | SpecialtyRatioRegenTask | merged | `SpecialtyRatioRegenTask` merged into `aaemu-game/src/systems/npcs/specialty.rs` |

### `AAEmu.Game/Models/Tasks/TimedRewards`

| File | Types | Status | Rust |
|---|---|---|---|
| `TimedRewardsTask.cs` | TimedRewardsTask | ported | `TimedRewardsTask` → `aaemu-game/src/systems/services/timed_rewards.rs` |

### `AAEmu.Game/Models/Tasks/UnitMove`

| File | Types | Status | Rust |
|---|---|---|---|
| `Move.cs` | Move | merged | `Move` merged into `aaemu-game/src/systems/ai/path_handler.rs` |
| `Record.cs` | Record | ported | `Record` → `aaemu-game/src/systems/npcs/portal/mod.rs` |
| `UnitMove.cs` | UnitMove | ported | `UnitMove` → `aaemu-game/src/models/movement.rs` |
| `UnitMovePause.cs` | UnitMovePause | dropped | `UnitMovePause` dropped: never created |

### `AAEmu.Game/Models/Tasks/World`

| File | Types | Status | Rust |
|---|---|---|---|
| `DoodadSpawnerDoDespawnTask.cs` | DoodadSpawnerDoDespawnTask | merged | `DoodadSpawnerDoDespawnTask` merged into `aaemu-game/src/systems/npcs/doodad/spawner.rs` |
| `DoodadSpawnerDoSpawnTask.cs` | DoodadSpawnerDoSpawnTask | merged | `DoodadSpawnerDoSpawnTask` merged into `aaemu-game/src/systems/npcs/doodad/spawner.rs` |
| `KillPortalTask.cs` | KillPortalTask | merged | `KillPortalTask` merged into `aaemu-game/src/systems/npcs/portal/mod.rs` |
| `NpcSpawnerDoDespawnTask.cs` | NpcSpawnerDoDespawnTask | merged | `NpcSpawnerDoDespawnTask` merged into `aaemu-game/src/systems/npcs/npc/spawner.rs` |
| `NpcSpawnerDoSpawnTask.cs` | NpcSpawnerDoSpawnTask | dropped | `NpcSpawnerDoSpawnTask` dropped: never created |

### `AAEmu.Game/Models/Tasks/Zones`

| File | Types | Status | Rust |
|---|---|---|---|
| `ZoneStateChangeTask.cs` | ZoneStateChangeTask | merged | `ZoneStateChangeTask` merged into `aaemu-game/src/systems/social/zone_conflict.rs` |

### `AAEmu.Game/Physics`

| File | Types | Status | Rust |
|---|---|---|---|
| `ReplicationSmoothing.cs` | ReplicationSmoothing | ported | `ReplicationSmoothing` → `aaemu-game/src/systems/ai/physics/ship.rs` |
| `ShipCliffInteraction.cs` | ShipCliffInteraction, CliffObstacleDefaults | merged | `ShipCliffInteraction` merged into `aaemu-game/src/systems/ai/physics/contacts.rs`; `CliffObstacleDefaults` merged into `aaemu-game/src/systems/ai/physics/contacts.rs` |
| `ShipController.cs` | ShipController, ShipMassBoxDefaults, ShipMotionDefaults, ShipWindProfile | merged | `ShipController` merged into `aaemu-game/src/systems/ai/physics/ship.rs`; `ShipMassBoxDefaults` merged into `aaemu-game/src/systems/ai/physics/ship.rs`; `ShipMotionDefaults` merged into `aaemu-game/src/systems/ai/physics/ship.rs`; `ShipWindProfile` → `systems/ai/physics/ship.rs` |
| `ShipDoodadInteraction.cs` | ShipDoodadInteraction, DoodadObstacleDefaults | merged | `ShipDoodadInteraction` merged into `aaemu-game/src/systems/ai/physics/contacts.rs`; `DoodadObstacleDefaults` merged into `aaemu-game/src/systems/ai/physics/contacts.rs` |
| `ShipHarpoonTowPhysics.cs` | ShipHarpoonTowPhysics | merged | `ShipHarpoonTowPhysics` merged into `aaemu-game/src/systems/vehicles/harpoon.rs` |
| `ShipShipInteraction.cs` | ShipShipInteraction, ShipHullPairDefaults | merged | `ShipShipInteraction` merged into `aaemu-game/src/systems/ai/physics/ship.rs`; `ShipHullPairDefaults` merged into `aaemu-game/src/systems/ai/physics/contacts.rs` |
| `ShipShoreInteraction.cs` | ShipShoreInteraction, ShorePhysicsDefaults | merged | `ShipShoreInteraction` merged into `aaemu-game/src/systems/ai/physics/mod.rs`; `ShorePhysicsDefaults` merged into `aaemu-game/src/systems/ai/physics/shore.rs` |
| `ShipSiegeAoEHit.cs` | ShipSiegeAoEHit | merged | `ShipSiegeAoEHit` merged into `aaemu-game/src/systems/ai/physics/mod.rs` |
| `ShipStaticBarrierInteraction.cs` | ShipStaticBarrierInteraction, BarrierDefaults | merged | `ShipStaticBarrierInteraction` merged into `aaemu-game/src/systems/ai/physics/barriers.rs`; `BarrierDefaults` merged into `aaemu-game/src/systems/ai/physics/barriers.rs` |
| `ShipStaticBarrierSpatialGrid.cs` | ShipStaticBarrierSpatialGrid, SegmentRef | merged | `ShipStaticBarrierSpatialGrid` merged into `aaemu-game/src/systems/ai/physics/barriers.rs`; `SegmentRef` merged: systems/ai/physics/barriers.rs tuple |
| `ShipStaticObstacleContact.cs` | ShipStaticObstacleContact | merged | `ShipStaticObstacleContact` merged into `aaemu-game/src/systems/ai/physics/contacts.rs` |

### `AAEmu.Game/Physics/Debug`

| File | Types | Status | Rust |
|---|---|---|---|
| `HarpoonMechanicsDebug.cs` | HarpoonMechanicsDebug | merged | `HarpoonMechanicsDebug` merged into `aaemu-game/src/systems/ai/physics/mod.rs` |
| `ShipTuningDebug.cs` | ShipTuningDebug, ChatTags, MarkerSet, MassBoxGameGeometry | dropped | `ShipTuningDebug` dropped: debug instrumentation, hard-wired off in C# (systems/ai/physics/mod.rs note); `ChatTags` dropped: debug instrumentation, hard-wired off in C# (systems/ai/physics/mod.rs note); `MarkerSet` dropped: debug instrumentation, hard-wired off in C# (systems/ai/physics/mod.rs note); `MassBoxGameGeometry` dropped: debug instrumentation, hard-wired off in C# (systems/ai/physics/mod.rs note) |

### `AAEmu.Game/Physics/Forces`

| File | Types | Status | Rust |
|---|---|---|---|
| `Buoyancy.cs` | Buoyancy | ported | `Buoyancy` → `aaemu-game/src/systems/ai/physics/forces.rs` |
| `ForceGenerator.cs` | ForceGenerator | merged | `ForceGenerator` merged into `systems/ai/physics/forces.rs` |
| `OneShotVelocityKick.cs` | OneShotVelocityKick | merged | `OneShotVelocityKick` merged into `aaemu-game/src/systems/ai/physics/forces.rs` |
| `WhirlpoolShipPull.cs` | WhirlpoolShipPull | merged | `WhirlpoolShipPull` merged into `aaemu-game/src/systems/ai/physics/forces.rs` |

### `AAEmu.Game/Physics/HeightMaps`

| File | Types | Status | Rust |
|---|---|---|---|
| `CollisionTriangle.cs` | CollisionTriangle | merged | `CollisionTriangle` merged: systems/ai/physics/jitter.rs SAT terrain contact |
| `Heightmap.cs` | Heightmap | ported | `Heightmap` → `aaemu-tool-worldconverter/src/tests.rs` |
| `HeightmapDetection.cs` | HeightmapDetection | merged | `HeightmapDetection` merged into `aaemu-game/src/systems/ai/physics/jitter.rs` |
| `HeightmapTester.cs` | HeightmapTester | merged | `HeightmapTester` merged: systems/ai/physics/jitter.rs SAT terrain contact |

### `AAEmu.Game/Physics/Util`

| File | Types | Status | Rust |
|---|---|---|---|
| `PhysicsUtil.cs` | PhysicsUtil | merged | `PhysicsUtil` merged into `aaemu-game/src/systems/ai/physics/jitter.rs` |

### `AAEmu.Game/Services`

| File | Types | Status | Rust |
|---|---|---|---|
| `DiscordBotService.cs` | DiscordBotService | ported | `DiscordBotService` → `aaemu-game/src/systems/services/discord.rs` |

### `AAEmu.Game/Services/WebApi`

| File | Types | Status | Rust |
|---|---|---|---|
| `IController.cs` | IController | ported | `IController` → `aaemu-game/src/systems/services/webapi/route_mapper.rs` |
| `RouteDefinition.cs` | RouteDefinition | ported | `RouteDefinition` → `aaemu-game/src/systems/services/webapi/route_mapper.rs` |
| `RouteMapper.cs` | RouteMapper | ported | `RouteMapper` → `aaemu-game/src/systems/services/webapi/route_mapper.rs` |
| `WebApiGetAttribute.cs` | WebApiGetAttribute | merged | `WebApiGetAttribute` merged into `aaemu-game/src/systems/services/webapi/route_mapper.rs` |
| `WebApiPostAttribute.cs` | WebApiPostAttribute | merged | `WebApiPostAttribute` merged into `aaemu-game/src/systems/services/webapi/route_mapper.rs` |
| `WebApiServer.cs` | WebApiServer | ported | `WebApiServer` → `aaemu-game/src/systems/services/webapi/mod.rs` |
| `WebApiService.cs` | WebApiService | ported | `WebApiService` → `aaemu-game/src/systems/services/webapi/mod.rs` |
| `WebApiSession.cs` | WebApiSession | merged | `WebApiSession` merged into `aaemu-game/src/systems/services/webapi/mod.rs` |

### `AAEmu.Game/Services/WebApi/Controllers`

| File | Types | Status | Rust |
|---|---|---|---|
| `AuctionController.cs` | AuctionController | ported | `AuctionController` → `aaemu-game/src/systems/services/webapi/controllers/auction.rs` |
| `BaseController.cs` | BaseController | merged | `BaseController` merged into `aaemu-game/src/systems/services/webapi/http.rs` |
| `CharacterController.cs` | CharacterController | ported | `CharacterController` → `aaemu-game/src/systems/services/webapi/controllers/mod.rs` |
| `CommandController.cs` | CommandController | ported | `CommandController` → `aaemu-game/src/systems/services/webapi/controllers/mod.rs` |
| `ExpeditionController.cs` | ExpeditionController | ported | `ExpeditionController` → `aaemu-game/src/systems/services/webapi/controllers/mod.rs` |
| `MailController.cs` | MailController | ported | `MailController` → `aaemu-game/src/systems/services/webapi/controllers/mail.rs` |
| `StatusController.cs` | StatusController | ported | `StatusController` → `aaemu-game/src/systems/services/webapi/controllers/mod.rs` |
| `WorldController.cs` | WorldController | ported | `WorldController` → `aaemu-game/src/systems/services/webapi/controllers/mod.rs` |

### `AAEmu.Game/Services/WebApi/Models`

| File | Types | Status | Rust |
|---|---|---|---|
| `CharacterModel.cs` | CharacterModel | ported | `CharacterModel` → `aaemu-game/src/systems/services/webapi/controllers/mod.rs` |
| `DeleteMailRequest.cs` | DeleteMailRequest | merged | `DeleteMailRequest` merged into `aaemu-game/src/systems/services/webapi/controllers/mail.rs` |
| `ErrorModel.cs` | ErrorModel | ported | `ErrorModel` → `aaemu-game/src/systems/services/webapi/http.rs` |
| `ExpeditionModel.cs` | ExpeditionModel | merged | `ExpeditionModel` merged into `aaemu-game/src/systems/services/webapi/controllers/mod.rs` |
| `ListMailRequest.cs` | ListMailRequest | merged | `ListMailRequest` merged into `aaemu-game/src/systems/services/webapi/controllers/mail.rs` |
| `ListMailResponseItem.cs` | ListMailResponseItem, ListMailResponseItems | merged | `ListMailResponseItem` merged into `aaemu-game/src/systems/services/webapi/controllers/mail.rs`; `ListMailResponseItems` dropped: unused wrapper |
| `SendMailRequest.cs` | SendMailRequest, RecipientType, AttachmentItem | ported | `SendMailRequest` → `aaemu-game/src/systems/services/webapi/controllers/mail.rs`; `RecipientType` → `aaemu-game/src/systems/services/webapi/controllers/mail.rs`; `AttachmentItem` → `aaemu-game/src/systems/services/webapi/controllers/mail.rs` |

### `AAEmu.Game/Utils`

| File | Types | Status | Rust |
|---|---|---|---|
| `IdManager.cs` | IdManager | ported | `IdManager` → `aaemu-game/src/managers/id.rs` |
| `MathUtil.cs` | MathUtil | merged | `MathUtil` merged into `aaemu-game/src/models/movement.rs` |
| `NumericExtensions.cs` | NumericExtensions | merged | `NumericExtensions` merged into `aaemu-game/src/systems/skills/effects/special/movement.rs` |
| `QuestCommandUtil.cs` | QuestCommandUtil | merged | `QuestCommandUtil` merged into `aaemu-game/src/utils/mod.rs` |
| `SphereCommandUtil.cs` | SphereCommandUtil | merged | `SphereCommandUtil` merged into `aaemu-game/src/utils/mod.rs` |

### `AAEmu.Game/Utils/Converters`

| File | Types | Status | Rust |
|---|---|---|---|
| `BaseJsonConverter{T}.cs` | BaseJsonConverter | merged | `BaseJsonConverter` merged into `aaemu-game/src/utils/json_models.rs` |
| `ChatConverter.cs` | ChatConverter | merged | `ChatConverter` merged into `aaemu-game/src/utils/chat.rs` |
| `JsonDoodadSpawnsConverter.cs` | JsonDoodadSpawnsConverter | merged | `JsonDoodadSpawnsConverter` merged into `aaemu-game/src/utils/json_models.rs` |
| `JsonModelsConverter.cs` | JsonModelsConverter | merged | `JsonModelsConverter` merged into `aaemu-game/src/utils/json_models.rs` |
| `JsonNpcSpawnsConverter.cs` | JsonNpcSpawnsConverter | merged | `JsonNpcSpawnsConverter` merged into `aaemu-game/src/utils/json_models.rs` |
| `JsonPositionConverter.cs` | JsonPositionConverter | merged | `JsonPositionConverter` merged into `aaemu-game/src/utils/json_models.rs` |
| `JsonQuestSphereConverter.cs` | JsonQuestSphereConverter | merged | `JsonQuestSphereConverter` merged into `aaemu-game/src/utils/json_models.rs` |

### `AAEmu.Game/Utils/DB`

| File | Types | Status | Rust |
|---|---|---|---|
| `HoldablesSchemaCheck.cs` | HoldablesSchemaCheck | merged | `HoldablesSchemaCheck` merged into `aaemu-game/src/utils/holdables_schema.rs` |
| `SQLite.cs` | SQLite | merged | `SQLite` merged into `aaemu-game/src/main.rs` |
| `SQLiteWrapperReader.cs` | SQLiteWrapperReader | merged | `SQLiteWrapperReader` merged into `aaemu-game/src/data/formulas.rs` |

### `AAEmu.Game/Utils/Scripts`

| File | Types | Status | Rust |
|---|---|---|---|
| `CharacterMessageOutput.cs` | CharacterMessageOutput | ported | `CharacterMessageOutput` → `aaemu-game/src/systems/services/commands/output.rs` |
| `IMessageOutput.cs` | IMessageOutput | ported | `IMessageOutput` → `aaemu-game/src/systems/services/commands/output.rs` |
| `ScriptCompiler.cs` | ScriptCompiler | merged | `ScriptCompiler` merged into `aaemu-game/src/systems/services/commands/mod.rs` |
| `ScriptObject.cs` | ScriptObject | merged | `ScriptObject` merged into `aaemu-game/src/systems/services/commands/mod.rs` |
| `ScriptReflector.cs` | ScriptReflector | merged | `ScriptReflector` merged into `aaemu-game/src/systems/services/commands/mod.rs` |

### `AAEmu.Game/Utils/Scripts/SubCommands`

| File | Types | Status | Rust |
|---|---|---|---|
| `ISubCommand.cs` | ICommandV2 | merged | `ICommandV2` merged into `aaemu-game/src/systems/services/commands/subcommand.rs` |
| `NumericSubCommandParameter.cs` | NumericSubCommandParameter | ported | `NumericSubCommandParameter` → `aaemu-game/src/systems/services/commands/params.rs` |
| `ParameterResult.cs` | ParameterResult | ported | `ParameterResult` → `aaemu-game/src/systems/services/commands/params.rs` |
| `ParameterResult{T}.cs` | ParameterResult | ported | `ParameterResult` → `aaemu-game/src/systems/services/commands/params.rs` |
| `ParameterValue.cs` | ParameterValue | ported | `ParameterValue` → `aaemu-game/src/systems/services/commands/params.rs` |
| `StringSubCommandParameter.cs` | StringSubCommandParameter | ported | `StringSubCommandParameter` → `aaemu-game/src/systems/services/commands/params.rs` |
| `SubCommandBase.cs` | SubCommandBase | ported | `SubCommandBase` → `aaemu-game/src/systems/services/commands/subcommand.rs` |
| `SubCommandParameterBase.cs` | SubCommandParameterBase | merged | `SubCommandParameterBase` merged into `aaemu-game/src/systems/services/commands/params.rs` |

### `AAEmu.Game/Utils/Scripts/SubCommands/AStar`

| File | Types | Status | Rust |
|---|---|---|---|
| `AStarEndPositionSubCommand.cs` | AStarEndPositionSubCommand | merged | `AStarEndPositionSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |
| `AStarPathFindingSubCommand.cs` | AStarPathFindingSubCommand | ported | `AStarPathFindingSubCommand` → `aaemu-game/src/systems/services/commands/sub.rs` |
| `AStarStartPositionSubCommand.cs` | AStarStartPositionSubCommand | merged | `AStarStartPositionSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |
| `AStarViewSubCommand.cs` | AStarViewSubCommand | ported | `AStarViewSubCommand` → `aaemu-game/src/systems/services/commands/sub.rs` |

### `AAEmu.Game/Utils/Scripts/SubCommands/Feature`

| File | Types | Status | Rust |
|---|---|---|---|
| `FeatureCheckSubCommand.cs` | FeatureCheckSubCommand | ported | `FeatureCheckSubCommand` → `aaemu-game/src/systems/services/commands/sub.rs` |
| `FeatureSetSubCommand.cs` | FeatureSetSubCommand | ported | `FeatureSetSubCommand` → `aaemu-game/src/systems/services/commands/sub.rs` |

### `AAEmu.Game/Utils/Scripts/SubCommands/FishFinder`

| File | Types | Status | Rust |
|---|---|---|---|
| `FishFinderSetSubCommand.cs` | FishFinderSetSubCommand | ported | `FishFinderSetSubCommand` → `aaemu-game/src/systems/services/commands/sub.rs` |

### `AAEmu.Game/Utils/Scripts/SubCommands/Time`

| File | Types | Status | Rust |
|---|---|---|---|
| `TimeSetSubCommand.cs` | TimeSetSubCommand | ported | `TimeSetSubCommand` → `aaemu-game/src/systems/services/commands/sub.rs` |

### `AAEmu.Game/Utils/Scripts/SubCommands/World`

| File | Types | Status | Rust |
|---|---|---|---|
| `WorldSetAutosaveintervalSubCommand.cs` | WorldSetAutosaveintervalSubCommand | merged | `WorldSetAutosaveintervalSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |
| `WorldSetExprateSubCommand.cs` | WorldSetExprateSubCommand | merged | `WorldSetExprateSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |
| `WorldSetGeodatamodeSubCommand.cs` | WorldSetGeodatamodeSubCommand | merged | `WorldSetGeodatamodeSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |
| `WorldSetGrowthrateSubCommand.cs` | WorldSetGrowthrateSubCommand | merged | `WorldSetGrowthrateSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |
| `WorldSetHonorrateSubCommand.cs` | WorldSetHonorrateSubCommand | merged | `WorldSetHonorrateSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |
| `WorldSetLogoutmessageSubCommand.cs` | WorldSetLogoutmessageSubCommand | merged | `WorldSetLogoutmessageSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |
| `WorldSetLootrateSubCommand.cs` | WorldSetLootrateSubCommand | merged | `WorldSetLootrateSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |
| `WorldSetMotdmessageSubCommand.cs` | WorldSetMotdmessageSubCommand | merged | `WorldSetMotdmessageSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |
| `WorldSetSubCommand.cs` | WorldSetSubCommand | ported | `WorldSetSubCommand` → `aaemu-game/src/systems/services/commands/sub.rs` |
| `WorldSetVocationrateSubCommand.cs` | WorldSetVocationrateSubCommand | merged | `WorldSetVocationrateSubCommand` merged into `aaemu-game/src/systems/services/commands/sub.rs` |

### `AAEmu.Game/Weather`

| File | Types | Status | Rust |
|---|---|---|---|
| `StormShipLogic.cs` | StormShipLogic, StormState | merged | `StormShipLogic` merged into `aaemu-game/src/systems/ai/physics/forces.rs`; `StormState` merged into `systems/ai/physics/forces.rs` |

### `AAEmu.Login`

| File | Types | Status | Rust |
|---|---|---|---|
| `LoginService.cs` | LoginService | merged | `LoginService` merged into `aaemu-login/src/server.rs` |
| `Program.cs` | Program | merged | `Program` merged into `aaemu-game/src/systems/services/game_service.rs` |

### `AAEmu.Login/Core/Authentication`

| File | Types | Status | Rust |
|---|---|---|---|
| `AuthFlowResult.cs` | AuthFlowResult, Success, Denied, Pending | ported | `AuthFlowResult` → `aaemu-login/src/auth.rs`; `Success` merged into `aaemu-login/src/services/password.rs`; `Denied` → `aaemu-login/src/models.rs`; `Pending` → `aaemu-game/src/systems/ai/physics/manager.rs` |
| `IArsAuthFlow.cs` | IArsAuthFlow | merged | `IArsAuthFlow` merged into `aaemu-login/src/auth.rs` |
| `IAuthenticationFlow.cs` | IAuthenticationFlow | merged | `IAuthenticationFlow` merged into `aaemu-login/src/network/login/tests.rs` |
| `ICertAuthFlow.cs` | ICertAuthFlow | merged | `ICertAuthFlow` merged into `aaemu-login/src/auth.rs` |
| `IChallenge2AuthFlow.cs` | IChallenge2AuthFlow | merged | `IChallenge2AuthFlow` merged into `aaemu-login/src/auth.rs` |
| `IChallengeAuthFlow.cs` | IChallengeAuthFlow | merged | `IChallengeAuthFlow` merged into `aaemu-login/src/auth.rs` |
| `IKoreaAuthFlowFactory.cs` | IKoreaAuthFlowFactory | dropped | `IKoreaAuthFlowFactory` dropped: interface only (DI / abstraction); implementations audited separately |
| `IOtpAuthFlow.cs` | IOtpAuthFlow | merged | `IOtpAuthFlow` merged into `aaemu-login/src/auth.rs` |
| `KoreaAuthFlow.cs` | KoreaAuthFlow, State | ported | `KoreaAuthFlow` → `aaemu-login/src/auth.rs`; `State` → `aaemu-game/src/systems/social/trial.rs` |
| `KoreaAuthFlowFactory.cs` | KoreaAuthFlowFactory | dropped | `KoreaAuthFlowFactory` dropped: DI factory / descriptor scaffolding; the Rust login builds flows and sessions directly (aaemu-login/src/controllers, network) |
| `KoreaAuthOptions.cs` | KoreaAuthOptions | ported | `KoreaAuthOptions` → `aaemu-login/src/config.rs` |
| `PasswordAuthFlow.cs` | PasswordAuthFlow | ported | `PasswordAuthFlow` → `aaemu-login/src/auth.rs` |
| `ReconnectAuthFlow.cs` | ReconnectAuthFlow | ported | `ReconnectAuthFlow` → `aaemu-login/src/auth.rs` |
| `ServiceCollectionExtensions.cs` | ServiceCollectionExtensions | merged | `ServiceCollectionExtensions` merged into `aaemu-login/src/packets/c2l.rs` |

### `AAEmu.Login/Core/Controllers`

| File | Types | Status | Rust |
|---|---|---|---|
| `GameController.cs` | GameController | ported | `GameController` → `aaemu-login/src/controllers/game.rs` |
| `IGameController.cs` | IGameController | ported | `IGameController` → `aaemu-login/src/controllers/game.rs` |
| `ILoginController.cs` | ILoginController | ported | `ILoginController` → `aaemu-login/src/controllers/login.rs` |
| `IRequestController.cs` | IRequestController | ported | `IRequestController` → `aaemu-login/src/controllers/request.rs` |
| `LoginController.cs` | LoginController | ported | `LoginController` → `aaemu-login/src/controllers/login.rs` |
| `RequestController.cs` | RequestController | ported | `RequestController` → `aaemu-login/src/controllers/request.rs` |

### `AAEmu.Login/Core/Network/Connections`

| File | Types | Status | Rust |
|---|---|---|---|
| `ConnectionIdLease.cs` | ConnectionIdLease | ported | `ConnectionIdLease` → `aaemu-login/src/id_manager.rs` |
| `ConnectionIdLeaseFactory.cs` | ConnectionIdLeaseFactory | dropped | `ConnectionIdLeaseFactory` dropped: DI factory / descriptor scaffolding; the Rust login builds flows and sessions directly (aaemu-login/src/controllers, network) |
| `IConnectionIdLeaseFactory.cs` | IConnectionIdLeaseFactory | dropped | `IConnectionIdLeaseFactory` dropped: interface only (DI / abstraction); implementations audited separately |
| `IInternalConnectionTable.cs` | IInternalConnectionTable | ported | `IInternalConnectionTable` → `aaemu-login/src/network/internal.rs` |
| `ILoginClient.cs` | ILoginClient | ported | `ILoginClient` → `aaemu-login/src/auth.rs` |
| `ILoginConnection.cs` | ILoginConnectionOwner, ILoginConnection | merged | `ILoginConnectionOwner` dropped: interface only (DI / abstraction); implementations audited separately; `ILoginConnection` → `aaemu-login/src/network/login.rs` |
| `ILoginConnectionTable.cs` | ILoginConnectionTable | ported | `ILoginConnectionTable` → `aaemu-login/src/network/login.rs` |
| `ILoginSession.cs` | ILoginSession | merged | `ILoginSession` merged into `aaemu-login/src/network/login.rs` |
| `ILoginSessionFactory.cs` | ILoginSessionFactory | dropped | `ILoginSessionFactory` dropped: interface only (DI / abstraction); implementations audited separately |
| `InternalConnection.cs` | InternalConnection | ported | `InternalConnection` → `aaemu-login/src/network/internal.rs` |
| `InternalConnectionTable.cs` | InternalConnectionTable | ported | `InternalConnectionTable` → `aaemu-login/src/network/internal.rs` |
| `LoginClient.cs` | LoginClient | ported | `LoginClient` → `aaemu-login/src/auth.rs` |
| `LoginConnection.cs` | LoginConnection | ported | `LoginConnection` → `aaemu-login/src/network/login.rs` |
| `LoginConnectionTable.cs` | LoginConnectionTable | ported | `LoginConnectionTable` → `aaemu-login/src/network/login.rs` |
| `LoginSession.cs` | LoginSession | merged | `LoginSession` merged into `aaemu-login/src/network/login.rs` |
| `LoginSessionFactory.cs` | LoginSessionFactory | dropped | `LoginSessionFactory` dropped: DI factory / descriptor scaffolding; the Rust login builds flows and sessions directly (aaemu-login/src/controllers, network) |
| `LoginState.cs` | LoginState | ported | `LoginState` → `aaemu-login/src/network/login.rs` |

### `AAEmu.Login/Core/Network/Internal`

| File | Types | Status | Rust |
|---|---|---|---|
| `IInternalNetwork.cs` | IInternalNetwork | merged | `IInternalNetwork` merged into `aaemu-login/src/network/internal.rs` |
| `IInternalPacket.cs` | IInternalPacket | merged | `IInternalPacket` merged into `aaemu-login/src/packets/l2g.rs` |
| `IInternalPacketDescriptor.cs` | IInternalPacketDescriptor | dropped | `IInternalPacketDescriptor` dropped: interface only (DI / abstraction); implementations audited separately |
| `IInternalProtocolHandler.cs` | IInternalProtocolHandler | merged | `IInternalProtocolHandler` merged into `aaemu-login/src/network/internal.rs` |
| `InternalNetwork.cs` | InternalNetwork | merged | `InternalNetwork` merged into `aaemu-login/src/network/internal.rs` |
| `InternalPacket.cs` | InternalPacket | merged | `InternalPacket` merged into `aaemu-login/src/packets/l2g.rs` |
| `InternalPacketDescriptor.cs` | InternalPacketDescriptor | dropped | `InternalPacketDescriptor` dropped: DI factory / descriptor scaffolding; the Rust login builds flows and sessions directly (aaemu-login/src/controllers, network) |
| `InternalProtocolHandler.cs` | InternalProtocolHandler | merged | `InternalProtocolHandler` merged into `aaemu-login/src/network/internal.rs` |
| `ServiceCollectionExtensions.cs` | ServiceCollectionExtensions | merged | `ServiceCollectionExtensions` merged into `aaemu-login/src/packets/c2l.rs` |

### `AAEmu.Login/Core/Network/Login`

| File | Types | Status | Rust |
|---|---|---|---|
| `ILoginConnectionFactory.cs` | ILoginConnectionFactory | merged | `ILoginConnectionFactory` merged into `aaemu-login/src/network/login.rs` |
| `ILoginPacket.cs` | ILoginPacket | merged | `ILoginPacket` merged into `aaemu-game/src/login/mod.rs` |
| `ILoginPacketDescriptor.cs` | ILoginPacketDescriptor | dropped | `ILoginPacketDescriptor` dropped: interface only (DI / abstraction); implementations audited separately |
| `ILoginProtocolHandler.cs` | ILoginProtocolHandler | merged | `ILoginProtocolHandler` merged into `aaemu-game/src/login/mod.rs` |
| `LoginConnectionFactory.cs` | LoginConnectionFactory | merged | `LoginConnectionFactory` merged into `aaemu-login/src/network/login.rs` |
| `LoginConnectionHandler.cs` | LoginConnectionHandler | merged | `LoginConnectionHandler` merged into `aaemu-login/src/network/login.rs` |
| `LoginPacket.cs` | LoginPacket | merged | `LoginPacket` merged into `aaemu-game/src/login/mod.rs` |
| `LoginPacketDescriptor.cs` | LoginPacketDescriptor | dropped | `LoginPacketDescriptor` dropped: DI factory / descriptor scaffolding; the Rust login builds flows and sessions directly (aaemu-login/src/controllers, network) |
| `LoginProtocolHandler.cs` | LoginProtocolHandler | merged | `LoginProtocolHandler` merged into `aaemu-game/src/login/mod.rs` |
| `ServiceCollectionExtensions.cs` | ServiceCollectionExtensions | merged | `ServiceCollectionExtensions` merged into `aaemu-login/src/packets/c2l.rs` |

### `AAEmu.Login/Core/PacketHandlers`

| File | Types | Status | Rust |
|---|---|---|---|
| `IInternalPacketHandler.cs` | IInternalPacketHandler | dropped | `IInternalPacketHandler` dropped: interface only (DI / abstraction); implementations audited separately |
| `IInternalPacketHandler`T.cs` | IInternalPacketHandler | dropped | `IInternalPacketHandler` dropped: interface only (DI / abstraction); implementations audited separately |
| `ILoginPacketHandler.cs` | ILoginPacketHandler | dropped | `ILoginPacketHandler` dropped: interface only (DI / abstraction); implementations audited separately |
| `ILoginPacketHandler`T.cs` | ILoginPacketHandler | dropped | `ILoginPacketHandler` dropped: interface only (DI / abstraction); implementations audited separately |
| `IPacketHandler.cs` | IPacketHandler | dropped | `IPacketHandler` dropped: interface only (DI / abstraction); implementations audited separately |
| `ServiceCollectionExtensions.cs` | ServiceCollectionExtensions | merged | `ServiceCollectionExtensions` merged into `aaemu-login/src/packets/c2l.rs` |

### `AAEmu.Login/Core/PacketHandlers/C2L`

| File | Types | Status | Rust |
|---|---|---|---|
| `CACancelEnterWorldPacketHandler.cs` | CACancelEnterWorldPacketHandler | merged | `CACancelEnterWorldPacketHandler` merged: aaemu-login/src/handlers.rs handle_c2l (one match arm per handler) |
| `CAChallengeResponse2PacketHandler.cs` | CAChallengeResponse2PacketHandler | merged | `CAChallengeResponse2PacketHandler` merged: aaemu-login/src/handlers.rs handle_c2l (one match arm per handler) |
| `CAChallengeResponsePacketHandler.cs` | CAChallengeResponsePacketHandler | merged | `CAChallengeResponsePacketHandler` merged: aaemu-login/src/handlers.rs handle_c2l (one match arm per handler) |
| `CAEnterWorldPacketHandler.cs` | CAEnterWorldPacketHandler | merged | `CAEnterWorldPacketHandler` merged: aaemu-login/src/handlers.rs handle_c2l (one match arm per handler) |
| `CAListWorldPacketHandler.cs` | CAListWorldPacketHandler | merged | `CAListWorldPacketHandler` merged: aaemu-login/src/handlers.rs handle_c2l (one match arm per handler) |
| `CAOtpNumberPacketHandler.cs` | CAOtpNumberPacketHandler | merged | `CAOtpNumberPacketHandler` merged into `aaemu-login/src/network/login/tests.rs` |
| `CAPcCertNumberPacketHandler.cs` | CAPcCertNumberPacketHandler | merged | `CAPcCertNumberPacketHandler` merged into `aaemu-login/src/network/login/tests.rs` |
| `CARequestAuthGameOnPacketHandler.cs` | CARequestAuthGameOnPacketHandler | merged | `CARequestAuthGameOnPacketHandler` merged: aaemu-login/src/handlers.rs handle_c2l (one match arm per handler) |
| `CARequestAuthMailRuPacketHandler.cs` | CARequestAuthMailRuPacketHandler | merged | `CARequestAuthMailRuPacketHandler` merged: aaemu-login/src/handlers.rs handle_c2l (one match arm per handler) |
| `CARequestAuthPacketHandler.cs` | CARequestAuthPacketHandler | merged | `CARequestAuthPacketHandler` merged: aaemu-login/src/handlers.rs handle_c2l (one match arm per handler) |
| `CARequestAuthTencentPacketHandler.cs` | CARequestAuthTencentPacketHandler | merged | `CARequestAuthTencentPacketHandler` merged into `aaemu-login/src/handlers.rs` |
| `CARequestAuthTrionPacketHandler.cs` | CARequestAuthTrionPacketHandler | merged | `CARequestAuthTrionPacketHandler` merged: aaemu-login/src/handlers.rs handle_c2l (one match arm per handler) |
| `CARequestReconnectPacketHandler.cs` | CARequestReconnectPacketHandler | merged | `CARequestReconnectPacketHandler` merged: aaemu-login/src/handlers.rs handle_c2l (one match arm per handler) |
| `LoginDeniedReason.cs` | LoginDeniedReason | ported | `LoginDeniedReason` → `aaemu-login/src/models.rs` |

### `AAEmu.Login/Core/PacketHandlers/G2L`

| File | Types | Status | Rust |
|---|---|---|---|
| `GLGameServerLoadPacketHandler.cs` | GLGameServerLoadPacketHandler | merged | `GLGameServerLoadPacketHandler` merged: aaemu-login/src/handlers.rs handle_g2l |
| `GLPlayerEnterPacketHandler.cs` | GLPlayerEnterPacketHandler | merged | `GLPlayerEnterPacketHandler` merged: aaemu-login/src/handlers.rs handle_g2l |
| `GLPlayerReconnectPacketHandler.cs` | GLPlayerReconnectPacketHandler | merged | `GLPlayerReconnectPacketHandler` merged: aaemu-login/src/handlers.rs handle_g2l |
| `GLRegisterGameServerPacketHandler.cs` | GLRegisterGameServerPacketHandler | merged | `GLRegisterGameServerPacketHandler` merged: aaemu-login/src/handlers.rs handle_g2l |
| `GLRequestInfoPacketHandler.cs` | GLRequestInfoPacketHandler | merged | `GLRequestInfoPacketHandler` merged: aaemu-login/src/handlers.rs handle_g2l |

### `AAEmu.Login/Core/Packets/C2L`

| File | Types | Status | Rust |
|---|---|---|---|
| `CACancelEnterWorldPacket.cs` | CACancelEnterWorldPacket | merged | `CACancelEnterWorldPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CAChallengeResponse2Packet.cs` | CAChallengeResponse2Packet | merged | `CAChallengeResponse2Packet` merged into `aaemu-login/src/packets/c2l.rs` |
| `CAChallengeResponsePacket.cs` | CAChallengeResponsePacket | merged | `CAChallengeResponsePacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CAEnterWorldPacket.cs` | CAEnterWorldPacket | merged | `CAEnterWorldPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CAListWorldPacket.cs` | CAListWorldPacket | merged | `CAListWorldPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CAOtpNumberPacket.cs` | CAOtpNumberPacket | merged | `CAOtpNumberPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CAPcCertNumberPacket.cs` | CAPcCertNumberPacket | merged | `CAPcCertNumberPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CARequestAuthGameOnPacket.cs` | CARequestAuthGameOnPacket | merged | `CARequestAuthGameOnPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CARequestAuthMailRuPacket.cs` | CARequestAuthMailRuPacket | merged | `CARequestAuthMailRuPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CARequestAuthPacket.cs` | CARequestAuthPacket | merged | `CARequestAuthPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CARequestAuthTencentPacket.cs` | CARequestAuthTencentPacket | merged | `CARequestAuthTencentPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CARequestAuthTrionPacket.cs` | CARequestAuthTrionPacket | merged | `CARequestAuthTrionPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CARequestReconnectPacket.cs` | CARequestReconnectPacket | merged | `CARequestReconnectPacket` merged into `aaemu-login/src/packets/c2l.rs` |
| `CLOffsets.cs` | CLOffsets | merged | `CLOffsets` merged into `aaemu-login/src/packets/c2l.rs` |

### `AAEmu.Login/Core/Packets/G2L`

| File | Types | Status | Rust |
|---|---|---|---|
| `GLGameServerLoadPacket.cs` | GLGameServerLoadPacket | ported | `GLGameServerLoadPacket` → `aaemu-game/src/login/packets.rs` |
| `GLOffsets.cs` | GLOffsets | merged | `GLOffsets` merged into `aaemu-game/src/opcodes.rs` |
| `GLPlayerEnterPacket.cs` | GLPlayerEnterPacket | ported | `GLPlayerEnterPacket` → `aaemu-game/src/login/packets.rs` |
| `GLPlayerReconnectPacket.cs` | GLPlayerReconnectPacket | ported | `GLPlayerReconnectPacket` → `aaemu-game/src/login/packets.rs` |
| `GLRegisterGameServerPacket.cs` | GLRegisterGameServerPacket | ported | `GLRegisterGameServerPacket` → `aaemu-game/src/login/packets.rs` |
| `GLRequestInfoPacket.cs` | GLRequestInfoPacket | ported | `GLRequestInfoPacket` → `aaemu-game/src/login/packets.rs` |

### `AAEmu.Login/Core/Packets/L2C`

| File | Types | Status | Rust |
|---|---|---|---|
| `ACAccountWarnedPacket.cs` | ACAccountWarnedPacket | merged | `ACAccountWarnedPacket` merged into `aaemu-login/src/packets/l2c.rs` |
| `ACAuthResponsePacket.cs` | ACAuthResponsePacket | merged | `ACAuthResponsePacket` merged into `aaemu-login/src/packets/l2c.rs` |
| `ACChallenge2Packet.cs` | ACChallenge2Packet | merged | `ACChallenge2Packet` merged into `aaemu-login/src/network/login/tests.rs` |
| `ACChallengePacket.cs` | ACChallengePacket | merged | `ACChallengePacket` merged into `aaemu-login/src/auth.rs` |
| `ACEnterOtpPacket.cs` | ACEnterOtpPacket | merged | `ACEnterOtpPacket` merged into `aaemu-login/src/packets/l2c.rs` |
| `ACEnterPcCertPacket.cs` | ACEnterPcCertPacket | merged | `ACEnterPcCertPacket` merged into `aaemu-login/src/packets/l2c.rs` |
| `ACEnterWorldDeniedPacket.cs` | ACEnterWorldDeniedPacket | merged | `ACEnterWorldDeniedPacket` merged into `aaemu-login/src/packets/l2c.rs` |
| `ACJoinResponsePacket.cs` | JoinResponseReason, AfsValue, ACJoinResponsePacket | ported | `JoinResponseReason` → `aaemu-login/src/packets/l2c.rs`; `AfsValue` → `aaemu-login/src/packets/l2c.rs`; `ACJoinResponsePacket` merged into `aaemu-login/src/packets/l2c.rs` |
| `ACLoginDeniedPacket.cs` | ACLoginDeniedPacket | merged | `ACLoginDeniedPacket` merged into `aaemu-login/src/network/login/tests.rs` |
| `ACShowArsPacket.cs` | ACShowArsPacket | merged | `ACShowArsPacket` merged into `aaemu-login/src/packets/l2c.rs` |
| `ACWorldCookiePacket.cs` | ACWorldCookiePacket | merged | `ACWorldCookiePacket` merged into `aaemu-login/src/packets/l2c.rs` |
| `ACWorldListPacket.cs` | ACWorldListPacket | merged | `ACWorldListPacket` merged into `aaemu-login/src/packets/l2c.rs` |
| `ACWorldQueuePacket.cs` | ACWorldQueuePacket | merged | `ACWorldQueuePacket` merged into `aaemu-login/src/packets/l2c.rs` |
| `LCOffsets.cs` | LCOffsets | merged | `LCOffsets` merged into `aaemu-login/src/packets/l2c.rs` |

### `AAEmu.Login/Core/Packets/L2G`

| File | Types | Status | Rust |
|---|---|---|---|
| `LGOffsets.cs` | LGOffsets | merged | `LGOffsets` merged into `aaemu-game/src/opcodes.rs` |
| `LGPlayerEnterPacket.cs` | LGPlayerEnterPacket | ported | `LGPlayerEnterPacket` → `aaemu-game/src/opcodes.rs` |
| `LGPlayerReconnectPacket.cs` | LGPlayerReconnectPacket | ported | `LGPlayerReconnectPacket` → `aaemu-game/src/opcodes.rs` |
| `LGRegisterGameServerPacket.cs` | LGRegisterGameServerPacket | ported | `LGRegisterGameServerPacket` → `aaemu-game/src/opcodes.rs` |
| `LGRequestInfoPacket.cs` | LGRequestInfoPacket | ported | `LGRequestInfoPacket` → `aaemu-game/src/opcodes.rs` |

### `AAEmu.Login/Core/Services`

| File | Types | Status | Rust |
|---|---|---|---|
| `IPasswordService.cs` | PasswordVerificationResult, IPasswordService | ported | `PasswordVerificationResult` → `aaemu-login/src/services/password.rs`; `IPasswordService` → `aaemu-login/src/services/password.rs` |
| `KoreaChallengeCrypt.cs` | KoreaChallengeCrypt | merged | `KoreaChallengeCrypt` merged into `aaemu-login/src/services/korea.rs` |
| `KoreanChallengeVerifier.cs` | KoreanChallengeVerifier | merged | `KoreanChallengeVerifier` merged into `aaemu-login/src/services/korea.rs` |
| `Password.cs` | PasswordKind, Password | ported | `PasswordKind` → `aaemu-login/src/services/password.rs`; `Password` → `aaemu-login/src/services/password.rs` |
| `PasswordService.cs` | PasswordService | ported | `PasswordService` → `aaemu-login/src/services/password.rs` |
| `ServiceCollectionExtensions.cs` | ServiceCollectionExtensions | merged | `ServiceCollectionExtensions` merged into `aaemu-login/src/packets/c2l.rs` |
| `Sha256Crypt.cs` | Sha256Crypt | ported | `Sha256Crypt` → `aaemu-login/src/services/mod.rs` |

### `AAEmu.Login/Core/Services/TwoFactor`

| File | Types | Status | Rust |
|---|---|---|---|
| `ArsService.cs` | ArsService | ported | `ArsService` → `aaemu-login/src/services/two_factor.rs` |
| `ArsSession.cs` | ArsSession | ported | `ArsSession` → `aaemu-login/src/services/two_factor.rs` |
| `IArsPhoneProvider.cs` | IArsPhoneProvider | merged | `IArsPhoneProvider` merged into `aaemu-login/src/services/two_factor.rs` |
| `IArsService.cs` | IArsService | ported | `IArsService` → `aaemu-login/src/services/two_factor.rs` |
| `IOtpService.cs` | IOtpService | ported | `IOtpService` → `aaemu-login/src/services/two_factor.rs` |
| `IPcCertService.cs` | IPcCertService | ported | `IPcCertService` → `aaemu-login/src/services/two_factor.rs` |
| `ITwoFactorRepository.cs` | ITwoFactorRepository | ported | `ITwoFactorRepository` → `aaemu-login/src/services/two_factor.rs` |
| `ITwoFactorService.cs` | ITwoFactorService | ported | `ITwoFactorService` → `aaemu-login/src/services/two_factor.rs` |
| `OtpService.cs` | OtpService | ported | `OtpService` → `aaemu-login/src/services/two_factor.rs` |
| `OtpValidationResult.cs` | OtpValidationResult | merged | `OtpValidationResult` merged: aaemu-login/src/services/two_factor.rs (Result values of the OTP / PC-cert checks) |
| `PcCertService.cs` | PcCertService | ported | `PcCertService` → `aaemu-login/src/services/two_factor.rs` |
| `PcCertValidationResult.cs` | PcCertValidationResult | merged | `PcCertValidationResult` merged: aaemu-login/src/services/two_factor.rs (Result values of the OTP / PC-cert checks) |
| `TwoFactorRepository.cs` | TwoFactorRepository | ported | `TwoFactorRepository` → `aaemu-login/src/services/two_factor.rs` |
| `TwoFactorService.cs` | TwoFactorService | ported | `TwoFactorService` → `aaemu-login/src/services/two_factor.rs` |

### `AAEmu.Login/Models`

| File | Types | Status | Rust |
|---|---|---|---|
| `AccountId.cs` | AccountId | ported | `AccountId` → `aaemu-login/src/models.rs` |
| `AppConfiguration.cs` | AppConfiguration, GameServerConfig | merged | `AppConfiguration` merged into `aaemu-game/src/config.rs`; `GameServerConfig` → `aaemu-login/src/config.rs` |
| `ConnectionId.cs` | ConnectionId | ported | `ConnectionId` → `aaemu-login/src/models.rs` |
| `DBConnectionsConfig.cs` | DBConnectionsConfig | merged | `DBConnectionsConfig` merged into `aaemu-login/src/config.rs` |
| `EnterWorldResult.cs` | EnterWorldResult | ported | `EnterWorldResult` → `aaemu-login/src/network/login.rs` |
| `GSLoad.cs` | GSLoad | merged | `GSLoad` merged into `aaemu-login/src/models.rs` |
| `GSRegisterResult.cs` | GSRegisterResult | merged | `GSRegisterResult` merged into `aaemu-login/src/models.rs` |
| `GameServer.cs` | GameServer | ported | `GameServer` → `aaemu-login/src/controllers/game.rs` |
| `GameServerId.cs` | GameServerId | ported | `GameServerId` → `aaemu-login/src/models.rs` |
| `InternalNetworkConfig.cs` | InternalNetworkConfig | merged | `InternalNetworkConfig` merged into `aaemu-login/src/config.rs` |
| `KoreaAuthInfo.cs` | KoreaAuthInfo | ported | `KoreaAuthInfo` → `aaemu-login/src/models.rs` |
| `LoginResult.cs` | LoginResult | ported | `LoginResult` → `aaemu-login/src/models.rs` |
| `NetworkConfig.cs` | NetworkConfig | ported | `NetworkConfig` → `aaemu-game/src/config.rs` |
| `PublicNetworkConfig.cs` | PublicNetworkConfig | merged | `PublicNetworkConfig` merged into `aaemu-login/src/config.rs` |
| `ReconnectResult.cs` | ReconnectResult | ported | `ReconnectResult` → `aaemu-login/src/models.rs` |
| `WorldListResult.cs` | WorldListResult | ported | `WorldListResult` → `aaemu-login/src/controllers/game.rs` |

### `AAEmu.Login/Models/TwoFactor`

| File | Types | Status | Rust |
|---|---|---|---|
| `TwoFactorConfig.cs` | TwoFactorConfig | ported | `TwoFactorConfig` → `aaemu-login/src/models.rs` |
| `TwoFactorMethod.cs` | TwoFactorMethod | ported | `TwoFactorMethod` → `aaemu-login/src/models.rs` |
| `TwoFactorRequirements.cs` | TwoFactorRequirements | ported | `TwoFactorRequirements` → `aaemu-login/src/models.rs` |

### `AAEmu.Login/Utils`

| File | Types | Status | Rust |
|---|---|---|---|
| `IIdManager.cs` | IIdManager | ported | `IIdManager` → `aaemu-game/src/managers/id.rs` |
| `IMySqlConnectionFactory.cs` | IMySqlConnectionFactory | merged | `IMySqlConnectionFactory` merged into `aaemu-login/src/db.rs` |
| `IdManager.cs` | IdManager | ported | `IdManager` → `aaemu-game/src/managers/id.rs` |
| `MySQLInitializer.cs` | MySqlInitializer | merged | `MySqlInitializer` merged into `aaemu-login/src/db.rs` |
| `MySqlConnectionFactory.cs` | MySqlConnectionFactory | merged | `MySqlConnectionFactory` merged into `aaemu-login/src/db.rs` |
| `SimpleIdManager.cs` | SimpleIdManager | ported | `SimpleIdManager` → `aaemu-login/src/id_manager.rs` |


