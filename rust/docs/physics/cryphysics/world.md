# CryPhysics — World object (CPhysicalWorld) and Areas (CPhysArea)

Draft section. Primary target: x64 10.x `cryphysics_dedicate.dll` (base 0x35000000, RTTI). x86 1.2 client addresses (base 0x34ff0000) are given where `compare/func_match.csv` / `compare/vtable_slots.txt` provide a match; sim<3 = tentative.
Everything below is derived from the decompiles. Names come from RTTI, binary strings (incl. x86 profiler strings) or are marked **(inferred)**. Offsets are decimal bytes unless written 0x.

Conventions seen everywhere:
- "unused" float marker = 0xFFBFFFFF (-4194305); test is `(bits & 0xFFA00000) == 0xFFA00000`. Unused int marker = 0x80000000.
- Spin locks: `while (InterlockedCompareExchange(lock, 0x10000, 0)) ;` = write lock; `InterlockedExchangeAdd(lock, 1); while (byte[lock+2]) ;` = read lock.
- Entity header fields used by the world (common CPhysicalEntity/placeholder prefix): +8..+28 AABB min/max, +32 pForeignData, +40 int16 iForeignData, +42 int16 flags (areas), +44/+46/+48/+50 int16 grid cell bbox (ix0, iy0, ix1, iy1; -2 = not in grid), +52 first grid-thunk index (20 bits), +56 placeholder<->entity link, +64 refcount/caller-lock bits (bit `1<<iCaller` used by RWI to skip), +68 low 23 bits = id (sign-extended `<<9>>9`), bit 0x800000 grid flag, byte +71 = simclass, +72 per-entity rw lock, +80 deletion state (0 alive, >0 pending), +84 time-since-deleted, +88 POD refcount, +92 flags (pef_*-like bits; 0x800 = "flagged", 0x4000/0x2000/... event monitor bits, see §5), +104 next in simclass list, +112 prev / world ptr (areas: +112 = world), +120 world ptr (entities), +128 int16 POD refs, +134 byte group mark, +136 group index, +144 next in group, +160 next in job list, +208/+216 contacting-entities array/count, +224 lock, +232 group leader, +252/+256 sleep timers (static/sleeping lists), +280 parts array (136-byte parts), +288 nParts.

---

## 1. CPhysicalWorld vtable (x64 0x351f0af8, 105 slots; x86 0x351fc598, 104 slots)

Other sub-vtables: this+8 IPhysUtils (x64 0x351f0ac8, 4 slots), this+16 CGeomManager (x64 0x351f09e8, 27 slots). x86 has an extra slot 6 (0x35082920 = the internal reset, x64 0x3507de80 is non-virtual) and slot 49 (0x35089f50, no x64 counterpart); x64 slots 44, 63, 85 have no x86 counterpart. Signatures are as decompiled (a1 = this omitted).

| x64 slot | x64 addr | x86 slot / addr | role / name | decompiled args (after this) |
|---|---|---|---|---|
| 0 | 0x350985d0 | 0 / 0x3508e180 | scalar deleting dtor -> 0x3507dcb0 | (char flags) |
| 1 | 0x35096800 | 1 / 0x350892a0 | ++counter @22580 (inferred) | () |
| 2 | 0x35096810 | 2 / 0x350892b0 | return counter @22580 | () |
| 3 | 0x35096820 | 3 / 0x350892c0 | counter @22580 = 0 | () |
| 4 | 0x35078ca0 | 4 / 0x35080f00 | set terrain origin vec @1652, copies into terrain entities @1568/@1576 parts and heightfield geom (+648) (inferred) | (Vec3*) |
| 5 | 0x35096660 | 5 / 0x35089120 | return &this[1652] (terrain origin) | () |
| — | — | 6 / 0x35082920 | x86-only: world reset (= x64 0x3507de80) | |
| 6 | 0x35078270 | 7 / 0x35083280 | Shutdown (inferred): calls TimeStep(…,415), stops workers, deletes all entities in lists @664..@720, @816, frees grid/pools | (int bDeleteGeometries) |
| 7 | 0x35096670 | 8 / 0x35089130 | Release: `this->vf0(1)` | () |
| 8 | 0x3507dc00 | 9 / 0x35088500 | "is current thread the POD-processing thread" (@4448) (inferred) | () |
| 9 | 0x35096690 | 10 / 0x35089140 | GetGeomManager -> this+16 | () |
| 10 | 0x350966a0 | 11 / 0x35089150 | GetPhysUtils -> this+8 | () |
| 11 | 0x3507e5a0 | 12 / 0x35089810 | SetupEntityGrid (inferred) §3 | (int axisz, Vec3* org, int nx, int ny, float stepx, float stepy, int log2PODscale) |
| 12 | 0x3507e570 | 13 / 0x35080ee0 | Init/Reset: vf6(0) then reset 0x3507de80 (inferred) | () |
| 13 | 0x35078f30 | 14 / 0x35083ca0 | register bbox in POD (physics-on-demand) grid, allocs 8x8 POD pages (inferred) | (AABB*) |
| 14 | 0x35076f30 | 15 / 0x35080fb0 | deactivate/free POD grid (inferred) | () |
| 15 | 0x350794f0 | 16 / 0x35084120 | query POD cells active in bbox (any/all) (inferred) | (AABB*, char bAny) |
| 16 | 0x35079760 | 17 / 0x35084370 | add POD refs of entity for bbox (ent+88, ent+128) (inferred) | (ent*, AABB* or null) |
| 17 | 0x3508b860 | 18 / 0x35093c00 | SetHeightfieldData: (re)creates 5 per-caller terrain entities @1568..1600 (inferred) | (hf*, int* matMapping, int nMats) |
| 18 | 0x35078d60 | 19 / 0x35083ae0 | GetHeightfieldData (copies @1616..1660) (inferred) | (out*) |
| 19 | 0x35078e20 | 20 / 0x35083b80 | SetHeightfieldMatMapping on the 5 terrain geoms (inferred) | (int* map, int n) |
| 20 | 0x350966b0 | 21 / 0x35089160 | **GetPhysVars -> this+104** | () |
| 21 | 0x35082f00 | 22 / 0x3509e020 CreatePhysicalEntity | CreatePhysicalEntity (x86 string) | (int type, float lifeTime, params*, foreignData, int iForeignData, int id, hostPlaceholder*, u8 flags) |
| 22 | 0x350966c0 | 23 / 0x35089170 | CreatePhysicalEntity overload -> slot 21 (inferred) | (type, params, fdata, ifdata, id, char) |
| 23 | 0x3507e990 | 24 / 0x35089bd0 | CreatePhysicalPlaceholder (placeholder bitmap pool @1352/@1360/@1376) (inferred) | (int type, params*, fdata, int16 ifdata, int id) |
| 24 | 0x35083830 | 25 / 0x3509e7d0 DestroyPhysicalEntity | DestroyPhysicalEntity (x86 string) | (ent*, int mode, int bThreadSafe) |
| 25 | 0x3507ede0 | 26 / 0x3508a240 | SetPhysicalEntityId (id table @1080) (inferred) | (ent*, int id, int bReplace, int bThreadSafe) |
| 26 | 0x35077300 | 27 / 0x35081420 | GetPhysicalEntityId (`ent+68 <<9>>9`, -1 for null) | (ent*) |
| 27 | 0x35079be0 | 28 / 0x35084770 | GetPhysicalEntityById (bit30 of id selects ent vslot 33 vs 32) (inferred) | (int id) |
| 28 | 0x350770f0 | 29 / 0x35081150 | SetSurfaceParameters ext. (calls slot 29, then 4 ints @10600/12648/14696/16744) (inferred) | (int idx, bounce, friction, flags, i0, i1, i2, i3) |
| 29 | 0x35077020 | 30 / 0x35081080 | SetSurfaceParameters: bounciness @4456, friction @6504, dyn-friction=friction*2/3 @8552, flags @18792; idx<512 (inferred) | (int idx, float bounce, float friction, int flags) |
| 30 | 0x35077180 | 31 / 0x350811e0 | GetSurfaceParameters ext. | (idx, …, int* x4) |
| 31 | 0x350770a0 | 32 / 0x35081110 | GetSurfaceParameters | (idx, float*, float*, int*) |
| 32 | 0x3508bd10 | 33 / 0x3509f6a0 TimeStep | **TimeStep** (x86 string) §4 | (float dt, int flags) |
| 33 | 0x3507dc30 | 34 / 0x35088520 | reset per-thread TLS caller slot (inferred) | () |
| 34 | 0x35096710 | 35 / 0x350891a0 | GetPhysicsTime -> float @1664 | () |
| 35 | 0x35096720 | 36 / 0x350891b0 | GetiPhysicsTime -> int @1688 | () |
| 36 | 0x35096730 | 37 / 0x350891c0 | SetPhysicsTime (also @1688 = t/vars.timeGranularity) | (float) |
| 37 | 0x35096770 | 38 / 0x35089200 | SetiPhysicsTime | (int) |
| 38 | 0x35096790 | 39 / 0x35089230 | SetSnapshotTime @1672+4i / @1692+4i | (float, int i) |
| 39 | 0x350967d0 | 40 / 0x35089270 | SetiSnapshotTime | (int, int i) |
| 40 | 0x350989b0 | 41 / 0x3509df80 | **GetEntitiesInBox** wrapper -> 0x35084680 under lock @22492, iCaller=4 | (Vec3* min, Vec3* max, IPhysicalEntity**& list, int objtypes, int szPrealloc) |
| 41 | 0x35094d80 | 42 / 0x350a2b20 | collect entities via iterator into std::vector with filter flags (inferred) | (vector*, int flags) |
| 42 | 0x351aa730 | 43 / 0x351c2b00 RayWorldIntersection | **RayWorldIntersection(SRWIParams&)** §6 | (params*, const char* tag, int iCaller) |
| 43 | 0x351a9fc0 | 44 / 0x351c3f70 | process queued RWI/PWI ("RayWorldIntersection(Queued)", "PrimitiveWorldIntersection(Queued)", "rwiQueueSz(%d)…") | (int) |
| 44 | 0x351a9ba0 | — | ray vs terrain heightfield (called by RWI when objtypes&0x100) (inferred) | (Vec3* org, Vec3* dir, hits*, int flags, int iCaller) |
| 45 | 0x3507b220 | 45 / 0x35085be0 | broadcast entity Action type 4 to all ents in simclass lists 1..4 (inferred) | () |
| 46 | 0x35081ac0 | 46 / 0x3508c260 | DestroyDynamicEntities (string "…func:CPhysicalWorld::DestroyDynamicEntities") | () |
| 47 | 0x3507b2c0 | 47 / 0x35085ca0 | flush deferred command queue @20872 (executes pending destroys, drops ops on deleted ents) (inferred) | () |
| 48 | 0x35096830 | 48 / 0x350892d0 | GetEntityCount(type) -> @1116+4*type | (int type) |
| — | — | 49 / 0x35089f50 | x86-only slot (unidentified) | |
| 49 | 0x3507db70 | 50 / 0x350884e0 (sim 0.7, tentative) | GetEntitiesIterator: new CPhysicalEntityIt (vt 0x351f0798), walks lists @664..@720 then @816 | () |
| 50 | 0x3508edf0 | 51 / 0x35097090 SimulateExplosion | SimulateExplosion (x86 string) | (explosion*, ent** skip, int nSkip, int objtypes, int iCaller) |
| 51 | 0x3508e460 | 52 / 0x350a1c50 | deform/break entity at point (uses same breakage path as TimeStep deformation queue) (inferred) | (ent*, Vec3* pt, Vec3* dir, int, int) |
| 52 | 0x3507a1e0 | 53 / 0x35084cf0 | update deforming-entity list @21656 (calls structure update 0x35061030) (inferred) | (float dt) |
| 53 | 0x35086960 | 54 / 0x3508e1c0 | entity query vs explosion-like params (skips areas, type 9) (inferred, uncertain) | (params*, ent*) |
| 54 | 0x3507b170 | 55 / 0x35085ae0 | IsAffectedByExplosion: looks ent up in last-explosion list (@1536 ents, @1544 kickback float, @1552 impulse Vec3, @1560 count) (inferred) | (ent*, Vec3* impulseOut) |
| 55 | 0x350820f0 | 56 / 0x3508c830 | AddExplosionShape: 48-byte entries @21632 sorted by idmat (low16 of arg), group first/count @+32/+36 (inferred) | (geom*, float size, int idmat, float prob) |
| 56 | 0x35077c90 | 57 / 0x35081d30 | RemoveExplosionShape(id) (inferred) | (int id) |
| 57 | 0x35096960 | 58 / 0x35089430 | RemoveAllExplosionShapes(callback) (inferred) | (void(*)(geom*)) |
| 58 | 0x3507b4f0 | 59 / 0x35085ef0 | debug dump/list entities ("listing physics object : %s", "active POD : %d") | (__int64*, int) |
| 59 | 0x35086db0 | 60 / 0x3508e860 CollideEntityWithBeam | CollideEntityWithBeam | (ent*, Vec3* org, Vec3* dir, float r, Vec3* hitOut?, int) |
| 60 | 0x350874b0 | 61 / 0x3508f150 CollideEntityWithPrimitive | CollideEntityWithPrimitive | (ent*, int itype, prim*, Vec3* dir, contact**, …, char) |
| 61 | 0x3508aa10 | 62 / 0x350921f0 RayTraceEntity | RayTraceEntity | (ent*, Vec3* org, Vec3* dir, hit*, pip*, int, char) |
| 62 | 0x35088a70 | 63 / 0x350908a0 CollideEntityToEntity | CollideEntityToEntity | (ent*, ent*, contact*, float*, char) |
| 63 | 0x35089cd0 | — | 10.x-only variant of CollideEntityToEntity (same callees, double* arg) (inferred) | (ent*, ent*, contact*, double*, uint) |
| 64 | 0x350964f0 | 64 / 0x35089600 | **PrimitiveWorldIntersection** wrapper: builds SPWIParams (96 B) -> slot 65 with tag "PrimitiveWorldIntersection" | (itype, prim*, Vec3* sweepDir, entTypes, contact**, geomFlagsAll, geomFlagsAny, pip*, fdata, ifdata, ent** skip, int nSkip) |
| 65 | 0x35093100 | 65 / 0x3509b8c0 (sim 0.0, tentative) | **PrimitiveWorldIntersection(SPWIParams&)** core | (params*, const char* tag) |
| 66 | 0x3507c8a0 | 66 / 0x35087200 | GetMemoryStatistics (sizer names "grid","thunk","world queues","areas","placeholders","temp ent list","groups","hidden entities","occl","world structures") | (ICrySizer*) |
| 67 | 0x35096860 | 67 / 0x35089310 | set streamer callback @640 (POD activation/destroy, vslot +32 = destroy-in-box) (inferred) | (ptr) |
| 68 | 0x35096870 | 68 / 0x35089320 | set second callback @648 (inferred) | (ptr) |
| 69 | 0x35096880 | 69 / 0x35089330 | last step time for entity's simclass: float @1708+4*(simclass&7) (inferred) | (ent*) |
| 70 | 0x35077aa0 | 70 / 0x35081b70 | step progress: {#groups @22416, iteration @21788} (inferred) | (int[2]*) |
| 71 | 0x350968f0 | 71 / 0x350893a0 | entity profile array @1760 (72 B), count @2912 | (out ptr*) |
| 72 | 0x35096910 | 72 / 0x350893c0 | function profile array @2920 (72 B), count @2928 | (out ptr*) |
| 73 | 0x35096930 | 73 / 0x350893e0 | group profile @2944, 14 entries ("Rigid bodies"…"World step total") | (out ptr*) |
| 74 | 0x35096940 | 74 / 0x35089400 | job profile @3952, 6 entries ("rb_intersect","solver","cloth","rope","featherstone","rwi") | (out ptr*) |
| 75 | 0x35077ae0 | 75 / 0x35081bd0 | **AddEventClient** (inferred): list @21232+16*type+8*logged, node 64 B {fn,next,prio float,…}, sorted by priority desc | (int type, fn, int bLogged, float prio) |
| 76 | 0x35077bc0 | 76 / 0x35081c70 | **RemoveEventClient** (inferred) | (int type, fn, int bLogged) |
| 77 | 0x35094fa0 | 77 / 0x350a2d70 PumpLoggedEvents | **PumpLoggedEvents** §5 | () |
| 78 | 0x35077c50 | 78 / 0x35081cf0 | return-and-clear int @4440 (inferred) | () |
| 79 | 0x3507d4f0 | 79 / 0x35087d80 | ClearLoggedEvents: recycles queue @21088 into free lists, resets RWI hit pool (inferred) | () |
| 80 | 0x3504a1a0 | 80 / 0x350526e0 | **AddGlobalArea** (creates area @20928 if absent, gravity = vars.gravity) (inferred) | () |
| 81 | 0x35049c60 | 81 / 0x35056c50 | **AddArea(spline/points tube)** (inferred) §7 | (Vec3* pts, int npt, float r, Vec3* pos, quat*, float scale) |
| 82 | 0x35049790 | 82 / 0x35056450 | **AddArea(geometry)** (inferred) | (IGeometry*, Vec3* pos, quat*, float scale) |
| 83 | 0x35047c80 | 83 / 0x35054890 | **AddArea(2D polygon prism, zmin/zmax, tessellation)** (inferred) | (Vec3* pts, int npt, float zmin, float zmax, Vec3* pos, quat*, float scale, Vec3* n, int* tessIdx, int nTess, …) |
| 84 | 0x35041be0 | 84 / 0x35052920 | **RemoveArea** (emits event 12, moves area to deleted list @20944) (inferred) | (area*) |
| 85 | 0x350418a0 | — | GetNextArea(prev) (null -> global area) (inferred) | (area*) |
| 86 | 0x35041910 | 85 / 0x35049180 | IsAreaValid/registered (type 9 and present in medium list @20952) (inferred) | (area*) |
| 87 | 0x3504e000 | 86 / 0x35052b90 | **CheckAreas** (inferred) §7 | (Vec3* pt, Vec3* gravity, buoy* pb, int nMaxBuoys, int iMedium, Vec3* vel, ent*, float r, int iCaller, area** out, int nMaxOut) |
| 88 | 0x35041850 | 87 / 0x35049130 | SetWaterMat: @20840; @20844 = 0x200 if global area has a water plane (inferred) | (int) |
| 89 | 0x35096950 | 88 / 0x35089420 | GetWaterMat @20840 | () |
| 90 | 0x3507d880 | 89 / 0x350880c0 | SetWaterManagerParams (params type 22; creates 0xC0-byte manager @20848 via 0x350fe450) (inferred) | (params*) |
| 91 | 0x35077db0 | 90 / 0x35081e30 (sim 2.2) | GetWaterManagerParams | (params*) |
| 92 | 0x35077dd0 | 91 / 0x35081e50 | GetWaterManagerStatus (inferred) | () |
| 93 | 0x3507d930 | 92 / 0x35088190 | DestroyWaterManager (inferred) | () |
| 94 | 0x350968a0 | 93 / 0x35089350 | get internal lock address: 1->@22468, 4->@22496, 5->@22508, 2..6->@22476+4*(i-2) (inferred) | (int) |
| 95 | 0x35077c70 | 94 / 0x35081d10 | return 0 stub | () |
| 96 | 0x35077c80 | 95 / 0x35081d20 | return 0 stub | () |
| 97 | 0x3507d980 | 96 / 0x350882c0 | lazily constructs one static prototype entity per type (same ctors as slot 21) and forwards to proto vslot 15 (inferred: serialize/defaults by type) | (ptr, int type) |
| 98 | 0x350824b0 | 97 / 0x3508caa0 | serialize entity header: "entity_ptr","id","simclass","com","mass" | (ser*, ent*) |
| 99 | 0x3508b530 | 98 / 0x3509da90 | deserialize/lookup entity by the same keys, verifies mass/com | (ser*) |
| 100 | 0x350823c0 | 99 / 0x350881c0 | entity total part mass + bbox centre (inferred) | (ent*, float* mass, Vec3* c) |
| 101 | 0x35037700 | 100 / 0x350894a0 | empty stub (ICF-shared) | |
| 102 | 0x35076030 | 101 / 0x350894b0 | return 1 stub (ICF-shared) | |
| 103 | 0x35097000 | 102 / 0x35093a50 | **SignalEvent(type, event*)** dispatch to per-type signal functions §5 (inferred) | (int type, void* ev) |
| 104 | 0x35096ee0 | 103 / 0x35089560 | **RayWorldIntersection** legacy arg-list wrapper -> slot 42 | (Vec3* org, Vec3* dir, objtypes, flags, hits*, nMaxHits, ent** skip, nSkip, fdata, ifdata, tag, hitLast*, iCaller) |

---

## 2. World parameters block ("physics vars", this+104 .. this+632)

Getter: x64 slot 20 0x350966b0 returns `this+104`. The solver gets the same pointer (`0x3509e850(…, this+104, …)` inside the rigid group step). Entities read it via `ent->world(+120) + off`. Defaults from ctor 0x350827b0 (x86 0x350889b0). "Off" = offset in CPhysicalWorld; "rel" = offset inside the vars struct (off-104). Meaning only where usage was seen; otherwise "?" plus the readers.

| off | rel | type | default | meaning / evidence |
|---|---|---|---|---|
| 104 | 0 | int | 8 | ? |
| 108 | 4 | float | 50.0 | ? |
| 112 | 8 | int | 6000 | ? |
| 116 | 12 | int | 4 | ? |
| 120 | 16 | int | 6000 | ? |
| 124 | 20 | float | 0.005 | ? (read by 0x350d06a0) |
| 128 | 24 | float | 0.005 | ? |
| 132 | 28 | int | 150 | contact-count threshold in rigid group step: if sum of per-entity counts <= this use @136 else @140 |
| 136 | 32 | int | 8 | solver iteration/substep count for "small" groups (passed to ent vslot 40/39) |
| 140 | 36 | int | 4 | same for "large" groups |
| 144 | 40 | int | 120 | ? |
| 148 | 44 | int | 250 | ? |
| 152 | 48 | int | 12000 | ? |
| 156 | 52 | int | 25000 | ? |
| 160 | 56 | int | 5 | ? |
| 164 | 60 | float | 0.05 | ? |
| 168 | 64 | int | 4 | ? |
| 172 | 68 | float | 0.05 | ? |
| 176 | 72 | float | 0.02 | distance threshold (geometry code 0x350dd620, 0x350d06a0) |
| 180 | 76 | float | 500.0 | ? |
| 184 | 80 | float | 500.0 | ? |
| 188 | 84 | float | 10.0 | ? |
| 192 | 88 | int | 0 | ? |
| 196 | 92 | float | 100.0 | ? |
| 200 | 96 | float | 15.0 | ? |
| 204 | 100 | int | 100 | ? |
| 208 | 104 | int | 0 | single-step mode: when !=0 rigid/independent stepping only runs if @212 != 0 |
| 212 | 108 | int | 0 | "do one step" trigger (cleared at end of step); value 2 = dump world to `D:\worldents.txt` / `D:\worldgeoms.txt` (world vslots +760/+768) |
| 216 | 112 | float | 0 | fixed-step override: if >0 and dt>0, dt := this |
| 220 | 116 | float | 1e-4 | time granularity: dt is quantised to multiples; iPhysicsTime @1688 = time/granularity; 1/gran cached @432 |
| 224 | 120 | float | 0.2 | max world dt (dt clamped to this) |
| 228 | 124 | int | 0 | debug flags (bit 0x40: report rays to debug callback @656); read heavily by slot 58 |
| 232 | 128 | int | 0 | ? (CPhysicalEntity vslot 73) |
| 236 | 132 | int | 0 | grid flags: bit0 = rays/boxes leaving the grid also visit the out-of-grid cell |
| 240 | 136 | float | 0.01 | small distance epsilon (entities multiply by 4, 5, square it) |
| 244 | 140 | float | 0.01 | living-entity distance epsilon (CLivingEntity vslot 34) |
| 248 | 144 | float | 6.0 | ? |
| 252 | 148 | int | 1 | ? |
| 256 | 152 | int | 0 | ANDed each step with !system.byte[290] |
| 260 | 156 | float | 10.0 | copied into rope/rigid entities (+616) |
| 264 | 160 | float | 2.5 | ? |
| 268 | 164 | float | 10.0 | ? (rope) |
| 272 | 168 | int | 1 | ? |
| 276 | 172 | int | 5 (ctor first sets 10) | **max substeps**: outer rigid loop count and per-independent-entity step loop |
| 280 | 176 | int | 512 | ? |
| 284 | 180 | Vec3 | (0,0,-9.8) | **gravity** (copied into global area +468; area params type 10/18 write it back when area is the global one) |
| 296 | 192 | int | 8 | ? |
| 300 | 196 | float | 0.5 | ? |
| 304 | 200 | int | 5 | ? |
| 308 | 204 | int | 30 | ? |
| 312 | 208 | int | 0 | validation strictness: entity SetParams paths and grid insert ("…is too large or invalid") mark entity bbox invalid (-2) when set |
| 316–340 | | int | 0 | debug/profiling switches (316/320/324 read by slot 58; 336 read by several Step functions); 328 := system.byte[290], 332 := system.byte[289] every step; 340 = function-profiling on (0x35081ef0 timings) |
| 344 | 240 | int | 0 | entity-type profiling on (TimeStep fills @2952..) |
| 348 | 244 | int | 800 | ? |
| 352 | 248 | int | 512 | ? |
| 356 | 252 | int | 300000 | max grid cells covered by one entity (else "Error: %s @ … is too large or invalid") |
| 360 | 256 | int | 128 | same limit for particles (simclass byte 5) |
| 364 | 260 | float | 100.0 | speed/size limit used by articulated & ent code |
| 368 | 264 | float | 150.0 | ? |
| 372 | 268 | float | 0.03 | ? (0x350ad060, squared) |
| 376 | 272 | float | 0.3 | ? |
| 380 | 276 | int | 1 | ? (CRigidEntity vslot 81) |
| 384 | 280 | int | 1 | ? |
| 388 | 284 | int | 256 | ? |
| 392 | 288 | int | 0 | breakage switch read by structure-update 0x35061030 / ent vslot 73 |
| 396 | 292 | int | 100000 | ? |
| 400 | 296 | int | 1 | breakage-event logging: structure events signalled with mask (@400+1) -> 1 immediate, 2 logged |
| 404 | 300 | float | 0.1 | used by 0x35061030 (breakage) |
| 408 | 304 | float | 1.2 | ? (0x3505f140) |
| 412 | 308 | int | 7 | ? |
| 420 | 316 | float | 0 | last dt (written each step unless flag 0x800); 0 makes PumpLoggedEvents stop at postStep events of current step |
| 424 | 320 | int | 0 | thread-index bias (if iCaller+this>4 the caller index is re-derived) (inferred) |
| 428 | 324 | float | 1.0 | ? (breakage, ent vslots 10/59) |
| 432 | 328 | float | (1/granularity) | written in TimeStep |
| 436 | 332 | float | 1e10 | ? |
| 440 | 336 | int,int | 0x80000000, -1 | ? |
| 448 | 344 | int | 0 | ? |
| 452 | 348 | float | 0 | ? |
| 456 | 352 | float | 7.0 | ? |
| 460 | 356 | float | 15000 | ? |
| 464 | 360 | float | 4.5 | ? |
| 468 | 364 | float | 30.0 | ? |
| 472 | 368 | float | 150000 | ? |
| 476 | 372 | float | 10.0 | ? |
| 480 | 376 | int | 0 | ? |
| 484 | 380 | float | 1.0 | ? |
| 492 | 388 | int | 0 | ? |
| 496 | 392 | float | 1.0 | time-scale-like: when !=1 and dt>0 a counter @4436 increments; after 5 such steps (and event pump lag < @568) slow rigid bodies (v^2<25) are re-awakened (inferred) |
| 504 | 400 | int | 1 | **number of physics threads** (clamped to [?,4]; TimeStep spawns n-1 SPhysTask workers when it changes) |
| 508 | 404 | int | 1536 | worker stack size in KB (`<<10`, passed with @520) |
| 512 | 408 | int | 4 | ? |
| 516 | 412 | int | 1 | ? |
| 520 | 416 | int | 4 | passed with stack size (worker param) |
| 524 | 420 | int | 0 | ? |
| 528 | 424 | i64 | 0 | ? |
| 536 | 432 | i64 | 3,000,000,000 | event-handler time budget: handlers whose `300*ticks` exceed it are logged as "PhysEventHandler(%d,%d)" |
| 544 | 440 | float | 0.1 | ? |
| 548 | 444 | float | 0.1 | ? |
| 552 | 448 | float | 0.99 | ? |
| 556 | 452 | float | 0.01 | ? |
| 560 | 456 | float | 5.0 | ? |
| 564 | 460 | int | 1 | ? (0x35055860) |
| 568 | 464 | int | 20 | max event-pump lag for the re-awake pass |
| 572 | 468 | int | 0 | keep temp memory: if 0, TimeStep frees pools @1000/@21616 at end of step |
| 576 | 472 | int | 0 | ? |
| 580 | 476 | int | 5 | ORed into an RWI internal flag word (inferred) |
| 584 | 480 | float | 0.01 | breakage tolerance (0x35061030) |
| 588 | 484 | float | 8.0 (set last) | ? |
| 592 | 488 | float | 100.0 | soft-entity divisor |
| 596 | 492 | float | 0.02 | rope segment length divisor |
| 604 | 500 | float | 7.0 | soft entity |
| 608 | 504 | float | 1.0 | ? |
| 612 | 508 | int | (system==null or system.vf47()==2) | dedicated/server mode: deleted entities age by dt (+84) and are freed after 30 s |
| 616 | 512 | i64 | 0 | ? |
| 620 | 516 | int | 0 | log event statistics in PumpLoggedEvents ("[PhysEvents] … total(%d)") |
| 624 | 520 | float | 0.001 | living-entity epsilon |
| 628 | 524 | float | 0.001 | rigid epsilon (0x350c4880) |

---

## 3. Entity grid

Members (x64 offsets): +856 R (3x3, identity), +896 origin Vec3, +908/+912 cell size x/y, +916/+920 inverse cell size, +924/+928 nx/ny (clamped to 1024), +932/+936 strides (1, nx), +940/+944 world-axis index of grid x/y (from tables 0x352094c8/0x352094d8 indexed by axisz), +948 axisz, +952/+956 z quantisation step/inverse, +960..+976 sparse cell map (from 0x35095da0: +960 log2 of row bits, +964/+968 masks, +976 page-pointer array, pages of 8x8 cells, each cell = int index of first thunk; empty pages read a zero dummy), +984 thunk array, +992 thunk capacity, +996 free-thunk list head.

- **SetupEntityGrid** x64 slot 11 0x3507e5a0 (x86 0x35089810): detaches every entity/part/area from the old grid (0x3507a860), frees pages, grows thunk pool (0x35078050, 100000), calls vslot 14 (POD reset), stores params, builds the page table, sets POD scale @1056 and POD page strides @1048=1, @1052 = ny>>(log2POD+3). Out-of-grid cell index = nx*ny.
- **Thunk** (32 bytes, index-linked, max 0x100000, grown by 0x8000): +0 bits0-19 next thunk in cell; +4 bits0-19 prev thunk in cell, or, when +8 bit 0x800000 is set (first in cell), the packed cell coords `ix | iy<<10`; +8 bits0-19 next thunk of the same entity, bit 0x800000 = head-of-cell, bits 20-22 simclass; +12..+15 bytes = sub-cell AABB (x0,y0,x1,y1 in 1/256 cell units, clamped 0..255); +16 entity ptr; +24/+26 uint16 quantised zmin/zmax (by @956). Simclass-5 thunks are kept at the front of a cell chain (insertion skips over them).
- **RepositionEntity** (inferred) 0x35080a00 (x86 0x35095fd0): flags bit0 = grid update, bit2 = test each cell against geometry (0x35055860). Computes cell range of the entity bbox; if unchanged and no flag change, only refreshes sub-cell bytes/z of existing thunks; otherwise detaches (0x3507a860), validates cell count against vars @356/@360, allocates one thunk per covered cell, writes ent +44..+50 cell bbox and +52 thunk head, mirrors to linked placeholder (+56). Logs "Error: too many entity grid thunks created, further repositions ignored" (once). Returns -1 for oversize areas (caller keeps them in the "big area" list). Also signals event 2 when monitored (via 0x350962d0).
- **GetEntitiesInBox / GetEntitiesAround** 0x35084680 (x86 0x350942c0 CPhysicalWorld__GetEntitiesAround, string "GetEntitiesInBox: too many cells request"): args (min, max, IPhysicalEntity**& list, int objtypes, petitioner ent*, int szListPrealloc, int iCaller). Per-caller result cache (bbox @1192+24*i, objtypes @1312+4*i, count @1332+4*i) returns the previous list if identical. Profiled as "GetEntitiesAround(Physics)" / "(External)". Iterates cells, filter `objtypes & (1 << thunk.simclass)`; extra objtype bits seen: 0x20 = include areas (walks area list @20928 with bbox test), 0x100 = include terrain entity @1568+8*iCaller, 0x10000 / 0x80001 / 0x20000 (sort/sleep handling), 0x40000 = caller supplied list, 0x100000 = AddRef results. Also queries POD cells (0x35095f70) and can trigger POD activation. If petitioner flags request it, emits event 0 (bbox overlap) per hit. Uses temp lists @792/@800/@808 or per-caller @21864+80*i.
- **POD (physics-on-demand) grid**: @1016 page array (pages of 8x8 cells, 16 B each: +0 next-active link, +4 float lifetime, +8 zmin, +12 zmax; new pages init zmin=1000, zmax=-1000), @1040 dummy cell, @1056 log2 scale, @1060 enabled, @1064 active list head (packed `ix | iy<<16`). Cell lookup 0x35095f70. TimeStep (flag 0x80) decrements lifetime of active cells by dt and, when expired, calls the streamer @640 vslot +32 with the cell box (destroy-in-box). Ray traversal (0x351ac640) activates cells via the streamer.

---

## 4. TimeStep pipeline — x64 slot 32 0x3508bd10 (x86 0x3509f6a0 CPhysicalWorld__TimeStep)

`TimeStep(float dt, int flags)`; dt<0 does nothing. Observed flag bits: 0x4 rigid step, 0x8 living step, 0x10 independent step + sleeping-list callbacks, 0x80 deferred/cleanup phase, 0x800 "flagged-only" step (entities with flag 0x800 are stepped / dt not stored in @420), 0x1000 (passed shifted as 0x800 mask) skip flagged.

Order:
1. **Thread pool**: clamp vars.nThreads (@504) to <=4; if count changed, stop old SPhysTask workers (set +20 quit, SetEvent start @21800+8i, WaitForSingleObject done @21832+8i, release) and create n-1 new `SPhysTask` (0x58 B, vt 0x351f0768, run = SPhysTask::vf0 -> 0x350807e0, name "PhysicsWorkerThread", stack @508 KB) via the system thread manager (system vslot +672). Busy-waits until @22424 == n-1. Worker loop 0x350807e0: TLS caller index = worker index (1..4), wait start event, dispatch on job mode @21784 (0/1 rigid groups 0x3507f9b0, 2 post-step list 0x3507a3b0, 3 living 0x350801b0, 4 independent 0x3507a520, 5 0x3507a6d0 unused), signal done event.
2. **Deferred command queue** (double buffer @20872/@20880, counts @20888/@20896, lock @22496): records `{int op (-1 end), int size, ent*, …}`; op 0 SetParams (ent vslot 7), 1 Action (vslot 10), 2 AddGeometry (vslot 11), 3 RemoveGeometry (vslot 12), 4 grid reposition (0x35080a00 + grow pools @792..@848 by 0x10000, "increasing physical entity pools … TimeStep"), 5 Destroy (world slot 24). Ropes/soft (simclass byte 7, or 5 with +80==2) only take op 1. Then deferred destroys @20912 (16 B, count @20924).
3. **dt handling**: dt = min(dt, vars @224); if granularity @220>0: dt rounded to multiple, iPhysicsTime @1688 advanced; physicsTime @1664 updated; fixed step @216 overrides; @4432/@21776 = dt; @420 = dt unless flag 0x800; refresh global-area gravity from vars; @212==2 world dump; flag 0x8: call vslot 26 (+208, pre-step hook) on living list @688.
4. If not single-stepping (@208==0 or @212!=0):
   - optional re-awake of slow rigid bodies (see vars @496/@568);
   - **deformation queue** @21672 (312-byte records, ring head @21684, count @21688, lock @22548): find part, 0x35086350 (compute deformation), 0x35061030 (structure update, "Lattice tension") -> 0x350819a0;
   - flag 0x10: vslot 26 on independent list @696;
   - **rigid bodies** (flag 0x4, dt>0, list @680): outer loop up to vars.maxSubsteps (@276) iterations until all done (v312): build interaction **groups** (BFS over contacts +208/+216, linked via +144, leader per group in @800, inverse-mass sums in @824, dt per group from ent vslot 69 (+552)); group dt equalised (`*1.01`), sorted (0x35079cc0); two passes (pass index -> job mode 0/1): workers + main thread pull groups atomically (@22428 < @22416, lock @22448) in 0x3507f9b0: pass 0 = per-entity StartStep/Step (vslots 37 +296 / 34 +272, vslot 27 +216 to roll back if not all succeeded), pass 1 = contact setup (vslots 39/40 with iteration counts @136/@140) then group solver 0x3507f210 using vars; after pass 0 the awake list @1752 is sorted (0x35077370) and run as job mode 2 (0x3507a3b0). Living entities get 0x3502b3f0 in that pass. `while (0x3516e360(&done,1) >= 0)` lets the main thread help with jobs. Per-simclass step times @1712/@1716.
   - entity profile counters reset (@1768.. 72-B records, count @2912); water manager step 0x350fe6b0 if @20848.
5. **Living** (flag 0x8): job mode 3 over list @688 (0x350801b0: entities partitioned so no two threads simulate overlapping swept bboxes; per-thread bbox @22296+24*i, lock @22456); time @1720.
6. **Independent** (flag 0x10, list @760): job mode 4 (0x3507a520): per entity StartStep (+296) then Step (+272) repeated up to @276 times; time @1724.
7. **Cleanup** (flag 0x80, when stepping): update deforming list @21656 (0x35061030), time @1708; 0x35079b30 drops logged events whose entities were deleted; free deleted entities in list @720 (simclass 7) when `pumpCounter(@22516) >= ent+80` and (age +84 > 30 s or refcount low) — "[Physics] deleted too many objects : %d" if >500000; POD cell aging (thread id @4448 during it); sleep handling for lists @664/@672: entities with any part flag 0x80000 or active structure are re-classed (0x35077650, string "Error: *major* problem - trying to change simclass") / placeholder swap via world slot 25/24; otherwise sleep timer +252 += dt and when > +256 the entity is destroyed via slot 24 mode 0 (timed-out static/sleeping entities); reset +252 on active list; free deleted areas @20944 (refcount +524 == 0); reset per-job lists @21896..@22216; time @1736.
8. `@1112 = 0; @1744 = 0; ++stepCounter @21572` (if dt>0); optional profiling summary (@344): 11 type counters via 0x35077f70, "World step total" time @3896; if @572==0 free temp pools (@1000, @21616) and 0x3514ac60 (geom-manager cleanup).

Locks used: @22468 entity lists, @22472 grid, @22492 world queues, @22496 command queue, @22504, @22508 areas, @22512 event queue, @22520 deforming list, @22524 RWI queue, @22528 RWI hit pool, @22548 deformation queue, @22556, @22460 POD, @22464 id map, @22476+4*i per caller (i=0..4).

Entity lists: heads @664+8*k for simclass k=0..7 (0 static, 1 sleeping, 2 active rigid, 3 living, 4 independent, 7 deleted — by usage; 5/6 not seen stepped here), markers @728+8*k, list @760 (independent stepping list in flag 0x10 path — may be a second "typed" list set), @816 extra list (iterator tail; hidden/placeholder entities (inferred)).

---

## 5. Events

Storage: logged-event queue head/tail @21088/@21096 (lock @22512); allocation 0x3507d400(world, type, size): per-type free list @21104+8*type, 8 KB arena pages chained from @21488 (current @21496, used @21504), per-type count @21508+4*type. Every event starts with `{+0 next*, +8 int type, +16 entity*, +24 entity2* or foreignData …}`. Clients: @21232 + 16*type + 8*bLogged (16 types), node 64 B `{+0 fn(int(*)(event*)), +8 next, +16 float priority, +56 time spent}`; immediate handlers are summed (return value), list sorted by priority (descending).

Per-type signal functions (also reachable through slot 103, which takes `(type, event*)`):

| type | size | signal fn (x64) | immediate mask / log mask (entity +92 flags) | emitted from / meaning (inferred) |
|---|---|---|---|---|
| 0 | 56 | 0x35037df0 | always / always | GetEntitiesInBox petitioner hits, CLivingEntity vslot 34 — bbox overlap (2 entities) |
| 1 | 160 | 0x35037ef0 | 0x2000 / 0x2000000 | CRigidEntity, 0x35061030, many — collision (2 entities) |
| 2 | 56 | 0x350962d0 | 0x1000 / 0x1000000 | RepositionEntity 0x35080a00 — grid/state change |
| 3 | 64 | 0x35096ae0 | 0x4000 / 0x4000000 | CRigidEntity vslot 62 after awake change — sim-class/awake state change |
| 4 | 80 | 0x35037fe0 | 0x80000 / 0x8000000 | living/particle/soft/rigid step — post-step (carries step index at +72, compared with @21572) |
| 5 | 136 | 0x35073180 | 1 / 2 (arg = vars@400+1) | structure update 0x35061030 — geometry/mesh update |
| 6 | 200 | 0x35073270 | 1 / 2 | 0x35061030 — part created by breakage |
| 7 | 64 | 0x35073360 | 1 / 2 | 0x35061030 — parts removed |
| 8 | 128 | 0x35073570 | 1 / 2 | 0x35061030, CRigidEntity vslot 59, articulated/rope code — joint broken |
| 9 | 72 | 0x35096bf0 | always | queued RWI result (slot 43); +48 hits ptr, +56 nHits, +60 nMaxHits; has callback at +40 |
| 10 | 88 | 0x35096d00 | always | queued PWI result (slot 43); callback at +40 |
| 11 | 176 | 0x3504fd40 | always | CheckAreas for areas with bUseCallback (+520): carries point, gravity, buoyancy copy (+388..+468), closest point/normal |
| 12 | 64 | 0x3504fe20 | always | area added/changed/removed (0x35041a30, slot 84, area SetParams): +16 area, +40.. bbox |
| 13 | 48 | 0x35073470 | 1 / 2 | 0x35053a80 (part/joint id change) |
| 14 | 48 | 0x35096de0 | always | only via slot 103 (external) |

**PumpLoggedEvents** slot 77 0x35094fa0 (x86 0x350a2d70): takes the whole queue under lock, ++@22516; builds a per-type count map (std::map, logged if @620); for each event: types <=1 require both entities alive (+80==0), others the first; type 4 (post-step) stops when its step index is not older than current step and @420!=0, or is skipped for entities with simclass > 8*@208+1 / flag 0x10000; types 9/10 call their own callback (+40) instead of clients; otherwise calls the logged clients @21240+16*type, measuring time per handler (+56) and stopping at the first handler that returns 0 (types 9/10 inverted). Afterwards handlers over budget (@536) are reported ("PhysEventHandler(%d,%d)" via 0x35081ef0), events are returned to free lists, deleted entities referenced by remaining events get their deletion counter bumped, RWI hit pool tail @21720 reset, function-profile entries aged (dropped after 20 pumps). Slot 79 discards the queue.

---

## 6. Queries

**SRWIParams** (96 B, as built by slot 104): +0 pForeignData, +8 iForeignData, +16 OnEvent callback, +24 org Vec3, +36 dir Vec3, +48 objtypes, +52 flags, +56 hits*, +64 nMaxHits, +72 phitLast (cache: ent*, ipart, partid), +80 nSkipEnts, +88 pSkipEnts.

**RayWorldIntersection** slot 42 0x351aa730 (x86 0x351c2b00), `(SRWIParams*, tag, iCaller)`:
- rejects zero dir or empty world. flags & 0x800 = queue: copied into 120-B ring @21696 (count @21712, lock @22524), skip entities stored as ids (max 5, via slot 26), hits taken from pool (@21728/@21720, 80-B hits, up to 512 per alloc); processed later by slot 43 -> event 9.
- otherwise: hits[i].dist = 1e10 init; locks per-caller @22476+4*iCaller when iCaller==4; objtypes & 0x100 -> terrain ray (slot 44) first; objtypes & water flag (@20844 = 0x200) -> water plane hit 0x351a9540 (then adds 0x20 = areas); skip entities get bit `1<<iCaller` in +64; pierceability = flags>>16 (0 -> 0x8000), flag 0x40 adds 1, flag 0x400 disables; grid traversal 0x351ad6a0 (x86 0x351c2430) over the 2D grid with per-cell callback 0x351ac640 (x86 0x351c0890): POD activation, thunk filter (simclass bit, sub-cell box, z range), particles via 0x3504e5e0, other entities per part via 0x351abc10 (part geometry ray intersect, CRayGeom). flags & 0x200 post-sorts hits (solid hit 0, pierceable hits after). Hit slot 0 = closest solid hit; dist -1 if none (then pt = org+dir).
- **Hit struct (80 B)**: +0 float dist, +8 pCollider ent*, +16 ipart, +20 partid (geom-local id replaced by part id; original kept at +64), +24 int16, +26 int16, +28 surface/material idx (geom vslot 18 +144 on the hit primitive), +32 ?, +36 Vec3 pt, +48 Vec3 n, +60 next/order index, +64 iPrim/original id, +72 ptr (pool link). (Particles/simclass 5 get partid -1.)
- debug: @228 bit 0x40 reports the ray to callback @656 (+16).

**SPWIParams** (96 B, slot 64): +0 pForeignData, +8 iForeignData, +16 OnEvent, +24 itype, +32 primitive*, +40 sweepDir Vec3, +52 entTypes, +56 contact**, +64 geomFlagsAll, +68 geomFlagsAny, +72 intersection params*, +80 nSkip, +88 pSkip. Core slot 65 0x35093100: GetEntitiesInBox (0x35084680) over the swept primitive bbox, then per part primitive-vs-geometry (0x3513a590 / 0x35141920 / 0x35144db0 / 0x351ae9e0 = geometry-library intersect/sweep). Queued variant through slot 43 -> event 10.

Entity-level queries: slots 59 CollideEntityWithBeam, 60 CollideEntityWithPrimitive, 61 RayTraceEntity, 62 CollideEntityToEntity (+ 63 variant) — each takes the per-caller lock and entity lock (+72), then calls part geometries.

---

## 7. Areas (CPhysArea)

RTTI CPhysArea derives from CPhysicalPlaceholder; vtable x64 0x351ef598 (38) / x86 0x351fb658. Size 0x228 (552). Ctor 0x35042150 (x86 0x3504ba10). GetType returns 9. World area containers: @20928 global area (also head of the all-areas list via area+88), @20936 area count, @20940 count of "big" areas (too many grid cells; chained via +96 from the global area), @20944 deleted areas (freed in TimeStep when refcount +524==0), @20952+8*i (i<16) per-medium lists via +104 (index = area+460), @21080 count of areas with gravity or buoyancy, @21084 count of non-global areas with gravity, @21424 event-12 immediate clients. Areas are also inserted into the entity grid (0x35041a30 -> RepositionEntity) and emit event 12 on change.

### CPhysArea vtable
| slot | x64 | x86 | role |
|---|---|---|---|
| 0 | 0x35050580 | 0x35049590 | dtor (0x350415f0) |
| 1 | 0x35020a30 | 0x35049000 | GetType = 9 |
| 2 | 0x3504fd00 | 0x35049030 | AddRef (+524) |
| 3 | 0x3504fd20 | 0x35049060 | Release (+524) |
| 4,5 | 0x35037700 | 0x35048ca0/cb0 | empty |
| 6,10–23,26,27,34 | CPhysicalPlaceholder impl | | inherited placeholder behaviour |
| 7 | 0x35045b50 | 0x35053450 | **SetParams** (types 0, 10, 12, 18; others -> placeholder) |
| 8 | 0x35041740 | 0x35048790 | **GetParams** (10, 18, 12 = memcpy 80 B from +388) |
| 9 | 0x3504ee60 | 0x350581e0 | GetStatus (type 1 pos: pos, bbox rel. to pos, q, scale +168, simclass) |
| 24,30,31 | 0x350376d0 | | stubs |
| 25 | 0x35076030 | | return 1 |
| 28 | 0x3504fcf0 | 0x35049020 | trivial getter |
| 29 | 0x350411d0 | 0x35048860 | GetMemoryStatistics |
| 32 | 0x350411c0 | 0x35048780 | returns static object 0x35209370 |
| 33 | 0x3504fce0 | 0x35049010 | forwards to own vslot 32 (+256) |
| 35 | 0x35046d50 | 0x350495d0 | DrawHelperInformation (renderer +16) (inferred) |
| 36 | 0x3504fa60 | 0x35059090 | area extent/length helper (spline mode 2) (inferred) |
| 37 | 0x3504e750 | 0x35057a00 | find closest point on area border, result in world space (inferred) |

### Area struct layout (x64)
| off | meaning |
|---|---|
| +0 | vtbl |
| +8..+28 | world AABB |
| +32/+40/+42..+52/+56 | placeholder header (foreign data, grid bbox, thunk, link) |
| +64 | lock, +68 id/simclass (byte +71 = 5), +72 |
| +80 | deleted state (0 active, 2 removed) |
| +88 | next area (all-areas list, rooted at global area) |
| +96 | next "big" area |
| +104 | next in per-medium list |
| +112 | world* |
| +120 | position Vec3 |
| +132 | R 3x3 (from quaternion) |
| +168 / +172 | scale / 1/scale |
| +176 / +188 | secondary offset Vec3 / R0 3x3 (base transform pre-multiplied in SetParams type 0) |
| +224 | IGeometry* (geometry area; refcount released on replace) |
| +232 | 2D polygon data (slot 83); +256/+264/+280 per-edge arrays; +288..+296 polygon local half-extents |
| +240 | npoints |
| +244 | spline tube radius (slot 81 `r`) / polygon thickness |
| +272 | per-point Vec3 array (polygon) |
| +304 | point array (12 B each) for spline/polygon |
| +312..+320 | Vec3 (1e10 = unset) |
| +324 | ? |
| +388..+467 | **buoyancy params block (80 B, = param type 12 struct, copied verbatim)**: +388 type (=12), +392 water density (def. 1000 when plane first set), +396 (unset), +400 damping-like (def 0), +404 water resistance (def 1000), +408 (unset), +412 water flow Vec3 (def 0), +424 (unset), +428 water plane normal (def (0,0,1)), +440 water plane origin (def 0), +452/+456 (unset), +460 iMedium (0x80000000 unset; 0..15 = medium list), +464 (unset) |
| +468 | gravity Vec3 (unset = no gravity; global area gets world gravity) |
| +480/+484/+488 | ellipsoid size (falloff extents), +492/+496/+500 their inverses |
| +504 | uniform mode: 0 radial/falloff, 1 uniform, 2 (axis/cylindrical) (def 1) |
| +508 / +512 | falloff0 (def 0.8 for spline areas) / 1/(1-falloff0) |
| +516 | damping (velocity drag in gravity calc; def 1.0 on spline areas) |
| +520 | bUseCallback -> event 11 instead of internal evaluation |
| +524 | refcount |
| +528..+536 | Vec3 centre (param 18 +56) |
| +540 | radius (param 18 +68, def -1); >0 builds a 12-point circle (30° steps, z = +448) @+544 |
| +544 | circle point array (0x90 B) |

### Area SetParams param layouts (from slot 7 / 8)
- type 0 (pos): +4 pos, +16 quat, +32 scale, +40 Matrix34*, +48 Matrix33*. Recomputes world bbox from geometry (+224), polygon (+232) or point list (+304, inflated by +244), re-registers in grid, event 12.
- type 10: +16 float -> damping (+516), +20 Vec3 gravity -> +468 (and world gravity if global).
- type 18 (area params): +4 gravity, +16 size Vec3 (-> +480.., inverses), +28 falloff0, +32 int uniform, +36 int bUseCallback, +40 damping, +48 IGeometry* (-1 = keep), +56 centre Vec3, +68 radius.
- type 12 (buoyancy): +4 density, +12 ?, +16 resistance, +24 flow Vec3, +40 plane n, +52 plane origin, +72 iMedium (moves area between medium lists). On the global area it also refreshes world @20844 (water present -> 0x200 objtype for RWI).

### Area creation (world slots)
- slot 80 0x3504a1a0: global area, bbox (-1e10..1e10), gravity from vars; emits event 12.
- slot 81 0x35049c60: spline tube through `npt` points, radius r (+244), pos/q/scale; bbox of transformed points inflated by r; defaults falloff0 0.8 (+508), damping 1.0 (+516).
- slot 82 0x35049790: geometry area (+224), bbox from geometry bbox transformed.
- slot 83 0x35047c80: 2D polygon area with zmin/zmax and optional tessellation (0x35122fa0/0x35113200 geometry helpers).
- slot 84: removal; slot 85: iteration; slot 86: validity test.

### CheckAreas (slot 87 0x3504e000, x86 0x35052b90)
`(pt, Vec3* gravity, buoy* pb, nMaxBuoys, iMedium, Vec3* vel, ent* pent, float r, iCaller, area** pAreas, nMaxAreas)`, returns count (or -1 if none: `n | ((n-1)>>31)`). gravity initialised to unset. If pent bbox is given and r==0, r := half-extent. Always evaluates the global area first (0x3504a2e0). Then, if iMedium >= 16 (all media): if every area is "big" (@20940==@20936) walk the big-area list, else GetEntitiesInBox(objtypes 32 = areas, iCaller) around pt±r; if 0<=iMedium<16 walk the per-medium list @20952[iMedium] under the areas read lock. Each candidate: not deleted, bbox test (0x35038180), inside test 0x3504c770 (x86 0x35050f30), then 0x3504a2e0 (x86 0x3504ef50) per-area evaluation:
- point transformed to area local (inverse R, 1/scale).
- buoyancy: if plane set (and not the global area when pent has flag 0x20000), copy +388..+467 (80 B) into pb[n] (skipping a duplicate medium already reported).
- spline areas (+304 and mode 0): closest param on spline (0x35043350); direction = blend of tangent and radial normal with weight `(1-d/r)^2*(1-falloff0)+falloff0`; gravity += R*(dir*|g|) - vel*damping; flow in pb := dir * flow magnitude.
- ellipsoid/size areas: metric `sum((local*invSize)^2)`; <1 inside; falloff from +508/+512; mode 0 radial, 1 uniform, 2 special.
- geometry areas (+224): geometry point-inside / water surface (geom type 2 path with height lookup via geom vslot +232; +528/+540 circle limits).
- bUseCallback (+520): builds event 11 (176 B) with point, closest point/normal and buoyancy copy and lets clients fill gravity/buoyancy; result read back.
- areas found are appended to pAreas (up to nMaxAreas).

---

## Uncertain / to verify
- Exact role of many vars entries (marked "?"); only a subset is read via a recognisable `world->vars` pattern.
- Event type names are inferred from emitting sites; field layouts beyond the common header not mapped.
- Slot 63 (x64-only) vs 62, slot 53, slot 97, x86 slot 49: purpose tentative.
- ~~Simclass list numbering (@664+8k)~~ **confirmed by the oracle**: k = simclass (0 static, 1 sleeping rigid, 2 active rigid, 3 living, 4 independent; particles also on @760). @728.. markers still unconfirmed.
- RWI flag 0x200 semantics (post-sort) and hit fields +24/+26/+32.
