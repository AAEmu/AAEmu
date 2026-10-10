# CryPhysics (ArcheAge zone server physics) — engine map

This is the research map for porting the physics engine of the ArcheAge **zone server** into Rust
(`aaemu-physics`). It replaces the Jitter2-subset ship physics in
`crates/aaemu-game/src/systems/ai/physics/`. What the zone does *with* physics is covered by the
sibling document [`ZONE-PHYSICS-USAGE.md`](ZONE-PHYSICS-USAGE.md). This document covers the
engine itself.

**Licensing.** Everything here was derived from the shipped binaries listed below. The published
CRYENGINE source was not used, quoted or consulted. Names in quotes or in `CClass` form come from
the binary's own RTTI and log/profiler strings. Names marked *(inferred)* describe observed
behaviour. Port from this document and the decompiles, never from the CRYENGINE source.

Detailed per-area notes, which are long and address-dense, are in [`cryphysics/`](cryphysics/):

| file | content |
|---|---|
| [`cryphysics/world.md`](cryphysics/world.md) | all 105 `CPhysicalWorld` vtable slots, the physics-vars block, entity grid, TimeStep pipeline, events, ray/primitive queries, `CPhysArea` |
| [`cryphysics/entities.md`](cryphysics/entities.md) | entity vtable roles per class, every params/status/action id with its field layout, member layouts, Step outlines |
| [`cryphysics/geometry.md`](cryphysics/geometry.md) | geometry ids and data layouts, the 60/62-slot geometry vtable, BV trees, intersection/unprojection tables, buoyancy volume math |
| [`cryphysics/dynamics.md`](cryphysics/dynamics.md) | rigid body state, integration, contact solver, buoyancy/water forces, wheeled vehicles, determinism |
| [`cryphysics/vtable_match.txt`](cryphysics/vtable_match.txt) | the 1.2 ↔ 10.x vtable correspondence |
| [`cryphysics/harness/`](cryphysics/harness/) | the oracle harness (`oracle_box.c`) and its reference outputs |

Addresses are virtual addresses at the DLL's preferred image base: **x64 10.x
`cryphysics_dedicate.dll` = 0x35000000**, **x86 1.2 `cryphysics.dll` = 0x34FF0000**. They are written
`x64 / x86`. An x86 address marked *(tent.)* comes from a weak cross-version match.

---

## 1. Binaries and RE artefacts

| | 10.x zone server | 1.2 client |
|---|---|---|
| file | `…\ArcheAge Returns 10.0.2.13 …\Bin64\cryphysics_dedicate.dll` (also the client `cryphysics.dll` in the same folder) | `D:\AA1.2\bin32\cryphysics.dll` |
| arch / base | x64 / 0x35000000 | x86 / 0x34FF0000 |
| PDB path string | `Z:\code\sgsj\shanggushijisdk\shanggushiji\ArcheAge\x2\trunk\Release_Dedicate64\CryPhysics_dedicate.pdb` | `C:\work\na_hotfix\trunk\dev32\CryPhysics.pdb` |
| PE timestamp | 2026-06 | 2014-10 |
| RTTI | yes (85 type descriptors, 77 vtables) | **none** (built without RTTI) |
| protection | none | **VMProtect-packed** (`.text` entropy 7.98, unpacker in `.reloc`; a few exports are mutated thunks) |
| functions (IDA) | 1,911 | 2,852 |
| exports | `CreatePhysicalWorld`, `CryModuleGetMemoryInfo` | same two |
| imports | `xlcommon.dll` (CryMalloc/CryFree/CryRealloc, X2Log, CryRandom*, CryGetCurrentThreadId), `tbbmalloc_proxy.dll` (marker symbol only), MSVCP100, MSVCR100, KERNEL32 | same families (x86 decorations) |

All RE outputs are under `D:\aa-re\cryphysics\` (not in the repo):

* `x64_10x\` and `x86_12\`: `cryphysics.c` (full Hex-Rays decompile; functions start at
  `// ===== <addr> <name>`), `funcs.csv`, `func_features.jsonl` (per-function strings, calls,
  float constants), `strings.txt`, `exports.txt`, `imports.txt`. x64 also has `rtti.json`,
  `rtti_vtables.txt` (classes, bases, vtables with method addresses) and `vtables_named.txt`.
  x86 has `vtables_raw.json` (vtables found by scanning) and `iat_map.txt` (the recovered import table).
* `x64_10x_client\rtti*`: RTTI of the 10.x *client* `cryphysics.dll`. Its 77 vtables have exactly the
  same slot counts as the dedicated-server build.
* `compare\func_match.csv` (x64 ↔ x86 function match, 1,122 pairs), `compare\vtable_slots.txt`
  (slot-by-slot alignment of all 55 matched vtables) and `compare\vtable_match.txt`.
* `scripts\`: `ida_export.py` (headless export, RTTI-driven renaming, string-driven renaming),
  `rtti_scan.py` (MSVC RTTI/COL/vtable recovery from the PE), `x86_vtables.py`, `match.py`, `fn.py`
  (print a decompiled function: `python fn.py x64 3508bd10`).
* `unpack_x86\`: the 1.2 unpacker (see below), `harness\`: the oracle.

**Unpacking the 1.2 DLL.** `LoadLibrary` on the packed DLL fails with error 1114 (its `DllMain`
returns FALSE outside the game client), but the VMProtect stub has already decrypted the image by then.
`unpack_x86\dump2.c` (32-bit) registers an `LdrRegisterDllNotification` callback, loads the DLL with the
1.2 runtime DLLs next to it, and copies the mapped image in the *unload* notification. It also writes
an address→export map of every loaded module. The dump is rebuilt into a PE with
`raw == virtual` sections and the bogus packer import directory removed (`work_x86\cryphysics_unpacked.dll`).
The import table is recovered by matching IAT slots against the export map (94 imports). After this the
1.2 `.text` entropy is 6.30, against 6.25 for the x64 build, and the decompile is clean.

**Headless IDA.** `idat.exe -A -S<script> -L<log> <dll>`, with output paths passed in environment
variables (`AARE_OUT`, `AARE_RTTI`, `AARE_IAT`). See `scripts\run_x64.sh` / `run_x86.sh`. A full export
of either DLL takes about 6 minutes.

---

## 2. Are 1.2 and 10.x the same engine generation? — **Yes**

Both are the same CryEngine-3-era CryPhysics. Both have the `ICryUnknown` / `CEngineModule_CryPhysics`
module factory, the same class hierarchy, the same vtable shapes, the same collision tables and the
same default tuning. 10.x is a lightly modified rebuild of the 1.2 code, not a newer physics engine.

| measure | result |
|---|---|
| exports | identical (2) |
| class set | 55 x64 vtables have an x86 counterpart in the same `.rdata` order: 52 found by automatic alignment, plus `CPhysicalWorld`, `CBoxGeom` and `CRayBV` paired by hand. x64-only: `CPhysicalStub` (35 slots), the `CTypeInfo` / `CStructInfo` / `CEnumInfo` reflection family (13-slot vtables, used for serialisation), `CSystemEventListner_Physics` and the stdext exceptions. |
| vtable slot counts | identical for 48 of the 55 pairs. The 7 that differ: `CPhysicalWorld` 105 vs 104 (x64 adds slots 44, 63 and 85; x86 has an extra reset at slot 6 and an unidentified slot 49), `IPhysicalWorld` 104 vs 103, `CLivingEntity` / `CParticleEntity` 77 vs 78, `CRayBV` 26 vs 22, `CMemStream` 2 vs 3, `ISystemEventListener` 2 vs 3. Every geometry (60/62), BV-tree (26), rigid (83), articulated (83), wheeled (84), rope/soft (77), area (38) and geom-manager (27) vtable has the same slot count and slot order. |
| function match | 1,122 of 1,911 x64 functions matched (632 of the 914 functions over 200 bytes). The vtable alignment gives sim ≥ 3 on almost every slot. |
| collision tables | the OVERLAP 8×8, INTERSECT 8×8 and UNPROJECT 2×8×8 tables have the same shape and order in both builds (x64 0x3520A3E0 / 0x35209D50 / 0x35209FA0 ↔ x86 0x35204808 / 0x352044A8 / 0x352045D8). |
| world defaults | the 90 non-zero defaults of the physics-vars block written by the world constructor are the **same values in the same order**. The only differences are one extra x86 field (−1) and one extra x64 float (0.001); the layout shifts by 8 bytes after +116. |
| params ids | the same `pe_params` / `pe_status` / `pe_action` ids. 10.x adds soft-body params id 117 (ignore-part list). |
| strings | 283 shared. The x86 build keeps profiler labels that the x64 build strips (`CRigidEntity::Step`, `CRigidEntity::ApplyBuoyancy`, `InvokeContactSolver`, `LCPCG`, `LCPMC`, …). These were used to name x64 functions through the match. |

**Practical consequence.** A port of the 10.x engine is also a port of the 1.2 engine. The wire-visible
1.2 server behaviour (the Rust server targets the 1.2 client) does not depend on any of the small
interface differences above. The x64 10.x `cryphysics_dedicate.dll` is the reference, because it
has RTTI and because it is the build that can be run as an oracle (§9).

---

## 3. Module entry and global environment

| item | x64 / x86 | notes |
|---|---|---|
| `CreatePhysicalWorld(ISystem*)` | 0x351DFEF0 / 0x351F4800 (mutated thunk) | stores `gEnv = pSystem->vf6()` in the global at **0x3537C6D8**, registers a system-event listener, then `new CPhysicalWorld` (0x5838 bytes). Works with `pSystem == NULL`: gEnv stays NULL, and the world is fully usable until the first `TimeStep` (§9). |
| `CPhysicalWorld` ctor | 0x350827B0 / 0x350889B0 | sets 3 vtables (+0 `IPhysicalWorld`, +8 `IPhysUtils`, +16 `CGeomManager`), physics-vars defaults, profiler labels ("Rigid bodies", "Wheeled vehicles", …), then reset 0x3507DE80 / 0x35082920 |
| gEnv uses | — | `+0` ISystem (slot 47 config spec, 82 module registry, 84 thread-task manager), `+112` (slot 20 getter), `+152` log, `+160` (slot 19, a void call made in TimeStep and on pool growth), bytes `+289` / `+290` flags |
| worker threads | — | `SPhysTask` (IThreadTask) through `gEnv->pSystem->GetThreadTaskManager` (slot 84). Thread count = vars `+504` (default 1, capped at 4). With 1 thread nothing is spawned. |
| memory | — | `operator new/delete` → xlcommon `CryMalloc/CryFree`. Allocation is pool-based for entities (grows by 0x10000, "increasing physical entity pools %d => %d") |

---

## 4. The world (`CPhysicalWorld`, `IPhysicalWorld`)

Full slot table: [`cryphysics/world.md` §1](cryphysics/world.md). The slots needed to drive the engine:

| slot (x64) | x64 / x86 | role |
|---|---|---|
| 0 | 0x350985D0 | destructor |
| 9 / 10 | 0x35096690 / 0x350966A0 | GetGeomManager (this+16) / GetPhysUtils (this+8) |
| 11 | 0x3507E5A0 / 0x35089810 | **SetupEntityGrid**(axisz, Vec3* origin, nx, ny, stepx, stepy, log2PODscale, bCyclic) |
| 13 / 14 | 0x35078F30 / 0x35076F30 | physics-on-demand grid register / clear *(inferred)* |
| 20 | 0x350966B0 / 0x35089160 | **GetPhysVars** → this+104 |
| 21 | 0x35082F00 / 0x3509E020 | **CreatePhysicalEntity**(type, float lifeTime, pe_params*, foreignData, iForeignData, id, host placeholder, flags) |
| 22 | 0x350966C0 | CreatePhysicalEntity without lifetime → 21 |
| 24 | 0x35083830 / 0x3509E7D0 | DestroyPhysicalEntity |
| 32 | 0x3508BD10 / 0x3509F6A0 | **TimeStep**(float dt, int flags) |
| 34–39 | 0x35096710… | get/set the current time (+1664 float, +1688 int ticks, quantised by vars `+220` = 1e-4) |
| 40 | 0x350989B0 / 0x3509DF80 | GetEntitiesInBox (core 0x35084680) |
| 42 | 0x351AA730 / 0x351C2B00 | **RayWorldIntersection**(SRWIParams*, tag, iCaller); hit record 80 B |
| 43 | 0x351A9FC0 / 0x351C3F70 | process queued ray/primitive queries |
| 50 | 0x3508EDF0 / 0x35097090 | SimulateExplosion |
| 59–62 | 0x35086DB0… / 0x3508E860… | CollideEntityWithBeam, CollideEntityWithPrimitive, RayTraceEntity, CollideEntityToEntity |
| 64 / 65 | 0x350964F0 / 0x35093100 | **PrimitiveWorldIntersection** (argument wrapper / core) |
| 75 / 76 / 77 | 0x35077AE0 / 0x35077BC0 / 0x35094FA0 | AddEventClient / RemoveEventClient *(inferred)* / PumpLoggedEvents |
| 80–84 | 0x3504A1A0… | areas: AddGlobalArea, AddArea (spline tube / geometry / 2D polygon prism), RemoveArea |
| 87 | 0x3504E000 / 0x35052B90 | **CheckAreas**(pt, gravity*, buoyancy[] out, nMax, iMedium, vel, entity, r, iCaller, areas[] out, nMax) |
| 103 / 104 | 0x35097000 / 0x35096EE0 | SignalEvent / legacy RayWorldIntersection argument wrapper |

**Physics vars** (`this+104 … +632`, returned by slot 20). Defaults decoded in
[`world.md` §2](cryphysics/world.md). Confirmed meanings include: gravity `+284` = (0, 0, −9.8),
time granularity `+220` = 1e-4, max world step `+224` = 0.2, max substeps `+276` = 5, thread count `+504` = 1,
and the solver block `+104…+264` used by the contact solver: iteration floor `+112` = 6000, PGS
tolerances `+124` = 0.005 and `+176` = 0.02, penetration slop `+240` = 0.01, unprojection velocity gain
`+260` = 10, max unprojection velocity `+264` = 2.5 ([`dynamics.md` §3.5](cryphysics/dynamics.md)).

**Entity grid** (`+856…+996`). The grid is 2D over the two axes other than `axisz`. Each entity
gets one 32-byte link record per cell its bounding box covers (up to 0x100000 records). Entities are
(re)registered by 0x35080A00. **Entities whose box lies below the grid origin's z are not found by the
broad phase**: in the oracle, a ground box whose top was at z ≤ −1 with the grid origin at z = 0 was
fallen through. Set the origin z below the lowest geometry. A separate physics-on-demand grid sits at
`+1016…+1064`.

**TimeStep pipeline** (0x3508BD10, [`world.md` §4](cryphysics/world.md)):
1. (Re)create worker tasks if the thread count changed. Execute the double-buffered deferred command
   queue: `SetParams`, `Action` and `AddGeometry` calls made while the world was busy were copied there.
2. Clamp and quantise dt (granularity 1e-4, max 0.2). Process the deformation/breakage queue.
3. **Rigid bodies.** Build contact groups (islands). Run up to max-substeps iterations. Per island
   (0x3507F9B0): every entity `Step` (slot 34) → `RegisterContacts` (slot 40) → **InvokeContactSolver**
   (0x3509E850) → `Update` (slot 41, damping and sleep check). Islands may run on worker threads (job modes 0/1).
4. Living entities (job mode 3), then independent entities (particles, ropes, soft; mode 4), then
   post-step (mode 2).
5. Cleanup: sleep/static timeouts, freeing deleted entities after 30 s, ageing on-demand cells, events.

**Events** ([`world.md` §5](cryphysics/world.md)). 15 event types with fixed sizes (0:56, 1:160,
2:56, 3:64, 4:80, 5:136, 6:200, 7:64, 8:128, 9:72, 10:88, 11:176, 12:64, 13:48, 14:48). Each type has an
"immediate" and a "logged" client list, sorted by priority. Logged events go to an 8 KB arena and are
delivered by PumpLoggedEvents. Event 11 is the area callback (game-supplied gravity and buoyancy) and
event 12 is area changed.

**Areas** (`CPhysArea`, 0x228 bytes, GetType = 9, vtable 0x351EF598 / 0x351FB658, ctor 0x35042150 /
0x3504BA10). Area kinds are global, spline tube, geometry, 2D polygon prism and ellipsoid/size.
Areas carry a gravity vector, an 80-byte **buoyancy record** at `+388` (params id 12: water density,
resistance, flow, water-plane normal and origin, medium index 0–15) and a callback flag. There are 16
per-medium lists, plus a list for "big" areas. Layout and the CheckAreas algorithm:
[`world.md` §7](cryphysics/world.md).

---

## 5. Entities

| type | class | size | ctor x64 / x86 | vtable x64 / x86 | step (slot 34) x64 |
|---|---|---|---|---|---|
| 1 | `CPhysicalEntity` ("static") | 0x1C0 | 0x35071D30 | 0x351EFE38 / 0x351FB7C0 (77) | — |
| 2 | `CRigidEntity` | 0x4B0 | 0x350ACBC0 | 0x351F14C8 / 0x351FCD08 (83) | 0x350C9DF0 / 0x350D84A0 |
| 3 | `CWheeledVehicleEntity` : rigid | 0x2BF8 | 0x35104900 | 0x351F2388 / 0x351FD9A0 (84) | rigid Step + slots 78/79 |
| 4 | `CLivingEntity` | 0x468 | 0x35025530 | 0x351EE1D8 / 0x351FAA60 (77/78) | 0x3502DED0 / 0x35035630 |
| 5 | `CParticleEntity` | 0x2A8 | 0x35039D40 / 0x3503F880 | 0x351EEE88 / 0x351FB220 (77/78) | slot 25 0x3503C210 |
| 6 | `CArticulatedEntity` : rigid | 0x618 | 0x35001380 | 0x351ED638 / 0x351FA2D8 (83) | 0x3501A150 / 0x35022C50 |
| 7 | `CRopeEntity` | 0x400 | 0x350CE210 / 0x350DBCA0 | 0x351F1A48 / 0x351FD2C0 (77) | ("CRopeEntity::Step2") |
| 8 | `CSoftEntity` | 0x360 | 0x350FC660 / 0x3510B230 | 0x351F1DE8 / 0x351FD5B8 (77) | ("CSoftEntity::Step") |
| (9) | `CPhysArea` | 0x228 | 0x35042150 | 0x351EF598 (38) | — |

Hierarchy (RTTI): `IPhysicalEntity` (32 slots) ← `CPhysicalPlaceholder` (35) ← `CPhysicalEntity` (77) ←
{rigid ← {articulated, wheeled}, living, particle, rope, soft}. `CPhysicalStub` (x64 only) and
`CPhysArea` derive from the placeholder.

**Common entity slots:** 0 dtor, 1 GetType, 2/3 AddRef/Release, 6 GetId (low 23 bits of `+0x44`;
sim class is the top byte), **7 SetParams(params, bThreadSafe)**, **8 GetParams**, **9 GetStatus**,
**10 Action(action, bThreadSafe)**, **11 AddGeometry(phys_geometry*, pe_geomparams*, id, bThreadSafe)**,
12 RemoveGeometry, 13/14 foreign data, 24 SyncFromNet (living), 26 StartStep, 34 **Step(dt)**,
37 GetMaxTimeStep, 40 RegisterContacts, 41 Update. All slots: [`entities.md` §2b](cryphysics/entities.md).

**Base layout** (x64): world bbox `+8…+28`, world pointer `+120`, flags `+0x5C`, scale `+0xC4`,
parts array `+0x118` (count `+0x120`, stride 0x88: pos, q, scale, phys_geometry* (and the proxy),
id `+72`, mass `+76`, flags `+80`, flagsCollider `+84`, minContactDist `+92`, …), **pos `+0x198`**,
**quat `+0x1A4` (x, y, z, w)**. The rigid body is embedded at `+744` (0xD4 bytes: mass, 1/mass, inertia,
CoM, momentum P/L, v, w, …).

**The parameter interface.** Every struct starts with `int type`. Optional fields use **unused
sentinels**:
* floats: `0xFFBFFFFF`, tested with `(bits & 0xFFA00000) == 0xFFA00000`. For a vec3 only `.x` is
  tested; for a quaternion only `.w`.
* ints: `0x80000000`.
* pointers: `-1`.

`SetParams`, `Action` and `AddGeometry` called while the world is stepping are **copied into the world
command queue**. Exact struct sizes come from static tables built at init (`sub_351E5900`: params sizes
at 0x3537C640, actions at 0x3537C5F0, geomparams at 0x3537C5D8, plus descriptors for embedded pointer
arrays).

| params id | size | name | | action id | size | name |
|---|---|---|---|---|---|---|
| 0 | 72 | `pe_params_pos` | | 1 | 24 | `action_move` (living) |
| 1 | 64 | `player_dimensions` | | 2 | 80 | `action_impulse` |
| 2 | 144 | car *(inf.)* | | 3 | 36 | drive *(inf.)* |
| 3 | 168 | `pe_params_particle` | | 4 | 8 | reset *(inf.)* |
| 4 | 104 | player dynamics *(inf.)* | | 5 / 6 | 128 / 48 | add / update constraint *(inf.)* |
| 5 | 208 | `params_joint` | | 7 | 88 | register collision event *(inf.)* |
| 6 | 160 | `pe_params_part` | | 8 | 12 | awake |
| 8 | 176 | articulated *(inf.)* | | 9 | 4 | remove all parts *(inf.)* |
| 9 / 11 | 24 / 32 | outer entity / foreign data *(inf.)* | | 10 | 40 | set velocity *(inf.)* |
| 10 | 84 | simulation *(inf.)* | | 11 / 12 | 48 / 48 | soft attach / target vertices *(inf.)* |
| **12** | 80 | **buoyancy** *(inf.)* | | 13, 15, 16, 17 | 12, 12, 72, 104 | part-matrix / detach / move parts / part poses *(inf.)* |
| 13 | 248 | rope *(inf.)* | | 18 | 88 | net sync ("CRigidEntity::Action() sync") |
| 14 | 28 | `pe_params_bbox` | | | | |
| 15 / 16 / 17 | 16 / 80 / 100 | flags / wheel / soft *(inf.)* | | | | |
| 20, 21, 23–26 | | ground plane, structural joint, timeout, skeleton, … *(inf.)* | | | | |

Status ids: 1 pos (output layout: pos `+24`, bbox `+36` / `+48` relative to pos, q `+60`, scale `+76`,
simclass `+80`), 2 living, 4 vehicle, 5 wheel, 6 joint, 7 awake, 8 dynamics, 9 collisions,
10 id, 12 nparts, 13 contains point, 14 rope, 15 vehicle abilities, 17 soft vertices, 19 contact patch,
21 check stance, 24 extent, 25 random position, 26 constraint. `pe_geomparams` (type 0, 112 B):
density `+4`, mass `+8`, pos `+12`, q `+24`, scale `+40`, Matrix34* `+48`, Matrix33* `+56`, surface_idx
`+64`, flags `+68` (bit 0x10000 = part takes part in buoyancy), flagsCollider `+72`, minContactDist `+76`,
idmatBreakable `+80`, lattice* `+88`, matMapping* `+96`, nMats `+104`, **bRecalcBBox `+108`** (the entity
bbox and grid cells are only updated when this is set). Full layouts:
[`entities.md` §1–§2](cryphysics/entities.md).

---

## 6. Geometry and collision detection

| id | class | data | notes |
|---|---|---|---|
| 0 | `CBoxGeom` (0xC8) | `primitives::box` 64 B: Basis 3×3, bOriented, center, half-size | SingleBox BV |
| 1 | `CTriMesh` | verts, indices, normals, materials, neighbour topology, BV tree | AABB or OBB tree |
| 2 | `CHeightfield` : trimesh (0x320) | grid origin/step/size, height scale, `getHeightCallback`, `getSurfTypeCallback` | `CHeightfieldBV` hands out triangles |
| 3 | `CRayGeom` (0x70) | origin, dir | `CRayBV` |
| 4 | `CSphereGeom` (0x98) | center, r | |
| 5 | `CCylinderGeom` (0xB0) | center, axis, r, hh | |
| 6 | `CCapsuleGeom` : cylinder (0xB0) | center, axis, r, hh | |
| 7 | `CVoxelGeom` : trimesh | voxel grid | `CVoxelBV` |

`CGeomManager` (27 slots, embedded at world+16): slot 7 **CreatePrimitive(type, prim*)** 0x3514B9F0,
slot 9 **RegisterGeometry(IGeometry*, defSurfaceIdx, matMapping*, nMats) → phys_geometry*** 0x3514AE40,
plus CreateMesh, heightfield and lattice factories. `phys_geometry` is 88 bytes: geometry `+0`,
inertia `+8`, q `+20`, origin `+36`, volume `+48`, refcount `+52`, surface `+56`, matMapping `+64`,
nMats `+72`.

The geometry vtable (`IGeometry` 46 → `CGeometry` 60 → `CTriMesh` 62 slots) has the same order in
both builds. It provides GetType, GetBBox, Intersect, FindClosestPoint, CalcPhysicalProperties,
PointInsideStatus, **CalculateBuoyancy (slot 13)**, **CalculateMediumResistance (slot 14)**, an
explosion pressure function (slot 12), GetPrimitive (17), SetData (30) and RegisterIntersection (54).
Per-class overrides: [`geometry.md` §2](cryphysics/geometry.md). BV trees (26 slots): OBB, AABB
(12-byte nodes, 7-bit quantised), SingleBox, Ray, Heightfield and Voxel ([`geometry.md` §3](cryphysics/geometry.md)).

**Intersection dispatch.** Three static `.rdata` tables indexed by geometry id. There is no init code.

| table | x64 / x86 | shape |
|---|---|---|
| OVERLAP (cheap reject) | 0x3520A3E0 / 0x35204808 | [8][8] |
| INTERSECT (contact generation) | 0x35209D50 / 0x352044A8 | [8][8] |
| UNPROJECT (mode 0 linear, 1 rotational) | 0x35209FA0 / 0x352045D8 | [2][8][8] |

INTERSECT has 20 unique pair functions: box–box 0x3516AE00, box–ray, box–sphere, box–cylinder, box–capsule,
tri–box 0x35162AA0, tri–tri 0x35159410, tri–sphere, tri–cylinder (14.8 KB), tri–capsule, ray–tri,
sphere–ray, sphere–sphere, cylinder–ray, cylinder–sphere, cylinder–cylinder, cylinder–capsule,
capsule–ray, capsule–sphere and capsule–capsule. The mirrored cells are swap wrappers with per-pair
normal and feature fix-ups. **Heightfield and voxel never appear as primitives**: their BVs emit
triangles, so terrain is collided as a triangle soup. Full matrices and the `geom_contact` (136 B)
layout: [`geometry.md` §4](cryphysics/geometry.md). Unprojection (continuous collision, linear and
rotational sweeps): [`geometry.md` §5](cryphysics/geometry.md).

---

## 7. Dynamics, contact solver and buoyancy

Detail: [`cryphysics/dynamics.md`](cryphysics/dynamics.md).

* **Integration** (body step 0x3509E180): positions and orientations are integrated from momentum each
  substep, with exact rotation and renormalisation. There is no gyroscopic term. Gravity and external
  impulses are added after contacts are registered. Damping is a multiply by `max(0, 1 − d·dt)` after
  the solver. Rigid max step defaults to 0.02 (GetMaxTimeStep 0x350BCAA0). The sleep energy default is 0.0049.
* **Contact solver** (`InvokeContactSolver` 0x3509E850 / 0x350A62B0; x86 labels "PreCG", "LCPCG",
  "LCPCG-unproj", "LCPMC"): projected Gauss-Seidel on velocity-level impulses with a cumulative
  friction cone. Penetration is fixed by a velocity bias `vreq = min(2.5, 10·(depth − 0.01))`. A CG
  fast path and two heavier stages handle hard islands. Contacts (176-byte records) are not
  warm-started. Bodies are sorted by memory address, so a port must sort by a stable id instead.
* **Buoyancy** (`CRigidEntity::ApplyBuoyancy` 0x350C0E50 / 0x350D1D80). For each buoyancy record from
  CheckAreas and each part flagged 0x10000, the submerged volume and centre come from geometry slot 13.
  Meshes are clipped to tetrahedra; a box is turned into a 12-triangle mesh. The impulse is
  `J = −g·(ρ_area·kρ_entity)·V_sub·dt` at the buoyancy centre, with **g hard-coded to (0, 0, −9.81)**.
  Water resistance is a drag *linear* in normal velocity on submerged faces moving into the water
  (slot 14). Damping is blended by submerged fraction. The entity factor `kρ = density/1000` defaults to
  **0.001**, so an entity floats only after it is given params id 12 itself. Copy-as-read oddities
  (sphere buoyancy centre, unrotated trimesh centre, flow drag without dt) are listed in
  [`dynamics.md` §4](cryphysics/dynamics.md) and [`geometry.md` §6.6](cryphysics/geometry.md).
* **Wheeled vehicles** reuse the rigid Step. Suspension and drive are applied as impulses (slots 78/79);
  only lateral tyre friction goes through the solver.
* **Determinism**: single-precision SSE throughout, with min/max idioms that must be copied literally.
  No random numbers in dynamics. Islands run in parallel, but with one thread (the default) the oracle
  is bit-identical run to run.

---

## 8. Address-range module map (x64, from the linker's object order)

MSVC lays functions out by object file, so the class names recovered from RTTI and strings give
contiguous ranges. The sizes below are used for the porting plan.

| module | x64 range | funcs | bytes |
|---|---|---|---|
| articulated entity | 35001000–35020960 | 67 | 128 K |
| living entity | 35024D30–35037620 | 36 | 75 K |
| particle entity | 35039850–350411C0 | 29 | 31 K |
| areas (`CPhysArea` + world area API) | 350411C0–35052980 | 66 | 71 K |
| physical entity (base) | 35052980–35076090 | 88 | 144 K |
| world core (grid, timestep, events, queries, tasks) | 35076090–350989C0 | 214 | 139 K |
| contact solver | 350989C0–350A9C20 | 28 | 70 K |
| rigid entity (incl. buoyancy) | 350A9C20–350CDD80 | 96 | 146 K |
| rope / soft | 350CDD80–350FE150 | 90 | 196 K |
| wheeled vehicle | 35104700–3510C950 | 28 | 49 K |
| mesh utilities (boolean subtract, packing) | 3510C950–35136410 | 80 | 154 K |
| AABB / OBB / SingleBox / Ray / HF / Voxel BV trees | (several) | ≈ 90 | ≈ 60 K |
| box / sphere / cylinder / capsule / ray geoms | (several) | ≈ 85 | ≈ 62 K |
| `CGeometry` base + geom manager + helpers | 35146F40–35153F60 | 46 | 50 K |
| primitive intersection checks (INTERSECT/OVERLAP pairs) | 35156E20–35191EA0 | 96 | 240 K |
| rotational/linear unprojection (UNPROJECT pairs) | 3519A5C0–351A9330 | 13 | 61 K |
| ray world intersection | 351A9330–351ADF00 | 16 | 19 K |
| trimesh / heightfield / voxel | (several) | ≈ 100 | ≈ 150 K |
| tetra lattice / breakable 2D grid | 351B09D0–351BDCC0 | 26 | 54 K |
| inline stubs, module, type-info, CRT | — | ≈ 600 | ≈ 80 K |
| **total** | | **1,911** | **1.99 MB** |

---

## 9. Oracle: running the real engine from a test harness — **works**

`cryphysics_dedicate.dll` can be loaded standalone and driven bit-for-bit.
Source: [`cryphysics/harness/oracle_box.c`](cryphysics/harness/oracle_box.c), built with MSVC 2022 x64 (`build.bat`).

What it needs:
1. **Runtime DLLs** in one directory: `cryphysics_dedicate.dll`, `MSVCR100.dll`, `MSVCP100.dll`,
   `tbb.dll`, `xldiag.dll` and **`xlcommon_orig.dll` renamed to `xlcommon.dll`**. The `xlcommon.dll` in
   the r575 Bin64 is a third-party forwarding proxy and fails to resolve outside the game folder.
   `tbbmalloc_proxy.dll` is replaced by a stub that exports `__TBB_malloc_proxy`
   ([`tbbstub.c`](cryphysics/harness/tbbstub.c)), because the real one needs a manifest-bound MSVCR90.
   The runtime folder on the dev box is `D:\aa-re\cryphysics\harness\rt\`.
2. `CreatePhysicalWorld(NULL)`: no ISystem is needed to create the world.
3. **A fake gEnv**: write a pointer to a zeroed block into the DLL global at image+0x37C6D8. The block's
   interface pointers at `+0`, `+112`, `+152` and `+160` point at a "null object" whose virtuals all
   return the object itself. `TimeStep` crashes on a NULL gEnv (`gEnv+0xA0`, slot 19).
4. Calls through the vtables, using the slots and layouts in this document: SetupEntityGrid (11),
   GetGeomManager (9) → CreatePrimitive (7) / RegisterGeometry (9), CreatePhysicalEntity (21),
   AddGeometry (11, with `bRecalcBBox = 1`), SetParams (7), GetStatus (9, `pe_status_pos`), TimeStep (32).

Results (one worker thread, dt = 1/30):
* A 1 m rigid box of 10 kg dropped from z = 5 onto a static box ground lands, settles at z = 0.480746
  (contact slop ≈ 0.019) and goes to sleep. Reference output: `expected_box_on_ground.txt`.
* With a global area carrying a water plane at z = 2 (params 12 on the area *and* on the body, part
  flag 0x10000), a 300 kg 1 m³ box floats at z = 2.214693 (submerged depth 0.285 m), and 700 kg settles
  around 1.8. 1500 kg sinks to the ground. Reference output: `expected_box_float_300kg.txt`.
* Repeated runs are **bit-identical**. Outputs print every float with its raw bits, so a Rust port can
  diff against them exactly.

**`physoracle` (P0/PT).** The harness grew into a scenario-driven tool,
`crates/aaemu-physics/oracle/physoracle.c` (build: `oracle/build.bat`, output
`D:/aaemu-target/physics/oracle/`). It reads a scenario (grid, vars, geometries incl. heightfield
terrain, entities, parts, global area, raw-field SetParams/Action/GetStatus, steps, rays, memory
peeks) and writes a bit-exact trace; `--math` calls internal routines of the DLL (quat↔matrix,
quaternion rotation, body UpdateState/Step) and the MSVCR100 math functions on generated inputs.
Golden traces live in `crates/aaemu-physics/tests/golden/` and are re-verified against the DLL
whenever `AAEMU_PHYS_ORACLE` is set. Scenario language, capabilities and limits: the crate README.

Engine facts found while building it:
* **RayWorldIntersection** returns 0 until the world's temp entity pools (world+792) exist; they are
  allocated at the first `TimeStep` with an ordinary entity (terrain entities alone do not count).
* The RWI flags' high word is **geomFlagsAll** (0 → 0x8000, +1 with flag 0x40); geomFlagsAny is 0xFDFF
  unless flag 0x400 (then All = 0, Any = the high-word value). Parts are only tested when
  `(partFlags & All) == All && (partFlags & Any) != 0`, so default rays need part flag **0x8000**.
* **ArcheAge addition "DrawRayOnGrid"**: unless `gEnv` byte +289 is set, the grid ray walk
  (the grid ray walk 0x351AD6A0 and the same check in 0x35103EA0 and the heightfield ray march 0x35158BA0) rejects rays with |dir.x| or |dir.y| larger than an
  integer config value read through `gEnv+112` → slot 20 → `+248` and logs
  `"DrawRayOnGrid, dir.x(%.1f), dir.y(%.1f)"`. The zone server's value is a game CVar (unknown); the
  oracle uses 1000.
* **Heightfield terrain** must go through world slot 17 `SetHeightfieldData(primitives::heightfield*,
  matMapping, nMats)`, which creates one terrain entity per caller slot (world+1568+8·i, part flags
  0xFDFF, objtype 0x100). A heightfield geometry added to an ordinary static entity gets an empty bbox
  and never collides. `CHeightfield::SetData` ignores the primitive's basis/origin, and a zero
  `typemask` makes it loop forever (typepower is derived from the lowest set bit).
* Entities join the simclass lists (world+664+8·k) only at the first `TimeStep` after creation.
* A default (zero) `action_impulse` still wakes a sleeping rigid body (simclass 1 → 2, awake byte);
  the `impulse` vector itself is **not optional** (no unused test: an unused value puts NaN into P).
* The "infinite" clamp constant is `9.9999999e14` = bits **0x58635FA9** (entities.md had 0x5866D2A9).
* The physics-vars block holds **93 non-zero words** after construction with `gEnv == NULL`
  (the constructor's defaults plus `dedicated` (612) = 1); full table with names:
  `crates/aaemu-physics/src/physvars.rs`.

Multi-threaded stepping (vars `+504` > 1) needs a real thread-task manager behind
`gEnv->pSystem` slot 84 and is not needed for the oracle.

---

## 10. Porting plan for `aaemu-physics`

Principles: port the x64 10.x semantics (identical to 1.2 in every way that matters, §2), keep the
binary's float formulas and evaluation order (min/max idioms, the order of SSE operations), replace
address-ordered sorts with id-ordered ones, and run single-threaded. Test every module against the
oracle (§9). Interface layer: a Rust API shaped after the slot tables (world / entity / geometry) with
typed params enums instead of `type`-tagged C structs. "Unused" sentinels become `Option`.

Work packages, in dependency order. Packages on the same line can run in parallel.

| # | package | source modules (§8) | ≈ size | depends on |
|---|---|---|---|---|
| P0 | **math & primitives**: Vec3/Quat/Matrix33 with the binary's conventions (x,y,z,w quats, min/max idioms), primitive structs, `phys_geometry`, sentinel handling | inline helpers | 15 K | — |
| P1a | **primitive geometries**: box, sphere, cylinder, capsule, ray (GetBBox, volume/inertia, PointInside, GetPrimitive/SetData, CalculateBuoyancy, CalculateMediumResistance) | box/sphere/cyl/cap/ray geoms, `CGeometry` base | 110 K, ~130 funcs | P0 |
| P1b | **BV trees**: SingleBox, AABB, OBB (build and traversal), Ray BV | BV trees | 60 K, ~90 funcs | P0 |
| P1c | **trimesh + heightfield** (mesh data, topology, CalculateBuoyancy on meshes, heightfield BV → triangles) | trimesh, heightfield | 120 K | P0, P1b |
| P2a | **INTERSECT + OVERLAP pair functions** (20 unique + wrappers), `geom_contact`, RegisterIntersection | primitive checks | 240 K, 96 funcs | P1a, P1c |
| P2b | **UNPROJECT** (linear and rotational sweeps) | unprojection | 61 K, 13 large funcs | P1a, P1c |
| P3 | **world core**: entity store/ids, entity grid and box queries, CheckAreas and areas, events, deferred command queue, TimeStep driver (single-threaded), RayWorldIntersection / PrimitiveWorldIntersection | world core, areas, ray world intersection | 230 K | P1, P2a |
| P4a | **base entity + rigid entity**: parts, SetParams/GetStatus/Action ids used by the zone, Step / RegisterContacts / Update, **ApplyBuoyancy** | physical entity, rigid entity | 290 K | P2, P3 |
| P4b | **contact solver** (PGS, friction cone, velocity-bias unprojection, CG / LCP stages) | contact solver | 70 K, 28 funcs | P4a interfaces |
| P5a | living entity (players/NPC movement, only if the server needs it) | living | 75 K | P4a |
| P5b | wheeled vehicle | wheeled | 49 K | P4a/b |
| P5c | particle | particle | 31 K | P3 |
| P6 | articulated, rope, soft, mesh boolean, tetra lattice, voxel: **defer**. These are client-side ragdolls, cloth, ropes and breakables that the zone server is unlikely to need; check against `ZONE-PHYSICS-USAGE.md` before porting. | | ≈ 600 K | — |
| PT | **oracle FFI crate + golden traces** | — | — | P0; then grows with each package |

**Recommended order for ships first:** P0 → (P1a ‖ P1b ‖ PT) → P1c → (P2a ‖ P2b) → P3 → (P4a ‖ P4b).
With that done, a ship hull (box or trimesh) floating, colliding with terrain and other hulls, and
pushed by impulses runs entirely on the ported engine. The Jitter2 subset in `systems/ai/physics/`
can then be replaced. Parallelism: P1a, P1b and the oracle crate can be done by three agents at once;
P2a splits per pair family (box/tri/round/ray), so up to four agents; P3 and P4a/b are better kept to
one or two agents because they share the entity/world structs.

Open questions to settle while porting: see **Appendix A** (the first four are resolved).

---

## Appendix A — open questions

Resolved in P0 (oracle experiments in `crates/aaemu-physics/tests/experiments.rs`, scenarios
`tests/scenarios/exp_*.scn`):

1. **Solver stage-1 iteration cap: `max(4·nContacts, 6000)`.** InvokeContactSolver 0x3509E850
   computes `a = nC·V[12]; d = a − V[8]; budget = a − (d & (d >> 31))` (x64 0x3509FCDF…), the
   branch-free **max**, and passes it as the 7th argument of the PGS loop 0x3509AC90, which stops at
   `iters >= budget`. (Settled from the disassembly; an oracle experiment would need > 1500
   contacts in one island.)
2. **Quaternion product order: `q_ent = normalize(B.q ⊗ qfb)`.** Oracle: a spinning rigid plank
   with a rotated, offset part (qfb ≠ identity); `q_new` (+460) equals `B.q ⊗ qfb` evaluated with
   the engine's SSE lane order (`math::Quat::product`) and normalised with
   `((x²+w²)+y²)+z²` (`Quat::normalized_xwyz`) **bit for bit** at two steps; `qfb ⊗ B.q` does
   not match. Body integration is `q = dq ⊗ q` (verified bit-exact by the `bodystep` probe, which
   also pins MSVCR100 `sinf`/`cosf`, the double-precision quat→matrix and the R·diag·Rᵀ order).
3. **Buoyancy is applied in the same (sub)step.** ApplyBuoyancy issues
   `Action(action_impulse{iApplyTime(+72)=0, iSource(+76)=1}, bThreadSafe=1)` (stack init at
   0x350C0E50): immediate P/L update with |Δv|, |Δω| ≤ 5 and no wake, after that substep's
   integration and before gravity. Oracle: a box released at rest fully submerged has
   v_z = +0.758 after the first world step (a next-step application would give −0.327) and has
   already moved inside that step (rigid substeps ≤ 0.02 s); the deferred-impulse accumulator
   E+956 is never touched.
4. **Simclass lists: `world+664+8·k` holds simclass k** — 0 static (664), 1 sleeping rigid (672),
   2 active rigid (680), 3 living (688), 4 independent (696; particles are also on the independent
   stepping list +760), 7 deleted (720, from the TimeStep cleanup code).

Still open:

5. Many params fields are marked *(inferred)*; the P0 structs carry them with sentinel defaults.
   `tests/params_oracle.rs` verifies that the Rust default images of params pos (0), part (6), simulation (10), buoyancy
   (12), flags (15), foreign data (11), timeout (23) and actions impulse (2) and set_velocity (10)
   are neutral in the DLL and that set fields take effect; confirm the remaining ids
   when a phase starts using them.
6. E0 (energy reference of the solver divergence guards), GetMaxTimeStep constants, the post-step
   zeroing (`dynamics.md` §7 items 2, 6, 7).
7. MSVCR100 `asinf`/`acosf` and the double `sin`/`cos`/`exp`/`asin` are not ported bit-exactly
   yet (`math::crt`; measured mismatch rates in the crate README).
8. The zone server's value of the RWI "DrawRayOnGrid" limit (`gEnv+112` → slot 20 → `+248`).
