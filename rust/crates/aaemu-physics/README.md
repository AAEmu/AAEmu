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
  entities/    P4a base + rigid (+ buoyancy); P5 living, wheeled, particle;
               interface/ = SetParams / GetParams / GetStatus / Action (slots 7–10) by type id
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
| `surface` | `idx= [bounce=0] [friction=h:3f99999a] [flags=0]` (SetSurfaceParameters, world slot 29) |
| `area` | `name= kind=global` (AddGlobalArea) |
| `params` / `action` / `status` / `getparams` | `target= type=N [tmpl=auto\|none] [ts=1] [fields] [dump=off,nwords]` (`ts` = bThreadSafe of SetParams / Action; `<= 0` may defer the call to the next TimeStep); fields: `f<off>= i<off>= u<off>= b<off>= v<off>=x,y,z q<off>=x,y,z,w p<off>=null\|unused\|ent:NAME\|geom:NAME hex=<bytes>` |
| `trace` | `ents=a,b,…` (entities printed after each step) |
| `step` | `n= [dt=h:3d088889] [flags=0x9F] [every=1]` (TimeStep; `every=0` prints nothing) |
| `print` | print the traced entities now |
| `ray` | `name= org= dir= [types=0x13F] [flags=0] [max=1] [caller=4]` (RayWorldIntersection) |
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
* Not yet: trimesh geometry (needs `CreateMesh`, add a `geom type=mesh` command when P1c starts),
  non-global areas, PrimitiveWorldIntersection, events (no event clients are registered),
  multi-threaded stepping (needs a real thread-task manager behind `gEnv`).
* Engine facts learned through the oracle (also in CRYPHYSICS-MAP): RWI returns 0 until the
  world's temp pools exist (first step with an ordinary entity); default RWI flags require part
  flag 0x8000; RWI rejects rays whose |dir.x| or |dir.y| exceeds a gEnv config integer unless
  gEnv byte 289 is set ("DrawRayOnGrid", ArcheAge-specific); a heightfield geometry on an
  ordinary entity never collides; entities join the simclass lists at the first step.

## Dynamics port status (P3 world, P4a rigid, P4b solver)

* `World::time_step` (0x3508BD10) runs the rigid section exactly as the binary does with one
  physics thread: island building, per-island GetMaxTimeStep/Step/StepBack (pass 0), the
  awake-list job, RegisterContacts → `InvokeContactSolver` → post-solve/Update (pass 1), the
  cleanup. Living/independent entities (P5) and worker threads are not ported.
* `CRigidEntity::Step` with CheckForNewContacts, CheckContactConflicts, RegisterContactPoint,
  the contact-patch fit, CCD sweep + beam check, ray-mode (`0x100000`) parts with their spring
  contacts, buoyancy and air resistance; Update's sleep logic.
* Port-side scenario interpreter: `scenario::Scenario::run(text)` prints the oracle's text for a
  scenario (`cargo run --example replay <file.scn>`); `tests/p7_dyn_goldens.rs` replays the
  stepping goldens bit for bit. Debug aids: the oracle command `contacts target=E` (the entity's
  contact records) with the same command in the interpreter, `peek` of rigid members
  (`scenario::entity_word`), and `AAEMU_PHYS_DUMP_SOLVER=<file>` to dump every island solve as a
  `physoracle --p4b` case (`cargo run --example solver_check <file> <physoracle.exe>`).
* Convenience API for the server: `world::api` (`World::with_grid`, `geometry`, `add_static`,
  `add_rigid`, `set_ocean`, `set_body_buoyancy`, `step`, `body_state`), plus the ported
  `rwi`/`pwi`, `set_heightfield_data`, `add_event_client`/`pump_logged_events`.

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
