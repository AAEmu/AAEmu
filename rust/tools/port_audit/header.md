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

Final generator counts: {COUNTS}.

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
