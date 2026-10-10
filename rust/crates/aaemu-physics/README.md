# aaemu-physics

A Rust port of **CryPhysics**, the physics engine inside the ArcheAge zone server
(`cryphysics_dedicate.dll`, x64, 10.0.2.13). It is the same engine generation as the 1.2 client's
`cryphysics.dll` (see [`docs/physics/CRYPHYSICS-MAP.md`](../../docs/physics/CRYPHYSICS-MAP.md) §2).
It will replace the Jitter2-subset ship physics in `aaemu-game/src/systems/ai/physics/`.

Status: **P0 (math, primitives, parameter interface, world defaults) and PT (oracle tooling)
are done.** Everything else is a stub with its owner, sources and addresses noted in the module
docs.

## Fidelity policy

The server must agree with what the client simulates, so the port matches the original **bit
for bit** wherever that is feasible:

* **Same precision per operation.** The x64 build is scalar SSE, but MSVC promotes many
  expressions to double (`cvtps2pd … mulsd … cvtsd2ss`; the binary has ~8,600 `mulsd`). Where
  Hex-Rays shows a `double` temporary, read the disassembly and evaluate that step in `f64`
  (example: `Mat33::from_quat`, all products and `1 −` terms in double).
* **Same operation order.** Float addition is not associative. Copy the association of the
  decompile (example: `Quat::len2_xwyz` sums `((x² + w²) + y²) + z²`, not `x²+y²+z²+w²`;
  `Mat33::sandwich_diag` gives `[i][j]` and `[j][i]` different roundings).
* **Same idioms.** `min`/`max` are `((a+b) ∓ |a−b|)·0.5` (`math::min_idiom`/`max_idiom`), signs
  come from the sign bit (`isneg`, `sgnnz`; `-0.0` counts as negative), rounding uses the
  `+12582912.0` trick (`round_magic`), quotient comparisons add `(a.x−b.x)·1e-20`
  (`math::Quotient`).
* **Same constants**, taken from the binary (often as raw bits: `f32::from_bits(…)`).
* **Same CRT.** The engine calls MSVCR100's math functions, which are not correctly rounded.
  `math::crt::sinf`/`cosf` are bit-exact ports of MSVCR100's algorithm; the others are double
  evaluations with known mismatch rates (asserted in `tests/math_oracle.rs`): `asinf` ≈7 %,
  `acosf` ≈2 %, double `sin`/`cos`/`asin` ≈1 %, `exp` ≈0.4 %; `tanf`, `atanf`, `atan2f`, `powf`,
  `expf`, `logf`, `log`, `atan2` matched on all samples. **Open item:** port `asinf`/`acosf` and
  the double functions if a phase hits them on a hot path.
* **Bugs are kept.** Where the original looks wrong, match it and document it at the call site
  (e.g. buoyancy uses a hard-coded g = 9.81 while gravity is 9.8; the flow-drag branch has no
  `dt`; the heightfield `SetData` ignores the incoming basis/origin).
* **Determinism over address order.** The original sorts solver bodies by heap address and steps
  islands on worker threads. The port runs single-threaded and sorts by a stable id; with one
  thread the oracle is itself bit-reproducible, so the remaining divergence is confined to
  solver ordering inside multi-body islands.

**Licensing.** Everything is derived from the shipped binaries (decompiles under
`D:\aa-re\cryphysics\`, disassembly). The published CRYENGINE source is never used or consulted.
The reference binaries (`D:\ArcheAge Returns 10.0.2.13 …\`, `D:\AA1.2`) are read-only.

## Architecture

```
src/
  lib.rs
  math/        P0  Vec3/Vec3d, Quat, Mat33, Quotient, Aabb, scalar idioms, crt (MSVCR100 math)
  sentinel.rs  P0  "unused" sentinels (0xFFBFFFFF floats, i32::MIN ints, -1 pointers), RawPtr
  ids.rs       P0  geometry / BV / entity / simclass / event ids, flag bits, struct-size tables
  params/      P0  pe_params / pe_status / pe_action / pe_geomparams with x64 layouts
  physvars.rs  P0  the world's 528-byte physics-vars block with the exact ctor defaults
  geometry/
    mod.rs         P0  Geometry trait (by vtable slot), PhysGeometry, GeomWorldData, PhysProperties
    primitives.rs  P0  box/sphere/cylinder/capsule/ray/plane/triangle/heightfield/mesh data
    shapes.rs      P1a primitive geometry objects
    bvtree.rs      P1b BV trees (BvTree trait)
    trimesh.rs     P1c CTriMesh
    heightfield.rs P1c CHeightfield + CHeightfieldBV
  collision/
    contact.rs     P0  geom_contact, prim_inters, intersection_params, unproj_params/result
    pairs/         P2a OVERLAP/INTERSECT tables (mod.rs P0) + 4 group files
    swept.rs       P2b UNPROJECT tables, RegisterIntersection, sweeps
  world/       P3  world core, grid, areas, queries, events
  entities/    P4a base + rigid (+ buoyancy); P5 living, wheeled, particle
  solver/      P4b contact solver
  trace.rs     PT  oracle trace parser
  oracle.rs    PT  oracle runner / golden-file helper
oracle/        PT  physoracle.c, tbbstub.c, build.bat
tests/
  golden/      oracle traces (*.trace) — the reference data
  scenarios/   oracle scenarios (*.scn)
  math_oracle.rs, golden_traces.rs, experiments.rs, params_oracle.rs
```

The crate has no dependencies. Every public engine type that crosses a phase boundary is
defined in a P0 file.

## The oracle

`oracle/physoracle.c` loads the real x64 `cryphysics_dedicate.dll` standalone (no game, no
`ISystem`), drives it through its vtables and prints a trace in which every float is written as
its raw IEEE bits. With one physics thread (the default) runs are bit-identical.

### Build and run

```
oracle\build.bat            # MSVC 2022 x64 → D:\aaemu-target\physics\oracle\physoracle.exe
                            # (AAEMU_PHYS_ORACLE_OUT overrides the output directory)
physoracle.exe [--rt DIR] tests\scenarios\box_on_ground.scn
physoracle.exe [--rt DIR] --math sinf 1000 6
```

The runtime directory (`--rt`, `%AAEMU_PHYS_RT%`, default `D:\aa-re\cryphysics\harness\rt`)
must contain `cryphysics_dedicate.dll`, `MSVCR100.dll`, `MSVCP100.dll`, `tbb.dll`, `xldiag.dll`,
the **original** `xlcommon.dll` (the r575 one is a forwarding proxy that does not load outside
the game folder) and the `tbbmalloc_proxy.dll` stub built from `tbbstub.c`. Binaries are never
committed.

### Tests and goldens

| env | effect |
|---|---|
| none | tests read `tests/golden/*.trace` (CI without the DLL) |
| `AAEMU_PHYS_ORACLE=<physoracle.exe>` | every golden is regenerated from the DLL and must match byte for byte |
| `AAEMU_PHYS_BLESS=1` (+ oracle) | mismatching / missing goldens are rewritten |

Helpers for later phases: `oracle::scenario_trace("name")` → [`trace::Trace`] (steps → per-entity
pos/q/v/w/simclass/awake as bits, rays → hits), `oracle::math_trace(name, count, seed)`,
`oracle::run_scenario_text(text)` (ad hoc, oracle only). Add scenarios as
`tests/scenarios/<phase>_<name>.scn`, generate the golden with the oracle (or `BLESS`) and diff
your port's output against it.

### Scenario language

One command per line, `key=value` arguments, `#` comments. Vectors are comma separated; floats
accept decimals, `U` (the unused sentinel) or `h:XXXXXXXX` (raw bits); ints accept `0x…` and `U`
(0x80000000).

| command | arguments |
|---|---|
| `grid` | `axisz=2 org=x,y,z n=nx,ny step=sx,sy pod=0 cyclic=0` (SetupEntityGrid; keep `org.z` below all geometry) |
| `var` | `off=<world offset 104..632> f=… \| i=…` (physics vars) |
| `env` | `raymax=N` (RWI ray-extent limit, default 1000), `b289=0\|1`, `b290=0\|1` (fake gEnv bytes) |
| `geom` | `name= type=box size=hx,hy,hz [center=] [basis=9 floats]` · `type=sphere r=` · `type=cylinder\|capsule r= hh= [axis=]` · `type=ray org= dir=` · `type=heightfield n=nx,ny step=sx,sy hscale= fn=flat\|slope\|bumps h0= kx= ky= amp= [worldhf=1]` |
| `entity` | `name= type=static\|rigid\|wheeled\|living\|particle\|N pos= [q=x,y,z,w] [id=]` + field setters on pe_params_pos |
| `part` | `ent= geom= [mass=] [density=] [pos=] [q=] [scale=] [flags=0x1809F] [collider=0x1] [mincontact=] [id=]` |
| `area` | `name= kind=global` (AddGlobalArea) |
| `params` / `action` / `status` / `getparams` | `target= type=N [tmpl=auto\|none] [fields] [dump=off,nwords]`; fields: `f<off>= i<off>= u<off>= b<off>= v<off>=x,y,z q<off>=x,y,z,w p<off>=null\|unused\|ent:NAME\|geom:NAME hex=<bytes>` |
| `trace` | `ents=a,b,…` (entities printed after each step) |
| `step` | `n= [dt=h:3d088889] [flags=0x9F] [every=1]` (TimeStep; `every=0` prints nothing) |
| `print` | print the traced entities now |
| `geom … type=mesh` | (P1c) `name=` + one source: `file=PATH` (relative to the scenario: u32 nVerts, u32 nTris, f32 verts[3·nV], i32 idx[3·nT], LE; ≤ 65536 verts) · `gen=grid nx= ny= step=s\|sx,sy [z=] [noise=seed,amp]` · `gen=terrainmesh nx= ny= step= amp= [k=kx,ky] [z=]` · `gen=box size=` · `gen=icosphere r= [sub=1]` · `gen=cylinder r= hh= [seg=16]` · `cgf=REL [slot=k]` (k-th 0xCCCC0018 chunk in chunk-table order of `%AAEMU_PHYS_CGF_DIR%\REL`, through LoadPhysGeometry; blobs that use the render mesh's vertices are refused). file/gen options: `[flags=0x40] [mintris=2] [maxtris=4] [obb=1] [tol=0] [mat=id] [scale=] [offset=x,y,z]`; any source: `[dump=1]`. Prints `geominfo` (+ `cgfload NAME used N of LEN` for cgf) |
| `geominfo` | (P1c) `name=` — `geominfo NAME type T nprims N nv N flags F bbox <GetBBox: Basis 9, bOriented, center 3, size 3> vol <GetVolume> pg <V> <Ibody 3> <q 4> <origin 3> <surface>` for any registered geometry |
| `ray` | `name= org= dir= [types=0x13F] [flags=0] [max=1] [caller=4] [ext=1]` (RayWorldIntersection; `ext=1` (P1c) appends `x <i16 +24> <i16 +26> <+32> <+60> <iPrim +64>` to each hit line) |
| `peek` / `peekworld` / `peekdll` | raw words of an entity, the world, or the DLL image (`va=`) |
| `snap` / `snapdiff` | remember an object, then print the byte offsets that changed (pointer-free) |
| `listwhere` | which world entity lists hold an entity |
| `dumpvars`, `echo` | physics-vars block; echo a marker |

Math probes (`--math NAME COUNT SEED`) call routines **inside** the DLL on generated inputs:
`quat2mat` (0x350247B0), `mat2quat` (0x35023EB0), `quatrot` (0x35022240), `bodyupdate`
(0x3509CE40), `bodystep` (0x3509E180), and the MSVCR100 functions `sinf cosf tanf atanf asinf
acosf expf logf sqrtf atan2f powf sin cos exp log asin atan2`.

### Capabilities and limits

* Works: static/rigid/living/particle entities, box/sphere/cylinder/capsule geometries, global
  area with buoyancy, all SetParams/Action/GetStatus ids (raw field access), TimeStep,
  RayWorldIntersection, heightfield terrain through world slot 17, internal-function probes.
* Trimesh geometry (P1c, `oracle/p1c_mesh.c`): CreateMesh from mesh files and built-in
  generators, LoadPhysGeometry from real CGF physics blobs, on static and rigid entities. The
  generators are deterministic single-precision formulas (sin/cos as a double Taylor series),
  documented in `p1c_mesh.c` and rebuilt bit for bit in Rust by `tests/p1c_scenarios.rs`
  (`gen` module). `dump=1` prints `meshv NAME <verts>` / `mesht NAME <tris>` (stored order) /
  `meshf NAME <foreign ids>` (if any).
* Not yet: shared-render-mesh CGF blobs (341 corpus models; would need the CGF DataStream
  positions/indices), non-global areas, PrimitiveWorldIntersection, events (no event clients are registered),
  multi-threaded stepping (needs a real thread-task manager behind `gEnv`).
* Engine facts learned through the oracle (also in CRYPHYSICS-MAP): RWI returns 0 until the
  world's temp pools exist (first step with an ordinary entity); default RWI flags require part
  flag 0x8000; RWI rejects rays whose |dir.x| or |dir.y| exceeds a gEnv config integer unless
  gEnv byte 289 is set ("DrawRayOnGrid", ArcheAge-specific); a heightfield geometry on an
  ordinary entity never collides; entities join the simclass lists at the first step.
* Trimesh facts (P1c-oracle):
  * `IGeomManager` slot 3 (0x3514B710) is CreateMesh(strided vtx*, strided idx*, mats, foreignIdx,
    nTris, flags, approx_tolerance, SMeshBVParams* {minTris, maxTris, favorAABB}); slot 4
    (0x35095CB0) takes the three values as arguments (and clears flag 0x80000 = voxel); slot 5
    uses tol 0.05, 2, 4, 1.0. Strided pointers are `{ptr, int stride}`; **indices are u16**
    (copied to int; flag 2 + stride 4 uses an int array in place), nVerts = max index + 1.
  * CreateMesh first tries a primitive (slot 6 → 0x3512D650): only when tol > 0, the material
    array is absent or uniform, and the flag of the shape is set: **0x800 cylinder, 0x1000 box,
    0x2000 sphere, 0x200000 capsule** (0x203800 = all). Otherwise a CTriMesh is built.
  * Tree choice: 0x20 OBB, 0x40 AABB, 0x40000 AABB in principal axes (+152 then shows 0x40 and
    the bbox is oriented), 0x80 single box; with several bits the cheapest tree wins and only its
    bit stays in +152. The build permutes the triangles; hit `iPrim` (+64) indexes the stored order.
  * LoadPhysGeometry (slot 16, 0x3514B340) takes `(CMemStream*, strided vtx*, strided idx*,
    mats)`, returns a ready phys_geometry (no RegisterGeometry) whose V/inertia come from the
    blob header (for open meshes ≠ GetVolume); corpus blobs keep flag 0x8000.
  * RegisterGeometry's V differs from GetVolume in the last bits for curved meshes (icosphere).
    Rigid mesh parts: `mass=` is used as is, `density=` gives density·V (pe_status_dynamics +112).
  * RWI on a trimesh is **two-sided** (rays from below / inside hit, the reported normal is the
    triangle's outward normal regardless); heightfield terrain is **one-sided** (upward rays from
    below miss). Hit +24/+26 int16: the triangle's material id for corpus meshes (0 / −1 for
    meshes without materials, 0 / 0 for terrain); +60 is 1 for terrain hits, 0 for entities; +32
    unidentified; surface (+28) is −1 without a material mapping.
  * Nothing special is needed for trimesh parts: default part flags 0x1809F include 0x8000 for
    rays, and rigid trimesh bodies collide with trimesh floors, objects and terrain.

## Parallel phase: ownership

Each agent owns the files listed for its package and creates new files only inside its own
paths. Shared (P0/PT) files change only through the P0 owner; additive changes needed by a phase
(a new field on a shared struct, a new method on a shared trait with a default body) are fine
when noted in the commit message. Tests: `tests/<phase>_*.rs`, scenarios and goldens prefixed
with the phase (`tests/scenarios/p2a_box_box.scn`). Never modify another phase's goldens.

| package | owns | uses (stable) |
|---|---|---|
| **P1a** primitive geometries | `src/geometry/shapes.rs` (+ `src/geometry/shapes/`) | `geometry::{Geometry, PhysProperties, GeomWorldData}`, `geometry::primitives::*`, `math::*` |
| **P1b** BV trees | `src/geometry/bvtree.rs` (+ `src/geometry/bvtree/`) | `geometry::bvtree::BvTree` (extend additively), `geometry::primitives::{BoxPrim, Triangle}` |
| **P1c** trimesh + heightfield | `src/geometry/trimesh.rs`, `src/geometry/heightfield.rs` (+ dirs) | `Geometry`, `BvTree`, `primitives::{MeshData, HeightfieldPrim, Triangle, Plane}` |
| **P2a-1** box pairs | `src/collision/pairs/box_pairs.rs` | `pairs::{PairTables, IntersectFn, OverlapFn}`, `contact::PrimInters`, `primitives::*` |
| **P2a-2** triangle pairs | `src/collision/pairs/tri_pairs.rs` | same |
| **P2a-3** triangle vs cylinder/capsule | `src/collision/pairs/tri_round_pairs.rs` | same |
| **P2a-4** round + ray pairs | `src/collision/pairs/round_pairs.rs` | same |
| **P2b** swept / unprojection | `src/collision/swept.rs` (+ `src/collision/swept/`) | `contact::{UnprojParams, UnprojResult, GeomContact, IntersectionParams, ContactArea}`, `PairTables` |
| **P3** world core, areas, queries | `src/world/**` | `physvars::PhysVars`, `ids::*`, `params::*`, `entities::EntityId`, `Geometry`, `PairTables` |
| **P4a** base + rigid + buoyancy | `src/entities/base.rs`, `src/entities/rigid.rs` (+ `src/entities/rigid/`) | `params::*`, `PhysGeometry`, `GeomWorldData`, `math::crt`, `Geometry::calculate_buoyancy` / `calculate_medium_resistance` |
| **P4b** contact solver | `src/solver/**` | the contact record P4a defines in `entities/rigid` (agree on it first), `PhysVars` solver fields |
| **P5** living / wheeled / particle | `src/entities/living.rs`, `wheeled.rs`, `particle.rs` | P4a's base/rigid API, `params::*` |
| **P0 / PT** (this package) | `src/math/**`, `src/sentinel.rs`, `src/ids.rs`, `src/params/**`, `src/physvars.rs`, `src/geometry/{mod,primitives}.rs`, `src/collision/{mod,contact}.rs`, `src/collision/pairs/mod.rs`, `src/entities/mod.rs`, `src/{trace,oracle}.rs`, `oracle/**`, `src/lib.rs`, `Cargo.toml` | — |

Shared interfaces that must stay stable: everything in `math`, `sentinel`, `ids`, `params`,
`physvars`; `geometry::{Geometry, PhysGeometry, GeomWorldData, PhysProperties}` and
`geometry::primitives::*`; `geometry::bvtree::BvTree`; `collision::contact::*`;
`collision::pairs::{PairTables, IntersectFn, OverlapFn}` (each group fills its cells in its own
`fill`); `entities::EntityId`; `trace::*` and `oracle::*`.

Dependency order (from CRYPHYSICS-MAP §10): P1a ‖ P1b ‖ PT → P1c → (P2a ×4 ‖ P2b) → P3 → (P4a ‖ P4b)
→ P5. P4a and P4b must agree on the contact record (176 bytes, `dynamics.md` §3.2) before
starting; P3 and P4a share the entity store and should be one agent or tightly coordinated.
