# How the ArcheAge 10.x zone server uses physics and world geometry

This document is the research input for porting what the ArcheAge **zone server** does with
CryEngine physics into the Rust world server (`aaemu-game`). The CryPhysics engine
internals are being reverse-engineered separately. This document covers the zone's *use* of
physics: what it creates, what it queries, what it reports to the world server, and which
world-geometry files it loads.

Sources. Everything below was derived from the 10.0.2.13 r575 binaries and data. The
published CRYENGINE source was not used.

* `x2game-dev_dedicate.dll` (image base 0x39000000). It was already decompiled:
  `D:\ArcheAge Returns 10.0.2.13 - 8yx - r575 - 2026-06-18\Bin64\decompiled\x2game-dev_dedicate.{c,funcs.csv,strings.txt}`.
* `cry3dengine_dedicate`, `cryaction_dedicate`, `cryentitysystem_dedicate`,
  `cryaisystem_dedicate`, `crysystem` and `cryphysics_dedicate`. These were decompiled
  headless for this work into `D:\aa-re\zone\out\*.c` (script `D:\aa-re\zone\scripts\export_all.py`).
* The `game_pak` contents. A Python reader is at `D:\aa-re\zone\scripts\pak.py`, the full
  listing at `D:\aa-re\zone\out\pak10.tsv` (523,291 files), and the 1.2 listing at `pak12.tsv`.
* Detailed working notes and tools:
  * `D:\aa-re\zone\agent_proto\zone_protocol_physics.md`: every WZ/ZW packet, with field
    layouts, handler addresses and constants. Full WZ opcode table in `wz_opcodes_fixed.txt`.
  * `D:\aa-re\zone\agent_geom\`: geometry loading, the CGF format, model counts, and the
    `.dat` and `.bai` formats. Tools are in `tools\`.

Addresses are virtual addresses in the named DLL. Names in quotes come from the binary's own
log strings and RTTI. Names without quotes describe the observed behaviour.

---

## 0. Architecture: what "the zone" is

* **Process.** `dedicatedserver.exe` (18 KB; it must sit in `Bin64` because it statically
  imports `xlcommon.dll`) does the following:
  1. Calls `XlSetWorkingDir`.
  2. Creates the save-dir junction `..\Documents`.
  3. Reads `-devmode` or the first line of `<savedir>/devmode.cfg`.
  4. Appends `+zone w_gweonid_forest_1` if the command line has no `+zone`.
  5. Loads `x2game-dev_dedicate.dll` and calls `CreateGameStartup()`, passing the startup
     struct (revision 575, the log name `ArcheAge.log`, and the `game_pak` size).

  Pseudo-code: `D:\aa-re\zone\out\dedicatedserver.c` (`WinMain_0`).
* **One process per zone.** `+zone <name>` names a `<Zone>` from
  `game/worlds/<world>/world.xml`. Each zone owns a `cellList` and, inside each cell, a
  `sectorList`. main_world has 164 zones, 2,111 zone-cell entries and 199,648 sector entries.
  Global world facts: `resolution=1024 unitSize=2 cellXCount=35 cellYCount=38 oceanLevel=100 maxTerrainHeight=4096`.
* **The zone is a client of the world server.** The class is `X2::GameServer::ZoneClient`.
  It reads the CVars `world_serveraddr` and `world_serverport` (default port **1240**; reader
  at 0x39332B50). The protocol is WZ (world to zone, 133 types) and ZW (zone to world).
  The same DLL also contains the full game client (`X2::GameClient::*`). The zone code lives
  in `X2::GameServer::*` together with the shared `X2::*` unit, model and physics code.
* **Division of labour.** The world server (AAEmu's role) owns units, stats, skills,
  damage, quests, DB and persistence. The zone owns:
  * the CryEngine level: terrain, brushes, vegetation, water, areas, AI navigation;
  * a physics copy of every relevant unit, doodad, house and gimmick;
  * NPC AI movement and pathing;
  * ship simulation;
  * movement verification;
  * collisions and physics events.

  It reports results back as ZW packets. **Everything the zone computes, the world server
  only receives as events.** In this design the world never does geometry itself.

### Global environment (x2game `gEnv = qword_3D8B0EE0`)
CrySystem stores the subsystems in `CSystem` and `gEnv = CSystem+80`:

| field | object | evidence |
|---|---|---|
| +56 | IConsole | CVar lookups (`vt192 GetCVar`) |
| +88 | I3DEngine | `a1[21] = CreateCry3DEngine()` in crysystem `sub_365BC0B0` |
| +112 | **IPhysicalWorld** | `a1[24] = CreatePhysicalWorld(a1)` (crysystem `sub_365BC9E0`, "Initializing Physics...", loads `CryPhysics_dedicate.dll` on dedicated) |
| +128 | IAISystem | `*(a1+208) = CreateAISystem()` |
| +136 | IEntitySystem | `a1[27] = CreateEntitySystem()` |

### IPhysicalWorld methods used by x2game
The table below combines an inventory of every `gEnv->pPhysicalWorld` call in x2game
(`D:\aa-re\zone\out\x2game_physworld_calls.tsv`, made by `scripts\genv_calls.py`, about 140
direct call sites) with the `CPhysicalWorld` vtable (cryphysics_dedicate vtable 0x351F0AF8).
Each name comes from the cryphysics implementation's own strings or from the call signature.

| vt off | method (evidence) | x2game call sites | zone use |
|---|---|---|---|
| +72 | GetGeomManager (returns the geometry factory; `+56` CreatePrimitive, `+72` RegisterGeometry) | 26 / 8 funcs | builds box/capsule/cylinder geometry for ships, ropes, doodad proxies (e.g. 0x3922B7F0) |
| +80 | GetPhysUtils | 6 | utils (convex hull etc.) |
| +160 | GetPhysVars | 5 | reads gravity and step settings |
| +176 | CreatePhysicalEntity(type, params, foreignData, foreignId, id, host) (`type=2` at 0x3922B7F0) | 1 direct + via IEntity::Physicalize | rope and anchor helpers. Most entities are created through `IEntity::Physicalize` (IEntity vt+528). |
| +192 | DestroyPhysicalEntity ("…func:CPhysicalWorld::DestroyPhysicalEntity") | 3 | |
| +208 / +216 | GetPhysicalEntityId / GetPhysicalEntityById | 3 / 5 | event to entity mapping |
| +320 | GetEntitiesInBox(min, max, list, objtypes, prealloc) (`objtypes=14`) | 7 | AoE and proximity (gimmicks, doodad collide, siege) |
| +336 | **RayWorldIntersection(SRWIParams&, "RayWorldIntersection(Game)", caller)** | 23 sites / 20 funcs | WZRayCasting, surface checks, ground snapping, LoS |
| +400 | **SimulateExplosion(pe_explosion*, skip, nskip, iTypes=7, caller=4)** | 1 | WZPhysicalExplosion |
| +480 / +488 / +496 | CollideEntityWithBeam / ray-trace-entity family (entity-specific sweeps with `geomflags 65023`) | 8 / 1 / 2 | projectile and beam vs. one entity, rope hits |
| +512 | **PrimitiveWorldIntersection** (string "PrimitiveWorldIntersection") | 37 sites / 29 funcs | capsule/box sweeps: blink, leap, knockback end, spawn and placement checks |
| +696 | water/ocean-level style query (13 args) | 5 | ocean |
| +712 | frame or step id | 2 | |

Physical-entity methods used through `IPhysicalEntity*` include SetParams, GetParams,
GetStatus (`pe_status_pos`, `pe_status_dynamics` type 8) and Action (`vt80`,
`pe_action_impulse` / `set_velocity`). The `pe_*` type ids are listed per feature below; their
struct layouts belong to the CryPhysics RE.

---

## 1. Where the zone uses physics, by game feature

The "Rust subsystem" column names where the equivalent belongs in `aaemu-game`.

### 1.1 Physical world creation and settings
* crysystem `InitPhysics` (string "Initializing Physics..."):
  1. `LoadLibrary("CryPhysics_dedicate.dll")` on dedicated builds.
  2. `CreatePhysicalWorld(ISystem*)`.
  3. Registers the `p_*` CVars on `PhysicsVars` (`GetPhysVars()`, vt+160), including:
     `p_single_step_mode`, `p_do_step`, `p_fixed_timestep`, `p_max_substeps`,
     `p_max_contact_gap(_player)`, `p_prohibit_unprojection`, `p_damping_group_size`,
     `p_group_damping`, `p_max_substeps_large_group`, `p_num_bodies_large_group`,
     `p_time_granularity`, `p_max_world_step`, `p_max_velocity`, `p_max_player_velocity`,
     `p_max_MC_iters`, `p_accuracy_MC`, `p_*LCPCG*`, `p_min_separation_speed`,
     `p_use_distance_contacts`, `p_unproj_vel_scale`, `p_max_unproj_vel`, `p_penalty_scale`,
     `p_max_contact_gap_simple`, `p_skip_redundant_colldet`, `p_limit_simple_solver_energy`,
     `p_tick_breakable`, `p_joint_dmg_accum*`, `p_lattice_max_iters`, `p_max_entity_cells`,
     `p_GEB_max_cells`, `p_ray_on_grid_max_size`, `p_max_contacts`, `p_max_plane_contacts*`.
     The source is `D:\aa-re\zone\out\crysystem.c` around line 150425.
* Gravity is the physics-vars default. The game logic uses g = 20 m/s² for falls
  (`√(40·h)`, see 1.3). Game physics CVars are `sys_physics_CPU` (set by WZPhysicsCPU),
  `g_use_physicalize_rigid` and `e_phys_ocean_cell`.
* **Ocean.** `world.xml oceanLevel=100`. `zone_groups.ocean_simulate` and the CVar
  `e_water_ocean_simulate_on_zone` select which zones simulate the ocean surface (waves);
  `zone_groups.enable_physics_collision_damage` gates collision damage.

### 1.2 Entities the zone physicalizes

| game object | physics entity | where | notes / Rust owner |
|---|---|---|---|
| Terrain | heightfield (3D engine terrain) | cry3dengine terrain load (§2) | `npcs::heightmap` already has the heights. No physics heightfield is needed if queries go through it. |
| Brushes, static objects | static (`PE_STATIC`), one per brush instance, CGF physics geometry | cry3dengine brush/statobj load (§2) | **missing in Rust**. Needed for LoS, sweeps and ship collision with piers or rocks. |
| Vegetation | static parts (trunks), many unphysicalized | cry3dengine vegetation (§2); WZVegetationCutdown clears trunk collision | missing |
| Characters (players) | **living** entity, built from `actor_models.physics_living_*` (mass, gravity, air control, max climb/slide/fall angles, max ground velocity, collider material, time-impulse-recover) | `WZUnitState` (op 7) creates or updates the copy; `WZUnitMovement` (op 9) applies client moves via actor vt160 | used for move verification, fall and knockback/impulse. Rust: `world::session::move_unit` + `skills::on_moved`. |
| NPCs | living entity (same model columns); AI-driven movement | zone AI / spawners (`npc_spawners.g`, `NpcSpawner::*`) | zone-side pathing (CryAI navigation, `MoveSkillController::RequestPathFinding`). Rust: `systems::ai` (+ `geodata` navmesh). |
| Mates (mounts) | living | WZUnitState | as characters |
| Ships (slaves, model kind 4) | **rigid** (`IEntity::Physicalize` type 2, mass from ship desc, `pe_params` 6/10/12 for buoyancy/flags/simulation) + per-part geometry (hull, buoy, keel, ballast; see `um_ship_draw_physics_geom` "1:all, c:collision, b:buoy, k:keel, a:ballast") | `X2::ShipUnitModel::InitPhysicalEntity`, `ShipUnitModel::ApplySubmergeDepth`, `SetAddedMass` | **the zone is the authoritative ship simulator** (§1.4). Rust: `systems::ai::physics` (Jitter2 port) + `vehicles::ship_physics`. |
| Land vehicles (model kind 3: carts, tanks, siege) | **wheeled vehicle** (`VehicleUnitModel::InitWheels`, `UpdateCenterSpindle`, "failed to physicalize vehicle") | `X2::ServerVehicleSimulationController::UpdateWheeledVehicle` (log "failed to get car params") | **not modelled in Rust** (vehicles move by client MoveType only) |
| Doodads | static or rigid from the model CGF; `_UpdateCollisionParams` toggles collision per phase; `PhysicalProxyScaler` for growing doodads | `WZCreateDoodad` / `WZRemoveDoodad` / `WZDoodadChangePhase` (ops 115–117), `X2::GameServer::ZdModelPrefab::Load`, `ZdModelVegetation::Load` | needed for placement and LoS. Rust: `npcs::doodad` (no collision). |
| Houses | static from the house model; swapped at build done | `WZHouseState` (6), `WZHouseBuildDone` (77) | placement checks. Rust: `systems::housing`. |
| Gimmicks (catapult balls, swinging logs, breakables) | static or **rigid** with joints (breakable) | `ServerGimmick::InitStaticEntity` / `InitDynamicEntity`, `WZGimmick*` (89–94); static ones come from the level entities (flag 0x100000) | Rust: `systems::ai::gimmick` (kinematic only) |
| Ropes (harpoon, ballista, rope skill) | rope entities + launcher/anchor helpers | `X2::Physics::CreateRopeSet`, rope skill controller | Rust: `vehicles::harpoon` + `ai::physics::harpoon` (tow forces only) |
| Area / water volumes | area entities (triggers and water) | level `object.dat`, `entities.xml`; `DoodadWaterVolumeData` (water-level doodads) | Rust: `ai::physics::water` (object.dat water bodies) |

### 1.3 Movement validation and fall damage (`WZUnitMovement` op 9 → ZW 16/57)
* Fall: `ZWUnitFellPacket` (op 16), sender 0x3935C850, landing logic at 0x39232570.
  1. The zone tracks the maximum downward speed and the fall start z.
  2. On landing (not swimming, levitating, or on a ship) it computes
     `impactVel = min(maxDownVel, √(40·(startZ − z)))`.
  3. It sends `impactVel` only if `impactVel ≥ pl_fallDamage_SpeedSafe` (20 m/s).
     Related CVars: `pl_fallDamage_SpeedFatal` 63.25 and `SpeedBias` 1.5.
  4. The world computes the damage.

  It also fires when a client move reports `fallVel > 20`. **Rust:** `skills::combat::do_fall_damage`
  already takes the client `fallVel` (the C# behaviour). The zone's height-based cap
  (`√(40·h)`) needs the terrain or ground height at the fall start and the landing point:
  `npcs::heightmap` gives the terrain, and brushes are missing.
* Move hack: `ZWReportMoveHackPacket` (op 57), verifier 0x3921E300 → sample builders
  0x39218810 (character) and 0x39219740 (vehicle), report 0x3921BD10.
  * It runs only when `movement_verify_enable` is on (default 0).
  * Reasons: client clock (0, 1), on-ground (2), gravity (3), air-standing (4), speed (6),
    packet rate (9), over-velocity (10), under terrain (11: `terrainZ − z > 0.1` via
    `I3DEngine::GetTerrainElevation`, vt+1064), z < −1 (12), pattern (14).
  * Teleport: 15/16/17 at >150 m/s and >50 m, >100 and >25, >50 and >10.
  * Vehicle levitation > 5 s (18) and vehicle warp > 500 m (19).

  **Rust:** a new movement verifier next to `world::session::move_unit`. All of it is
  arithmetic plus `heightmap` lookups.
* `ZWReportDoodadSurfaceHack` (op 58, 0x394510F0). It casts a ray from the new doodad
  z+100 down 500 m against terrain and water, and reports when the surface type is not in
  the doodad template's allowed list. **Rust:** doodad placement validation (needs the
  terrain surface-type map from `cover.ctc` / `surface.xml`, §2).

### 1.4 Ships, vehicles and collision damage
* `WZShipControlChange` (op 75, handler 0x39363980 → 0x3935F470) gives a ship to, or takes
  it from, the zone's simulator `X2::ServerShipSimulationController`:
  * `UpdatePhysics` 0x39237520 computes buoyancy, thrust and rudder through CryPhysics. Debug
    output: "[Physics] mass / Velocity Forward / Side / Angular".
  * `SendMovement` 0x39236B60 sends `ZWUnitMovements` (op 8, MoveType 4) at most every 50 ms.
  * Before sending, the state is validated by `X2::_IsValidShipMovement` 0x39228AE0
    (rejects NaN, Inf and non-unit quaternions).
  * After 3 s below 1 m/s the ship is flagged "stopped".

  **Rust:** `systems::ai::physics` (Jitter2 port of the 1.2 C# ship model) +
  `vehicles::ship_physics` plays this role today. The 10.x zone uses CryPhysics rigid bodies
  with per-part buoy, keel and ballast geometry from the ship model (`ShipUnitModel::Init*`),
  so exact parity needs the ship CGF part geometry and the CryPhysics buoyancy model (the
  other RE track).
* Collision damage: `ZWUnitCollisionPacket` (op 17). The handler is the physics collision
  event 0x390AF3C0, then the manager 0x390AEC80 and the resolver 0x390AF030.
  * Closing speed is `v = −dot(v0 − v1, n)`.
  * Only units of model kind 4 (ship) and 3 (vehicle) count. Terrain and static objects
    count as fixed mass (expression constant 21).
  * Contacts at or below `expr_const[13]` are dropped. The manager keeps the max per pair
    and clamps it to `expr_const[12]` every 500 ms.
  * The world applies the `physics_collision_{front,side,rear,top,bottom}_damage_mul` and
    `physics_collision_armor_mul` columns (model tables) and `zone_groups.enable_physics_collision_damage`.

  **Rust:** `ai::physics::contacts` already produces ship–shore, ship–ship and doodad
  contacts. Wire these into a collision-damage function with the same max-per-pair 500 ms
  window.
* Wheeled vehicles: `ServerVehicleSimulationController::UpdateWheeledVehicle` /
  `SendMovement`, `VehicleUnitModel::{InitWheels,UpdateDriver,UpdateCenterSpindle}`. This
  is a zone-simulated wheeled vehicle. **Rust:** not present (lower priority; the client
  drives it with MoveType 3 and the world could stay kinematic, clamping to terrain).

### 1.5 Skills: targeting, line of sight, displacement, AoE
* `WZRayCasting` (op 122, 0x3936F340) answers with `ZWRayCastingResult` (op 48). It calls
  `RayWorldIntersection` with objtypes 0x31F and flags 0xFFFE048F, or, in water-level mode,
  I3DEngine vt+568 (water level). This is the world's generic "ask the zone for a ray". The
  client-side companion is `X2::GameClient::PhysicsManager::RequestServerRayCasting` /
  `RequestServerWaterLevelCasting` / `ServerRayCastingFrontalCone`, used for ground-targeted
  skills and the `skills.check_obstacle` / `check_terrain` columns.
  **Rust:** `systems::skills` (`geometry.rs`, `water.rs`) needs a `raycast(world, from, to) -> Option<hit>`
  service: heightmap first, then static geometry.
* `WZBlinkUnit` (op 50, 0x39369B10 → `GetBlinkPosition` 0x3925D990 / 0x3925E670):
  1. Capsule sweep (r 0.35 or the actor's physics dimensions) with
     `PrimitiveWorldIntersection` (entTypes 15).
  2. Back off 0.1 m from the hit.
  3. Allow slopes up to 30°.

  **Rust:** `skills::controllers` (Leap/Dash) and teleport effects. It needs a swept-capsule
  query (heightmap + static colliders).
* `WZKnockBackUnit` (51, 0x39258BB0) and `WZImpulseUnit` (52, `X2::Unit::Impulse`
  0x3925C7D0). The impulse handler rotates the vectors by the source-to-target yaw and then
  applies `pe_action_impulse` / `set_velocity` to the living entity (`impulse_mass_min` 40),
  so the unit really flies. **Rust:** `skills::effects` (`ImpulseEffect`, knockback). The
  `sc_unit_impulse_packet` exists; the end position must be integrated against the
  heightmap (ballistic with g = 20, stop on ground).
* `WZPhysicalExplosion` (95, 0x39363E90 → 0x39336830) runs `SimulateExplosion` with radius,
  hole size and pressure from `physical_explosion_effects` (`id, hole_size, pressure, radius`).
  It affects static, sleeping and rigid objects, i.e. gimmicks and breakables, not damage.
  **Rust:** `skills::effects::PhysicalExplosionEffect` applies impulse to `ai::gimmick`
  bodies only.
* Projectiles: `projectiles.proj_physic_id` and the beam/entity-collide calls (vt+480/488/496,
  `geomflags 65023`) are used by the client and shared code for projectile flight. The world
  decides hits. **Rust:** optional; skills hit-test by distance today.
* AoE: `GetEntitiesInBox` (vt+320, objtypes 14) is used for physics-level proximity (gimmick
  and doodad collide). Skill AoE target selection is world-side (`skills`); the zone does
  not choose AoE targets.

### 1.6 NPC movement and pathing
* NPCs exist on the zone. Spawners come from
  `level_design/zone/<id>/zone_server/npc_spawners.g` (115 files in main_world); the world
  is told with `ZWSpawnNpc` (op 2). The AI moves them: `Npc::_FollowPath`,
  `_FollowUnit`, `GotoAnchor`, `CheckPos`, `MoveSkillController::RequestPathFinding` /
  `OnPathFound` (CryAISystem navigation over the `.bai` navmesh). Results go back as
  `ZWUnitMovements` (op 8, `npc_move_sync_period` 100 ms).
* Related flags: `npc_spawn_on_terrain` / "spawn npc on terrain",
  `npc_check_target_under_terrain` / `npcs.check_target_under_terrain`, and
  `npc_area_spawner_collision_ckeck` (collision test at the spawn point).
* **Rust:** `systems::ai` already runs NPC AI on the world (C# design) with
  `ai::geodata` (A* over `.bai` net/areas/verts/hide missions) and `heightmap`.

### 1.7 Areas, gimmicks and doodads
* `ZWEnterArea` / `ZWLeaveArea` (35/36, 0x3932B1B0 / 0x3932B370) come from CryEngine area
  triggers (`ServerAreaManager::LoadAreas`, `%s/level_design/zone/%d/world_server/%s.xml`).
  **Rust:** quest areas (`npcs::area_trigger`, `subzone`). The geometry is a point-in-shape
  test, already partly done.
* Static gimmicks are read from level entities (flag 0x100000) and requested with
  `ZWRequestSpawnStaticGimmick` (38, 0x393365A0). Dynamic gimmick state goes out as
  `ZWGimmickMovement` (37, every `sg_sync_period` 100 ms), collisions as `ZWGimmickCollided`
  (42, relVel ≥ per-gimmick minimum, 3000 ms immunity), and breakable joints as
  `ZWGimmickJointBroken` / `RemoveParts` / `ResetJoints` (39–41).
  **Rust:** `ai::gimmick` (kinematic).
* `ZWDoodadCollideUnit` (59, 0x394547C0): for doodads with template flag 0x20, an AABB
  then OBB–OBB overlap with units. **Rust:** `npcs::doodad` tick (pure geometry, cheap).

### 1.8 Housing, doodad placement and terrain checks
The zone holds houses and doodads with collision (WZHouseState / WZCreateDoodad). Placement
validity is a world-plus-client check:

* strings `house_cannot_locate_terrain_too_low/high`;
* `Terrain Material Name : %s, CannotPlaceDoodad`;
* `doodad_sim_low_terrain`;
* the surface-hack report above.

**Rust:** `systems::housing` placement needs terrain height range and surface type at
the footprint, plus overlap with existing houses (`housing_area.g`, `HousingConnectorList`).

---

## 2. World geometry the zone loads

Detailed notes:
* `D:\aa-re\zone\agent_geom\sub_x2game.md`: zone start-up, the cell loader, doodads, ocean,
  and the query inventory with ray masks.
* `sub_cgf.md`: the CGF format, brush.dat / vegetation.dat / object.dat layouts, and model
  counts.
* `sub_navmesh.md`: `.bai`.
* Data and tools: `D:\aa-re\zone\agent_geom\data\` (`main_world_cgf_models.tsv`,
  `main_world_cgf_summary.json`, `main_world_veg_groups.tsv`, all 4,900 extracted cell
  `.dat` files) and `tools\` (`pakx.py`, `cgf.py`, `physblob.py`, `baiparse.py`).

### 2.1 Start-up and what gets loaded
1. `dedicatedserver.exe +zone <name>` → x2game `sub_39A2D030` → CryAction "zone" command
   `sub_306FC330` → StartGameContext.
2. CrySystem parses `world.xml` `<cellList>/<sectorList>` (`sub_36565C30`).
3. `SeamlessSystem::LoadZone` (`sub_365704D0`) loads **every cell listed in the zone's
   `cellList`, in full**. The `sectorList` only maps ownership. Each cell is loaded with:
   * terrain: `I3DEngine+288 Seamless_LoadCellTerrain` (0x3015D4A0, `client/terrain/heightmap.dat`);
   * objects: `+296 Seamless_LoadCellObject` (brush/vegetation/object `.dat`).
4. Loaded cells live in a local 8×8 grid around the zone origin cell.

Cells are 1024 m (`resolution=1024`, heightmap `unitSize=2`, i.e. 512 samples per cell edge).
main_world is 35×38 cells, of which 1,225 exist in 10.x (1,205 in 1.2).

### 2.2 Per-cell files (main_world, 10.x pak; the 1.2 pak has the same set)

| file | count | total size | content | zone loader / physics |
|---|---|---|---|---|
| `cells/X_Y/client/terrain/heightmap.dat` | 1,225 | 389 MB (1.2: 702 MB) | terrain heights (quadtree nodes) | `Seamless_LoadCellTerrain` 0x3015D4A0 → CryPhysics **terrain heightfield** (`ent_terrain`). Queries: `GetTerrainElevation` (I3DEngine vt+1064, 38 call sites), `GetTerrainZ` (+1080, housing). **Rust: `npcs::heightmap` reads it (done).** |
| `cells/X_Y/client/terrain/cover.ctc` | 1,220 | 1.79 GB | terrain texture/cover (surface-type layer) | surface types for doodad/house placement (`CannotPlaceDoodad`, surface-hack). Rust: not read. |
| `cells/X_Y/client/statobjs.dat` | 1,225 | 7.7 MB | per-cell model name list (`u32 n; {u32; char[256]}`) | brush model table |
| `cells/X_Y/client/object.dat` | 1,225 | 416 MB | the same model list + material table + octree object nodes (`SOcTreeNodeChunk`) | `PrecacheCGFFromCell` 0x301601C0 + octree loader. **Rust: `ai::physics::objects_file` reads only the water areas from it.** |
| `cells/X_Y/client/brush.dat` | 1,225 | 34 MB | **244,297 brush instances**. Header `u32 ver=1; u32 off[256]; u32 size[256]` (16×16 sectors of 64 m). 128-byte `SBrushChunk` records: AABB, flags, sector-local `Matrix34` @67, material id @115, **statobj index @123** | loaders 0x3017CC30 → 0x3017BA50 → `_LoadBrush` 0x3021E840. `CBrush::Physicalize` 0x30111970 creates a **static entity** with the model's phys geoms 0x1000 (collide), 0x1001 (no-collide) and 0x1002 (obstruct). **Rust: missing.** |
| `cells/X_Y/client/vegetation.dat` | 1,225 | 156 MB | **2,429,076 instances**. Header with 4×4 sectors of 256 m; 64-byte `SVegetationChunk` (sector-local pos @39, scale @51, `u16 group` @55, packed angles) | `CVegetation::Physicalize` 0x30103DC0. Static whenever the group model has physics (`e_on_demand_physics`). 85% of instances are physical. **Rust: missing.** |
| `world.xml` / `vegetation.xml` | 1 / 1 | 6.1 MB / 3.6 MB | zones, cells, sectors / 4,145 vegetation groups (`modelFileName`, …) | |
| `cells/X_Y/client/visareas.dat`, `big_object.dat`, `entities.xml`, `leveldata.xml` | 1,225 each | 54 MB / 0.7 MB / 21 MB / 6 MB | indoor portals/areas; large objects; entity archetypes (areas, triggers, **static gimmicks** with flag 0x100000); level data | entity system (areas → ZWEnterArea; static gimmicks → ZWRequestSpawnStaticGimmick). Rust: not read. |
| `paths/X_Y/{net,areas,verts}missionN.bai` | 9,359 sectors × 3 (+7,184 hide) | | CryAI navmesh per 256 m sector | cryaisystem (§2.5). **Rust: `ai::geodata` reads them.** |
| `level_design/zone/N/zone_server/npc_spawners.g` | 115 (+28 ignore lists) | | zone-side NPC spawners (no geometry) | `NpcManager::LoadSpawners` 0x3934AD80 |
| `level_design/zone/N/world_server/{quest_area,district}.xml` | 121 / 132 | | area/trigger shape entities | `ServerAreaManager::LoadAreas` 0x3932B530 → `IEntitySystem::LoadEntities` |

### 2.3 Collision meshes (CGF)
* The models are CryEngine `.cgf` files: header `"CryTek\0\0"`, chunk table version 0x744 or
  0x745.
* Collision lives in the **`MeshPhysicsData` chunk (0xCCCC0018)**, referenced from
  `Mesh.physicsDataChunkId[4]` @+92. Proxy nodes are named `proxy*`, `$collision` or
  `$physics_proxy`. The loader is `LoadPhysicsDataChunk` 0x301D1FD0, then the
  compiled-physics path 0x30047F40 calls `IGeomManager::LoadPhysGeometry` (cryphysics
  0x3514B340).
* The payload is a **serialized CryPhysics geometry blob**:
  * `u32 ver=1`, a 64-byte `phys_geometry` header, then a geometry type:
    * 0 box;
    * 1 trimesh: verts, u16 indices, material ids, and a serialized AABB/OBB/SingleBox BV
      tree; 341 models share the render mesh's vertices;
    * 4 sphere, 5 cylinder, 6 capsule.
  * A parser (`tools\physblob.py`) handled every blob in main_world with no anomalies.
* **main_world needs 5,730 distinct models** (1.18 GB in the pak). 5,151 of them carry
  physics: 167 MB of physics blobs and 1.89 M collision triangles.
  * Slot 0 (collide): trimesh 4,922, box 974, cylinder 160, sphere 19, capsule 3.
  * 19 editor `solid*` brushes are missing from the pak.

### 2.4 Doodads, houses, ocean and water
* **Doodads are pushed by the world server**, not loaded from cell files on the zone:
  1. `WZCreateDoodadPacket` → `sub_393641A0` → `sub_39453AD0` → `sub_39451EC0`.
  2. The model class is chosen by path prefix: `prefab:`/`cga:` → Prefab, `cgf:` → Brush
     (`CreateSimpleBrush`), `vegetation:` → Vegetation, otherwise Dummy.
  3. `_UpdateCollisionParams` `sub_39451190` sets `pe_params_part` flags on up to 64 parts
     from the `doodad_almighties` collision columns: `collide_vehicle` → 0x08,
     `collide_ship` → 0x80, `no_collision` → only 0x8000 (ray-only).

  In AAEmu the world owns doodads already (`npcs::doodad`; `level_design/cells/X_Y/doodad.g`
  461 files and `world_spawns`). It must give them colliders itself.
* **Ocean.** `sub_392AA7D0` sets `e_water_ocean_simulate_on_zone` and
  `e_phys_ocean_cell` (0.5 / 0.0). `C3DEngine::Update` 0x30143AA0 sets the physics **global
  buoyancy area** (IPhysicalWorld vt+640): flat at `GetWaterLevel`, or a 0.5 m wave
  heightfield across 8192 m on ocean-simulating zones.
* `GetWaterLevel` (I3DEngine vt+568, 0x3013FCB0) checks water-volume areas first, then the
  terrain ocean level (`world.xml oceanLevel` = 100), else −1e6.
* **Rust:** `ai::physics::water` (`object.dat` water bodies + ocean) matches this model.

### 2.5 Navmesh
* `.bai` sectors are 256 m (4×4 per cell), in sector-local coordinates. Loaders in
  cryaisystem: `LoadZone` seamless path 0x3105B8F8, per sector 0x31149CE4 (areas
  0x31149764, verts 0x311A2AC4, net 0x31103E58).
* Formats:
  * verts v2: 32-byte records;
  * net v55: 60-byte nodes, 44-byte links, link flag byte +42 bit 0;
  * areas v22 in 10.x, v21 in 1.2.
* `hidemission*.bai` is never loaded.
* The engine streams a 3×3-sector window. It is unconfirmed how much stays resident on a
  dedicated zone.
* The only x2game consumer is `MoveSkillController::RequestPathFinding` 0x39227ED0, through
  IAISystem (gEnv+128). Walkability uses physics sweeps (`CheckWalkability` 0x3100B214).
* **Rust `ai::geodata` gaps:**
  * The areas reader rejects v > 21, so all 10.x areas fail; 1.2 is OK.
  * Link byte +42 is compared `== 1` instead of testing bit 0, and +43 is misused.
  * The nav-modifier header is misread (`altered` is always true).
  * There is no cross-sector graph and no triangle height interpolation.
  * Pass radius, cost, flags and nav type are ignored.
  * There is no physics walkability.
  * The A* step cost uses distance-to-goal (a C# quirk kept on purpose).

### 2.6 Ray and sweep masks used by gameplay code (x2game)

| objtypes | meaning | users |
|---|---|---|
| 0x101 | static + terrain | vehicle slope `sub_39248100`, wheels `sub_39283780`, transfer path `sub_3973EF70` |
| 0x107 | static + sleeping + rigid + terrain | unmount check `sub_3972C690`, building `sub_398351E0` |
| 0x11F | all + terrain | tornado `sub_3920FE10`, surface id `sub_394B6660` |
| 0x31F | all + terrain + water | WZRayCasting `sub_3936F340` |
| 0x301 | static + terrain + water | glide start `sub_392E36B0` |

There is also an async ray queue (I3DEngine vt+2496) for foot height, swim bottom and
ballistic projectiles. `PrimitiveWorldIntersection` (37 sites) is used for capsule and box
sweeps: blink, resurrect/teleport positions, mount/unmount, and building.

### 2.7 Gap summary vs. the Rust server
Rust already loads: heightmaps (`npcs::heightmap`), water bodies from `object.dat`
(`ai::physics::{objects_file,water}`), and the `.bai` navmesh (`ai::geodata`).

Missing:
* **static brush colliders** (`brush.dat` + `statobjs.dat` + CGF `MeshPhysicsData`);
* **vegetation colliders** (`vegetation.dat` + `vegetation.xml` + CGF);
* **doodad/house colliders** from their models;
* terrain surface types (`cover.ctc`, `surface.xml`);
* visareas and indoor geometry;
* level entities: areas and static gimmicks from `entities.xml`;
* terrain holes.

The 1.2 client pak (the data the Rust server targets today) has the same per-cell file set,
so the loaders apply to both. Record versions must be checked per client (e.g. `.bai` areas
v21 vs v22).

Memory estimate for full static collision of main_world: about 170 MB of physics blobs
(less if loaded per cell on demand), plus 244 k brush and 2.07 M physical vegetation
transforms (about 100 MB as compact instances).

---

## 3. dedicatedserver.exe as a reference oracle

**Command line.** `Bin64\dedicatedserver.exe -devmode +zone <zoneName>` with cwd = `Bin64`.
* The default zone is `w_gweonid_forest_1`.
* The exe must be in `Bin64`. From the install root it hangs on a "missing xlcommon.dll"
  loader error.
* `-devmode` loads `x2game-dev_dedicate.dll` without the patcher or launcher.
* The world server address comes from the CVars `world_serveraddr` / `world_serverport`
  (default 1240).
* The DB comes from `db_location`, falling back to `game\db\compact.sqlite3`
  (`StartupDatabaseServices` 0x39A309D0, DDCMS loader `sub_39BCE460`).

**What happened** (run from a copy of `Bin64` under `D:\aa-re\zone\run\`, no world server
listening on 1240):
* It boots CrySystem with `CryRenderNULL`, then network, physics ("Physics initialization",
  `CryPhysics_dedicate`), AI, animation, 3D engine, script, entity system and the seamless
  system. It reaches "Splash: Loading Game Data..." in about 25 s at about 860 MB.
* It then fails in the **DDCMS (game database) load**:
  `sqlite3_prepare_v2: SELECT COUNT(*) FROM cinema_effects ... no such table`, followed by
  "retry in 2 sec..." ten times.
  * The shipped `game/db/compact.sqlite3` is the *client* DB.
  * The zone needs 41 server-only tables that the client DB lacks: `npc_spawners`,
    `npc_spawner_npcs`, `npc_ai_params`, `ai_commands`, `loot_*`, `aggro_links`,
    `indun_*`, `siege_plans`, `world_var_defaults`, `np_skills` and others.
  * `game/db/game.sqlite3` (552 MB) is in the pak but encrypted (no SQLite header).
* A patched copy of the client DB with those tables added empty was built. It could not be
  injected:
  * `+db_location <path>` on the command line makes `LoadLibrary(x2game-dev_dedicate.dll)`
    fail.
  * A loose `Bin64\game\db\compact.sqlite3` is ignored (the pak copy wins).
  * The remaining route is `db_location` in the save-dir `system.cfg`. That file is the
    user's live client config, so it was not edited.
* **Logs.** The zone writes `ArcheAge.log` to the **real** user save dir
  (`Documents\ArcheAge\`, via `XlGetSafeSaveGameDir`), whatever the junction says. It
  backs up the previous log into `LogBackups\ArcheAge\`. Physics, area and gimmick logs use
  `X2LogPrefixed`.
* **Verdict.** It is not usable as an oracle out of the box. It needs:
  * a server DDCMS (decrypt `game.sqlite3`, or synthesize the missing tables and set
    `db_location` in an isolated save dir, e.g. a separate Windows user);
  * a stub world server on 1240 that answers ZWJoin with `WZJoinResponse(reason=0)` and
    then sends WZUnitState / WZRayCasting / WZShipControlChange.

  With those it would be a strong oracle. WZRayCasting (op 122) in particular is a
  ready-made "raycast against the real collision world" RPC that returns WorldPos hits.
* Safety. `game_pak` was reached through a temporary hard link. Its modification time is
  unchanged (2026-07-05 16:46:17) and the link has been removed. The run copies (`Bin64`,
  `db`) were deleted after the runs; `D:\aa-re\zone\run\zone_run1_ArcheAge.log` keeps the
  boot log.

---

## 4. Recommendation: what the Rust world server needs, in priority order

The guiding fact: in 10.x the zone owns all geometry, and the world only consumes events
(fell, move-hack, collision, ray result, area enter/leave, unit movements). AAEmu has no
zone, so each event must be produced in-process.

1. **A world collision query service.** This is a new `systems::ai::collision` module, or an
   extension of `npcs::heightmap`.
   * API: `ground_height(x, y, z_hint)`, `raycast(from, to, mask) -> Hit {pos, normal, surface}`,
     `sweep_capsule(from, to, r, h) -> Hit`, `water_level(x, y)`, `surface_type(x, y)`.
   * Phase 1 backs it with the existing heightmap (terrain) + `ai::physics::water`.
   * Phase 2 adds static brushes: `brush.dat` placements + CGF `MeshPhysicsData` trimeshes /
     boxes, in a per-cell BVH. Use the box/OBB of the physics proxy first and full trimeshes
     later.

   Every item below uses this service, so it comes first.
2. **Fall damage parity** (`skills::on_moved`): keep the client-`fallVel` path and add the
   zone cap `min(fallVel, √(40·Δh))` with Δh from the tracked fall start minus the landing
   ground height, threshold 20 m/s. Cheap; uses #1 phase 1.
3. **Movement verification** (`world::session::move_unit`): the zone's checks:
   * speed and over-velocity;
   * teleport thresholds 150/100/50 m/s over 50/25/10 m;
   * packet rate;
   * z < −1;
   * under-terrain (`terrainZ − z > 0.1`);
   * vehicle warp > 500 m and levitation > 5 s.

   Keep it off by default like `movement_verify_enable`. Uses #1 phase 1.
4. **Skill geometry** (`systems::skills`):
   * LoS and obstacle checks (`check_obstacle`, `check_terrain`);
   * ground-targeted positions;
   * blink, leap and dash end positions (capsule sweep, 0.1 m back-off, 30° step);
   * knockback and impulse end positions (ballistic g = 20 against the ground);
   * water targeting (`target_water`, `target_only_water`).

   Phase 1 against terrain gets most of the value; phase 2 removes blinking through walls.
5. **Ship physics fidelity and collision damage** (`systems::ai::physics`, `vehicles::ship_physics`):
   * Keep the Jitter2 model.
   * Feed `contacts.rs` results into a `ZWUnitCollision`-equivalent: closing speed along the
     normal, max per pair per 500 ms, then damage from the `physics_collision_*_damage_mul`
     and armor columns, gated by `zone_groups.enable_physics_collision_damage`.
   * Add brush colliders (piers, rocks) from #1 phase 2 as static barriers. `barriers.rs`
     already supports static boxes.
6. **Doodad, house and gimmick interactions** (`npcs::doodad`, `housing`, `ai::gimmick`):
   * doodad-collide-unit OBB test (flag 0x20);
   * housing and doodad placement (height range, surface type from `cover.ctc`/`surface.xml`,
     overlap);
   * static gimmicks extracted from level entities;
   * simple rigid-body gimmicks (reuse the Jitter2 subset).
7. **NPC navigation parity** (`systems::ai::geodata`): keep `.bai` A*; add `npc_spawn_on_terrain`
   snapping and the under-terrain target check through #1. Use `zone_server/npc_spawners.g`
   (10.x) as the spawn source if moving off the JSON spawns.
8. **Wheeled vehicles and physical explosions** (lowest priority): either a kinematic
   vehicle clamped to terrain, or a small wheeled-vehicle model; and `SimulateExplosion`
   impulses for gimmicks and breakables only.

Out of scope for parity: ropes and ragdolls (client visuals), aux physics (hair, cloth),
and ocean wave simulation (`e_water_ocean_*`). The world only needs the static ocean level
(100) and the water bodies.
