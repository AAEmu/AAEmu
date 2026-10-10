# CryPhysics dynamics: rigid bodies, contact solver, buoyancy (draft)

Source: Hex-Rays decompiles of `cryphysics_dedicate.dll` x64 10.x (image base 0x35000000, has RTTI) and the
x86 1.2 client dump (base 0x34ff0000). Everything below comes from those decompiles. Names come from RTTI
or strings; anything marked *(inferred)* is a name chosen from behaviour. "as read" means the formula is
copied from the binary as-is, including anything that looks like a bug. Offsets are byte offsets into the x64
object. x86 1.2 struct offsets are different (32-bit pointers) and are not listed here.

Notation: `E` = CRigidEntity object, `B = E+744` = the embedded rigid body, `W` = CPhysicalWorld (`*(E+120)`),
`tid` = physics thread index (TLS), `dt` = substep. Quaternions are stored `(x,y,z,w)` with `w` last.

---------------------------------------------------------------------------------------------------
## 0. Function map (dynamics)

| role | x64 | x86 1.2 | notes |
|---|---|---|---|
| CRigidEntity vtable (83 slots) | 0x351f14c8 | — | ctor 0x350acbc0 (object 0x4B0) |
| **Step(dt)** = vf34 | 0x350c9df0 | 0x350d84a0 (not decompiled; label "CRigidEntity::Step") | integrate, collide, register contacts, apply forces |
| StartStep(dt) = vf26 | 0x350ccb20 | — | `E+596 = dt (timeStepFull); E+600 = 0 (timeStepPerformed)` |
| GetMaxTimeStep(dt) = vf37 | 0x350bcaa0 | 0x350c19a0 | CCD-limited substep |
| GetLastTimeStep = vf38 | 0x35020a40 | | returns `E+604` |
| RegisterContacts = vf40 | 0x350bdba0 | 0x350cf490 (named) | fills the solver buffers, calls AllocSolverTmpBuf 0x3509a9c0 |
| **Update(dt, damping)** = vf41 | 0x350beb40 | 0x350c3460 | post-solver: damping, sleep test |
| GetEnergy = vf42 | 0x350ab8f0 | | summed per island |
| GetDamping(dt) = vf43 | 0x350ac490 | 0x350b8a40 | |
| vf44 | 0x35020a50 | | returns `E+704` (param `+80`, default 100) |
| GetRigidBody(ipart) = vf64 (vt+512) | — | | returns `&E+744` for a rigid entity |
| Action = vf10 | 0x350c5d30 | 0x350c6950 (named) | action_impulse at vt+80 |
| SetParams = vf7 | 0x350c4cc0 | 0x350d4920 | pe_params type 10 = simulation, type 12 = buoyancy |
| DelayedIntersect = vf60 | 0x350bb8c0 | 0x350d7590 (named) | |
| GetPotentialColliders = vf81 | 0x350b2b90 | 0x350cad80 (named) | |
| CheckForNewContacts | 0x350b3440 | 0x350cb650 (named) | "rb_intersect" |
| CheckContactConflicts | 0x350b9880 | 0x350c0a00 (named) | |
| RegisterContactPoint | 0x350b5e10 | 0x350d5a40 (named) | |
| **ApplyBuoyancy** *(sub)* | 0x350c0e50 | 0x350d1d80 (named) | §4 |
| ApplyAirResistance *(sub)* | 0x350c2790 | label "CRigidEntity::ApplyAirResistance" | §4.6 |
| gather water/medium areas *(inferred)* | 0x350c4880 | | calls world vf87 CheckAreas 0x3504e000 |
| velocity clamp *(inferred)* | 0x350b9440 | | §2.4 |
| commit new pose *(inferred, x86 "UpdatePosition")* | 0x350ac080 | 0x350bab60 | copies `*(E+200)` → `E+408/420`, part poses, BBox |
| body UpdateState *(inferred)* | 0x3509ce40 | | `Iinv = R·diag(Ibody_inv)·Rᵀ; v=P·Minv; w=Iinv·L` |
| body Step(dt) *(inferred)* | 0x3509e180 | 0x350a5af0 | §2.2 |
| body ctor/zero *(inferred)* | 0x35098ea0 | | |
| quat → 3x3 | 0x350247b0 | | row-major |
| quat · vec3 | 0x35022240 | | `v' = v + 2·qv×(qv×v + w·v)` |
| island step (per group) *(inferred)* | 0x3507f9b0 | | phase 0: Step all; phase 1: RegisterContacts + solver + Update |
| island post-solve *(inferred)* | 0x3507f210 | | computes group damping, calls Update |
| **InvokeContactSolver** | 0x3509e850 | 0x350a62b0 (labels "InvokeContactSolver","PreCG","LCPCG","LCPCG-unproj","LCPMC") | §3 |
| World TimeStep = world vf32 | 0x3508bd10 | 0x3509f6a0 | |
| geometry CalculateBuoyancy = IGeometry slot 13 (vt+104) | CTriMesh 0x351da620, CBoxGeom 0x3513b1b0 (box→mesh), CSphere 0x351aec00 | CTriMesh 0x351ef500 | §4.3 |
| geometry CalculateMediumResistance = slot 14 (vt+112) | CTriMesh 0x351cd0c0 → per-polygon 0x35133e50, CBoxGeom 0x3513e7c0 | 0x351d7250 / 0x35145950 | §4.4 |
| polygon-vs-plane clip *(inferred)* | 0x35133970 | | keeps points with `n·x < d` |

---------------------------------------------------------------------------------------------------
## 1. State layout

### 1.1 Embedded rigid body `B = E+744` (struct size ≈ 0xD4)

| B+ | E+ | type | meaning | evidence |
|---|---|---|---|---|
| 0 | 744 | Vec3 | pos: centre of mass, world | body Step `pos += v·dt` |
| 12 | 756 | Quat | q: principal-axes orientation (x,y,z,w) | integrated by w |
| 28 | 772 | Vec3 | P: linear momentum | `v = P·Minv` |
| 40 | 784 | Vec3 | L: angular momentum | `w = Iinv·L` |
| 52 | 796 | Vec3 | w: angular velocity | |
| 64 | 808 | Vec3 | v: linear velocity | |
| 76 | 820 | f32 | M: mass | `P = v·M` |
| 80 | 824 | f32 | Minv (0 means static/infinite) | |
| 84 | 828 | f32 | V: volume | buoyancy "floats" test `ρ·k·V > M` |
| 88 | 832 | Vec3 | Ibody: principal inertia (diagonal) | |
| 100 | 844 | Vec3 | Ibody_inv | |
| 112 | 856 | Quat | qfb: body→entity frame rotation *(inferred)* | `q_ent = q ⊗ qfb` |
| 128 | 872 | Vec3 | offsfb: entity origin → CoM offset in entity frame *(inferred)* | `pos_ent = pos − q_ent·offsfb` |
| 140 | 884 | Mat33 | Iinv (world, row-major) | |
| 176 | 920 | Vec3 | accumulated contact impulse *(inferred)* | read at the start of Step, zeroed at the end |
| 188 | 932 | Vec3 | accumulated contact angular impulse *(inferred)* | same |
| 200 | 944 | ptr | | |
| 208 | 952 | f32 | extra energy term *(inferred)* | added to E_kin in Update; `>0` forces a pose resync |

### 1.2 CRigidEntity fields used by dynamics

| E+ | meaning | default (ctor 0x350acbc0) | set by |
|---|---|---|---|
| 8..28 | BBox min/max (committed) | | |
| 92 | entity flags (pef_*-like). bit0 "keep awake"-like (damping ≥0.9, energy cap); bit2 no splash; bit3 apply air resistance; 0x800000 ignore areas | | |
| 120 | `CPhysicalWorld*` | | |
| 200 | `ptr → new coords {pos, q}` (= &E+448 *(inferred)*) | | |
| 208/216 | collider entity array / nColliders | | |
| 280/288 | parts array (stride 136) / nParts | | |
| 408 / 420 | committed pos / q of entity | | |
| 448 / 460 | new (stepped) pos / q | | Step |
| 476..496 | new BBox | | |
| 504 | flags2: 0x1, 0x800 *(post-step zeroing)*, 0x20000 no CCD, 0x4000000 **floats**, 0x8000000 (sim param +64), 0x10000000 | | |
| 508 | byte0 **awake**; bits 8..12 sleep frame counter; bits 13..16 contact-state counter; bits 17..21, 22..26 maxLoggedCollisions | | |
| 520/528 | per-collider contact list heads / contact bitmasks | | |
| 536 | contact list (doubly linked, sentinel = E+536); contact n at +48 | | |
| 552 | nContacts | | |
| 568 | constraint array (stride 128) | | |
| 580 | sleep history bits (popcount ≥ 20 changes the energy test) | | |
| 584 | nConstraints *(inferred)* | 0 | |
| 588 / 592 | CCD: `|v·axis|` along the thinnest axis of the largest part / thickness along that axis | 0 | GetMaxTimeStep |
| 596 / 600 | timeStepFull / timeStepPerformed | 0.01 / 0 | StartStep / Step |
| 604 | last dt | 0 | Step |
| 608 | sleep-prevention timer (set to 0.2 when area gravity ≠ freefall gravity) | | |
| 612 | pending/forced dt *(inferred)* | 0 | |
| **616** | gravity (in contact) | world gravity or (0,0,−9.81) | sim param +20 |
| **628** | gravityFreefall | = gravity | sim param +36 |
| 640 | constant angular acceleration *(inferred)*: `L += I_world·α·dt` | 0 | sim param +68 |
| **652** | Emin (sleep energy) | 0.0049 | sim param +12 |
| **656** | maxTimeStep | 0.02 | sim param +8 |
| 660 / 672 | sleep accumulators Σv, Σw | | |
| **684** | damping (with contacts) | 0 | sim param +16 |
| **688** | dampingFreefall | 0.1 | sim param +32 |
| 692 | water damping, effective (recomputed every Step) | 0 | ApplyBuoyancy |
| **696** | maxw (max angular speed) | 20 | sim param +48 |
| 700 | **minFriction**: contact μ = max(μ, E+700). Default 0.1; set to 3.0 for the step after a swept (CCD) unprojection | 0.1 | |
| 704 | **maxFriction** (sim param +80). The island takes the min over its entities (vf44) and stores it in W[80·tid+21936]; RegisterContactPoint caps μ with it | 100 | |
| 708 / 720 | v / w saved before a sleep zeroing | | |
| 956 / 968 | queued impulse P / angular impulse L (from Action) | 0 | |
| 980 / 1016 | previous pos / q (start of Step) | | |
| 992 / 1004 | previous v / w (end of Step) | | |
| 1048 / 1060 | external force / torque (applied ×dt) | 0 | |
| 1072 | pending position shift (added to B.pos at the start of Step; `/dt` adds to v in GetMaxTimeStep) | 0 | |
| **1120** | waterDamping | 0 | buoy param +12 |
| **1124** | kwaterDensity (multiplier on area density) | 0.001 | buoy param +8, or `+4 / refDensity` |
| **1128** | kwaterResistance | 0.001 | buoy param +20, or `+16 / refResistance` |
| **1132** | Emin in water (sleep energy when floating and no contacts) | 1e-4 | buoy param +64 |
| **1140** | kAirResistance | 0.05 | buoy param +68 |
| 1136 | submerged fraction (0..1, water medium only) | 0 | ApplyBuoyancy |
| **1192** | water-level offset (subtracted from area plane origin.z) | 0 | buoy param +76 |

`refDensity` and `refResistance` are read from the world's default area (`W+20928`), at area+392 and area+404,
which are that area's waterDensity and waterResistance. Both default to 1000. So by default `k = param/1000`, and
the effective density/resistance used on the entity is `area.value · k`.

### 1.3 Part record (`E+280`, stride 136)

`+0 pos, +12 q, +28 scale, +32..52 BBox, +64 phys_geometry** (pgeom), +76 mass, +80 flags (0x10000 = floats,
used by buoyancy; 0x100000 special contact path; 0x400000 and 0x1800000 used by contacts), +84 flagsCollider, +120 ptr → new part coords {pos,q,…,scale@+28}`.
phys_geometry: `+0 IGeometry*`, `+48 V` (volume at unit scale). Part mass = `scale³·V·density` (SetParams type 10, param +56).

### 1.4 Simulation-param record (SetParams type 10) → entity

`+4 iSimClass, +8 maxTimeStep→656, +12 minEnergy→652, +16 damping→684, +20 gravity→616 (also 628 if +36 unset),
+32 dampingFreefall→688, +36 gravityFreefall→628, +48 maxRotVel→696, +52 mass (rescales part masses),
+56 density (recomputes part masses), +60 maxLoggedCollisions, +64 bit→E+504 0x8000000, +68 Vec3→640, +80→704`.
Fields that hold NaN with bits `0xFFA00000` count as "unset". The engine's "unused" sentinel is `-4194305` (0xFFBFFFFF).

### 1.5 Buoyancy record (pe-params type 12, 80 bytes). The same layout is used on areas and in SetParams

| off | field | use in ApplyBuoyancy |
|---|---|---|
| 0 | type = 12 | |
| 4 | waterDensity ρ | buoyancy, flow drag |
| 8 | kwaterDensity | (entity side) |
| 12 | waterDamping | damping blend |
| 16 | waterResistance | medium resistance |
| 20 | kwaterResistance | (entity side) |
| 24 | waterFlow (Vec3) | relative velocity |
| 36 | flowVariance *(inferred)* | |
| 40 | waterPlane.n (Vec3) | |
| 52 | waterPlane.origin (Vec3) | |
| 64 | waterEmin (entity) | |
| 68 | kAirResistance (entity) | |
| 72 | iMedium (0 = water, 1 = air) | records with iMedium ≥ 2 are ignored |
| 76 | water-level offset (entity) | |

Areas keep a copy of this record at area+388..+467. For spline/flow areas (area+304) the flow direction is
computed along the spline (0x3504a2e0). An area with a callback flag (area+520) raises an event (type 11) so
the game can override the record. **For ArcheAge this is a likely place where water level and waves come from.**

---------------------------------------------------------------------------------------------------
## 2. Per-substep sequence

### 2.1 World / island driver (summary; the world-object agent covers the details)

World TimeStep 0x3508bd10:
1. `dt = min(dt, W+224)`. If `W+220 > 0`, quantise: `dt = round(dt/fixed)·fixed`, using the float trick `+12582912.0` and an accumulator at W+1688. If `W+216 > 0`, force `dt = W+216`.
2. Every awake entity gets `StartStep(dt)` (vt+208, vf26).
3. Islands (groups) are built from the contact graph and linked through entity+144. Per-island dt goes in W+824[i] and is clamped by `W+22420 = 1.01·min island dt`.
4. Loop: up to `W+276` iterations. Each iteration has two passes over the islands (0x3507f9b0, possibly on worker threads):
   - **pass 0:** `dt_i = min_e GetMaxTimeStep(e)` (vf37). Then `Step(dt_i)` (vf34) on every entity in the island.
   - **pass 1:** for each entity, `RegisterContacts` (vf40/vf39), then `InvokeContactSolver` (0x3509e850), then 0x3507f210 → `Update(dt, damping)` (vf41) on each entity.
   - It stops when every entity reports `performed ≥ full − 0.001`.

Damping passed to Update (0x3507f210):
```
damp_group = (nEntitiesInIsland > W+296) ? 1 − dt·W[+300] : 1      // group damping (as read: a "size−1 > n" test)
damp       = min(damp_group, min_e GetDamping_e(dt))
GetDamping(dt) (vf43): d = (nColliders ? E.damping[684] : E.dampingFreefall[688]);
                       d = max(d, E.waterDampEff[692]);   return max(0, 1 − d·dt)
```

### 2.2 Body integration `0x3509e180 body.Step(dt)`

```
UpdateState()                                   // 0x3509ce40
pos += v·dt
if |w|²·dt² ≥ 9e-6:                             // exact rotation
    a = |w|·dt/2;  dq = (ŵ·sin a, cos a)
    q = dq ⊗ q                                  // SSE quaternion product (world-frame ω)
else:                                           // first order, as read: w-term updated first
    q.w -= (w·q.xyz)·dt/2
    q.xyz += dt/2 · (w × q.xyz + w·q.w_new)
q = q / |q|                                     // 1/fsqrt (sqrtss + divss, not rsqrtss)
if Minv > 0:
    R = mat(q);  Iinv = R·diag(Ibody_inv)·Rᵀ;  w = Iinv·L
```
UpdateState is `Iinv = R·diag(Ibody_inv)·Rᵀ`. If `Minv>0` it also does `v = P·Minv; w = Iinv·L`.
There is **no gyroscopic term**: L is conserved and w is recomputed from the rotated inertia, which is the
standard momentum-based integration. v is not changed by Step. Forces arrive through P/L before the next UpdateState.

### 2.3 `CRigidEntity::Step(dt)` vf34 0x350c9df0 (in order)

1. Call vf52 (vt+416) on each collider entity. Colliders whose simclass is 7 are put on a deferred list and get vt+384.
2. Save `prevPos(980) = B.pos`, `prevQ(1016) = B.q`. Clear 708..736 and `E+692 = 0` (water damping is recomputed every step).
3. `if performed(600) > full(596) − 0.001 or nParts == 0 → return 1` (nothing to do).
4. `E+700 = 0.1; performed += dt; E+604 = dt`. If `E+612` is set, use it as dt (pending dt) and clear it.
5. **Asleep** (`(byte)E+508 == 0`): skip integration, recompute BBox (vt+624), goto 12.
6. Clear 1048..1068 (external force and torque). Call `body.Step(dt)`. Then `B.pos += E+1072; E+1072 = 0`. Clamp velocities (§2.4).
7. New entity pose: `q_ent(460) = B.q ⊗ qfb(856)`, normalised. `pos_ent(448) = B.pos − q_ent·offsfb(872)`.
8. **CCD / swept test.** If `(dt − f)·E[588] > 0.71·E[592]` (f = 1 if the step was flagged; GetMaxTimeStep fills 588/592):
   rerun CheckForNewContacts from the old pose (E+408/420) with sweep vector `v·dt`. If something is hit,
   move the entity back along the hit normal by `depth + W[+240]·0.5` (W+240 is a world contact gap *(inferred)*),
   recompute B.pos, and set `E+700 = 3.0`. After that, if any collider is still intersecting (world vt+488 test),
   **halve v, w, P, L** and restore the old pose. This branch also sets the search distance to `3·dt`.
9. Recompute BBox (vt+600) and commit (vt+624). Then **CheckForNewContacts** 0x350b3440 (string "rb_intersect"),
   which returns n contacts in a per-thread table (`qword_35218710[tid]`, 136-byte records: `+0 double depth`, `+8 pt`,
   `+32 n`, `+44 flags/iFeature`, `+48 vrel-ish scale`, `+104 contact-area ptr`, …).
   - If n == 0 and the swept test hit, remove the normal velocity: `v -= 1.1·(v·n)·n; P = M·v`.
   - If 0 < n ≤ 8: **CheckContactConflicts** 0x350b9880.
   - Unprojection for the deepest contact `iBest`: if `depth < 1.1·dt·rec[+48]` and no existing contact in the
     list (E+536) opposes it, move `pos(448)` and `B.pos` by `n·(depth − W[240]/2)` *(as read)*.
     The contacts are then re-detected (second CheckForNewContacts).
10. For each contact record with `depth ≥ 0`: if part flag 0x100000, take the special path 0x350badE0. Otherwise
    **RegisterContactPoint** 0x350b5e10(E, i, pt, table, partid0, partid1, matid0, matid1, flags, depth', tid, &extra).
    For area contacts (record+104 has ≥2 points) it registers one contact per polygon point that lies within the
    penetration window. `depth' = max(0.001, depth − (n·(pt−pt0)) + minDepth)`. Contact flags start at
    0x80000|0x1040040 (first) and 0x40 (rest).
11. Clear part flag 0x1000000 when 0x1800000 is set on parts in the per-thread list.
12. **Post-collision forces**, in this order:
    - 0x350adb90: re-validate existing contacts (flag 0x100000 → 0x350aa810 removes them to the world free list).
    - If `E+1184 != 0`: run the queued Action (0x350c9750).
    - World vt? 0x35080a00(W, E, 1, &BBox) re-registers E in the grid. 0x350ac080 commits the new pose.
    - `n = gatherAreas(E, dt, buoy[4], 4)` 0x350c4880 → world vf87 CheckAreas (fills up to four buoyancy records).
    - `g = nColliders ? E.gravity(616) : E.gravityFreefall(628)`
    - **ApplyBuoyancy(E, dt, g_fixed = (0,0,−9.81), buoy, n)** 0x350c0e50. **Buoyancy uses a hard-coded (0,0,−9.81)**, not E's gravity.
    - if `E+92 & 8`: **ApplyAirResistance(E, dt)** 0x350c2790.
    - `P += E[956]; L += E[968]; clear` (impulses queued by Action)
    - `P += M·g·dt`
    - `P += F_ext(1048)·dt;  L += T_ext(1060)·dt`
    - `L += (R·diag(Ibody)·Rᵀ)·α(640)·dt`
    - UpdateState; vf79 (vt+632) hook (**CWheeledVehicleEntity overrides this**; empty for rigid); clear 920..940; UpdateState; velocity clamp.
    - `prevV(992) = v; prevW(1004) = w`
13. Post-step zeroing (bit 0x800 of E+504). This needs `M > W[80·tid+21932]·0.001` (an island minimum mass value
    *(inferred)*) and "some contact had depth ≥ size/2". If `(|w|²·(size/2)² + |v|²)·dt² ≥ (0.1·size)²`, it zeroes
    P, L, v, w. size = `max(bbox dims)` *(as read; purpose unclear)*.
14. Return `!(flags & 0x800) | (contactCounter > 3)`.

**Gravity is applied after contacts are registered, and the solver runs after all Steps.** So the solver sees velocities that already include `g·dt`.

### 2.4 Velocity clamp 0x350b9440

```
if |v| > W[364]:  v = v̂·W[364]; P = M·v                    // world max linear velocity
if nContacts == 1:  limit |w·n_c| ≤ maxw (n_c = normal of the single contact)
else:               wmax = nContacts>0 ? max(maxw, 300) : maxw;  if |w| > wmax: w = ŵ·wmax
after w changes:    L = q·(Ibody ∘ (q⁻¹·w))
```

### 2.5 GetMaxTimeStep vf37 0x350bcaa0

For the part with the largest BBox volume, compute the velocity `v' = v + E[1072]/max(dt,1e-4)` in that part's
frame. Take the axis with the smallest `|v'_axis|·(face area)` heuristic (bbox dims × scale). Store `E[588] = |v'_axis|`
and `E[592] = dim_axis`. If `maxTimeStep·|v'| > 0.7·dim` and the swept box test (CBoxGeom on the stack vs
neighbours, 0x35084680 + CCylinderGeom__vf10 intersect) finds a hit, or the island flag says so:
`dt = max(0.005, 1.7·dim/|v'|)` *(as read)*.
Return `min(full − performed, maxTimeStep·(1 + 0.5·[W[80·tid+21928] && nColliders≥1]), dt)`.
(The factor is 1.5 in that case because the code computes `1 − (−1)·0.5`.)

### 2.6 Update(dt, damping) vf41 0x350beb40 (sleep logic)

```
Emin_eff = (floats(0x4000000) && nColliders+nConstraints == 0) ? E.EminWater(1132) : E.Emin(652)
if E+92 & 1: damping = max(damping, 0.9)
v,w,P,L *= damping                                   // linear multiplicative damping
if E+952 > 0: resync pose from body (q_ent, pos_ent) and re-register
(constraints with flag 0x10000 whose bodies are broken → action type 6)
if attached to a moving support (first collider with simclass>0 / contact E+736):
    subtract the support's v,w at the contact point from own v,w;  P = M·v;  L = I_world·w
E_kin = 0.5·(Minv·(w·L) + |v|²) + E[952]             // per unit mass
if E+92 & 1: E_kin = min(E_kin, E[1036])
Emin_eff *= 1 − 0.9·[contact-history flag or (nContacts<3 && nColliders==1)]
timer(608) = max(timer,0) − dt
(below z = −200 with no contacts/colliders/water: counter E+252 increments by 3·dt per step → used by world to freeze/kill)
if awake:
    if E_kin < Emin_eff && (nColliders+nConstraints+floats ≠ 0 || |gravity|==0) && timer ≤ 0:  awake = 0
    else clear sleep counter bits
else (candidate asleep):
    accumulate Σv(660) += v, Σw(672) += w;  E' = 0.5·(Minv·(Σw·Iworld·Σw) + |Σv|²) + E[952]
       (unless the history popcount ≥ 20 or flags 0x600 ≥ 0x400, in which case E' = E_kin)
    if E' ≤ Emin_eff:
        if not (E+92&1 or floats): save v,w to 708/720, zero P,L,v,w
        sleepFrames++ ; if sleepFrames ≥ 2 and simclass==2: simclass=1 (sleeping rigid), world re-list
    else: awake=1, reset counters, and if simclass==1 → simclass 2
history(580) = (history<<1) | !awake
return  (performed < full−0.001) | !awake | W[80·tid+21928]
```

---------------------------------------------------------------------------------------------------
## 3. Contact / constraint solver

`V = W+104` is the solver-parameter block, so `V+x` is the same field as `W+104+x`. The defaults below come from the
world ctor (around decompile line 79740). `T = qword_352186E0[tid]` is the per-thread solver buffer ("RB thread data",
0x5FC030 bytes).

### 3.1 Flow
Island pass 1, in 0x3507f9b0:
1. InitSolver 0x3509a900 resets T and sets the "CG allowed" byte at T+6275112.
2. For each entity: RegisterContacts (vf40). It walks the contact list at E+536 and pushes contact pointers into
   T+8 through 0x350994b0 (maximum 9984; any overflow overwrites the last slot).
   0x35099570 clears the CG-allowed byte when the entity has flag 0x8000000 or is in water (`E+1136 > 0`).
3. InvokeContactSolver 0x3509e850 (x86 0x350a62b0).
4. Post-solve 0x3507f210: Update(dt, damping) on each entity.

**No warm starting.** Contact Pn (`+148`) is zeroed at setup for ordinary contacts. Contacts persist between steps
only as entity-list records, which RegisterContactPoint reuses when the same pair has a point within tolerance.

### 3.2 Entity contact record (176 bytes, pooled, list at E+536)

| off | field |
|---|---|
| +0/+8 | next / prev |
| +24, +36 | pt[0], pt[1] (both = hit point) |
| +48 | n (= −geometry normal; for depth > 0.06·Σbbox extents the alternative normal from the hit record is used) |
| +64/+72 | pent[0] (self), pent[1] |
| +80/+84 | ipart[0/1] |
| +96/+104 | pbody[0/1]. Static bodies → per-thread dummy body at `0x3520C390 + 212·tid` |
| +112 | nloc / constraint axis |
| +124 | μ friction |
| +128 | flags (below) |
| +132 | vn (normal relative velocity, also re-used post-solve) |
| +136 | vreq (required separation velocity) |
| +148 | Pn out / impulse limit in (kept only with 0x800000) |
| +152 | depth |
| +156 | iNormal (bits 0-1), surface ids |
| +160 | solver idx (low16), part id |
| +164/+168 | group count / owner pointer (0x4000 shared Pn; 0x400000 multi-body array) |

Flags:
- 0x3F: Stage 1 minimum hit count (0x3F = disabled)
- 0x80: ignore
- 0x100: angular-only
- 0x200 / 0x400 / 0x800: bilateral 3D / 1D-along-n / 2D-tangent
- 0x1000: in the LCP active set
- 0x4000: shares Pn
- 0x8000: "soft" budgeted contact
- 0x10000: project impulse on the axis (a·aᵀ)
- 0x20000: project on the plane (I − a·aᵀ)
- 0x80000: end of the entity's run
- 0x400000: multi-body
- 0x800000: keep the limit
- 0x1000000: auxiliary

### 3.3 Contact creation: RegisterContactPoint 0x350b5e10 (x86 0x350d5a40)

- **Reuse.** An existing contact is reused if it has the same other entity and body, and its point is within
  `min(part sizes @part+92)` (or `0.15·min bbox extent` for flag 0x100000). Otherwise allocate a new one while the
  count is below W+388 (256), or recycle an inactive one.
- **Normal velocity.** `vn = n·[(v0 + w0×r0) − (v1 + w1×r1)]`
- **Friction.**
  - Pick dynamic friction (`W+8552[mat]`, default 0.8; SetSurfaceParams sets it to ⅔·static) if `|vrel|² > (5·W[240])²`.
    Otherwise use static friction (`W+6504[mat]`, default 1.2).
  - `μ = max(0, μa+μb)/2`; set to 0 if the other part has flag 0x40000000 (wheel).
  - `μ = max(μ, E.minFriction(700))`, then `μ = min(μ, W[80·tid+21936])` (the island maxFriction).
- **Restitution.** `e = (bounce_a + bounce_b)/2` (W+4456 table, default 0).
  - It applies only if `e > 0` and `vn < −W[248]` (6 m/s).
  - It is applied **immediately** as an impulse action on both bodies: `P = −(1+e)·vn / (nᵀ K n)`.
  - It is skipped if `(P·Minv0)² ≥ max(|v0|²,|v1|²)·(e+1.1)²`.
- **Penetration bias.**
  - For depth > 0: `vreq = n·min(W[264]=2.5, (depth − W[240]=0.01)·W[260]=10)`.
  - Otherwise `vreq = 0`.

`K = Σ_b (Minv_b·I − [r_b]× Iinv_b [r_b]×)` is the point inverse-mass matrix (built by 0x35098fa0).

### 3.4 InvokeContactSolver 0x3509e850 (x86 0x350a62b0, labels "PreCG","LCPCG","LCPCG-unproj","LCPMC")

**Setup.**
- Per contact: K, Pproj and Kinv. Pproj is I, a·aᵀ, I − a·aᵀ, or n·nᵀ when μ < 0.01.
- The 0x800 tangent basis picks its seed axis with the `|n_k|² > 0.81` test.

**Difficulty test** (only if `V+56`=5 > 0). The island is marked `hard` if:
- any body has `|v| > V+96` (15), or
- a light body is squeezed between opposing normals of bodies heavier than `ratio = V+92` (100), or
- `V+92 ≤ 1`.

**Fast path "PreCG"** (CG allowed and nC < 16):
- Plain conjugate gradient on per-contact 3D impulses, with residual `r = vreq − Pproj·vrel`.
- Tolerance `V+20` (0.005), at most 6·n iterations, with blow-up guards.
- The result is accepted only if every contact satisfies all of:
  - Δvn ≥ −0.05
  - `|Pt|² ≤ (μ·Pn + limit)² + 0.001`
  - `(vreq + r)·n ≥ −0.004` (or −0.015 for angular contacts)
- The island must also satisfy: energy `Σ(M|v|² + w·L) < 1.5·E0`, and residual < 1e-4.
- If accepted: `P += ΔP`, `L += ΔL`, then v and w are recomputed, `B+176/188 = Δ/dt`, and the solver returns.

**Stage 1, the default:** 0x3509ac90, projected Gauss-Seidel on velocity-level impulses, run in buffer order.
- Iteration budget: `maxIt = max(nC·V[12]=4, V[8]=6000)` (decoded as max; worth checking).
- Normal tolerance `tolN = V+20` (0.005); tangential tolerance `tolT = V+72` (0.02).

For each contact:
```
dv = vrel − vreq  (Pproj applied if 0x30000);  dvn = dv·n
ordinary contact acts only if dvn < 0 and (|dvn| > tolN or (budget > 1e-4 and |dv_t| > tolT))
  μ ≤ 0.01:  ΔP = n·(−dvn / nᵀKn)
  else:      ΔP = −dv·|dv|² / (dvᵀ K dv)          // steepest-descent full-3D impulse (stops the relative motion)
             budget += μ·(ΔP·n) − |ΔP_t|          // cumulative cone per solve: Σ|ΔPt| ≤ μ·ΣΔPn
             if budget < 0: u = dv + dv_t·(budget/|ΔP_t|);  ΔP = u·(−dvn / (n·K u))   (fallback to normal-only if ill-conditioned); budget = 0
  acc_n += ΔP·n
bilateral (0xE00): ΔP = −Kinv·dv, clamped so |acc| ≤ 1.01·limit
soft (0x8000):     same steepest-descent step, excess friction taken from the budget, negative normal part removed
apply: v0 += Minv0·ΔP; w0 += Iinv0·(r0×ΔP); L0 += r0×ΔP; body1 the opposite
```
Termination:
- After each sweep: `iters += applied + (nC − applied)>>4`.
- Stop if nothing was applied, if `iters ≥ maxIt`, or if `Σ(M|v|² + w·L) ≥ 3·E0` (divergence guard).
- The function returns the number applied in the last sweep (0 = converged).
- On exit: `P = M·v` and `L = q·(Ibody∘(q⁻¹w))`.

Friction model: friction acts only while the contact is approaching. The cone is cumulative and L1-like, per solve,
with no pyramid and no per-axis clamp. The static/dynamic switch happens only at contact creation.

**Stage 2 "LCPCG"** runs only if Stage 1 did not converge, the island is `hard`, and `nC < V+100` (100).
- Preconditioned CG over an active set; contacts with `vn < 0` (flag 0x1000) form the set. Sticking contacts have
  3 unknowns and the rest have 1.
- α is clamped to ≤ 50.
- Iteration limits: `min(2·nU, V+44=250)` or `min(nU, V+40=120)`. Tolerance `V+24` (0.005).
- Stall exit: more than `V+64` (4) iterations each improving by less than `V+60` (5%).
- Up to `V+56` (5) outer passes.
- Rejected if `|Δv| > V+76` or `|Δw| > V+80` (500), or on NaN.

**Stage 3 "LCPCG-unproj"** (positional unprojection) runs only after an accepted Stage 2.
- Bodies are ordered by stack level above static bodies.
- Bottom-up, a per-body CG solves pseudo-velocities `vu`, `wu` for the contacts with `|vreq|² > 1e-4`.
- Results go into the pose: `pos += vu·dt`, `q = rot(wu·dt)·q`. The exception is `V+88 ≠ 0` (default 0), where they
  are added to the velocities instead.

**Finish.** `B+176 = (P − P_saved)/dt`, `B+188 = (L − L_saved)/dt` (the "collision force" accumulators), and contact
`+148` = accumulated Pn.

**Takeaway.** For ordinary resting and stacking contacts the engine is effectively "PGS with a velocity bias
`vreq = min(2.5, 10·(depth − 0.01))`". There is no separate position correction in the default path.
Ships resting in water have no contacts at all, so the solver does not take part in floating.

### 3.5 Solver world variables (offset from W)

| W+ | default | meaning |
|---|---|---|
| 112 | 6000 | Stage 1 iteration floor |
| 116 | 4 | iterations per contact |
| 124 | 0.005 | tolN |
| 128 | 0.005 | Stage 2 tolerance |
| 144/148 | 120/250 | Stage 2 iteration limits |
| 160 | 5 | passes |
| 164 | 0.05 | minimum improvement |
| 168 | 4 | stall iterations |
| 176 | 0.02 | tolT |
| 180/184 | 500 | max Δv/Δw |
| 192 | 0 | unproject by velocity |
| 196 | 100 | heavy/light mass ratio |
| 200 | 15 | hard-speed threshold |
| 204 | 100 | Stage 2 maximum contacts |
| 240 | 0.01 | contact gap |
| 248 | 6 | minimum bounce speed |
| 260 | 10 | unproject velocity per metre |
| 264 | 2.5 | maximum unproject velocity |
| 364 | ? | maximum linear velocity |
| 388 | 256 | maximum contacts per entity |

---------------------------------------------------------------------------------------------------
## 4. Buoyancy and water (ApplyBuoyancy 0x350c0e50; x86 0x350d1d80)

### 4.1 Inputs
- `buoy[0..n)`: up to 4 records (80 bytes each), filled by world vf87 **CheckAreas** 0x3504e000 from the default area
  (W+20928) plus every area whose bounds contain the entity's BBox centre. Areas with a callback can override.
  CheckAreas also returns a gravity value. In 0x350c4880 that value is **only** used to set the sleep timer
  (`E+608 = 0.2` if it differs from gravityFreefall).
- `g = (0,0,−9.81)` (hard-coded at the call site).
- Skipped entirely if `E.kwaterDensity(1124) == 0` or `B.Minv == 0`, in which case `E+1136 = 0`.

### 4.2 Algorithm (per area record `a`, per part `p`)

```
clear E.flags2 0x4000000 (floats)
fracMax = 0
for each record a with a.iMedium < 2:
    plane.n = a.n;  plane.o = a.origin;  plane.o.z -= E.waterLevelOffset(1192)
    if a.n.z == 1: plane.o.xy = E.pos(408).xy                     // numerical nicety only
    // whole-entity early out with the NEW bbox (E+476..496):
    c = bbox centre, h = bbox half size
    d   = n·(c − o);   ext = |hx·nx| + |hy·ny| + |hz·nz|
    if d > ext: continue                                           // completely above the surface
    Vtot = 0; Vsub = 0
    for each part p with (p.flags & 0x10000 /*floats*/):
        Vp = p.scale³ · pgeom.V
        if !( Vp·a.ρ·E.kρ > 0.1·p.mass
              || A_bb·p.scale²·a.res·E.kres > 0.0125·p.mass      // A_bb = middle-of-3 of the bbox face areas (pgeom bbox, vt+48)
              || a.damping > 0 ): continue
        pose_p = (E.q_new(460)⊗p.q, E.pos_new(448)+E.q_new·p.pos, p.scale)   // NEW (post-integration) pose
        body = GetRigidBody(p)             // = B for a rigid entity
        u  = body.v − a.flow;   ω = body.w;   c0 = body.pos (CoM)
        if a.res > 0:                                                   // medium resistance
            (F, T) = geom.CalculateMediumResistance(plane, pose_p, u, ω, c0)    // slot 14, §4.4
            k = E.kres · a.res
            dP = F·k·dt;  dL = T·k·dt
            if a.iMedium == 0: Fwater += F·k                            // used for splash events
        else if |a.flow|² > 0:                                          // crude flow drag, no dt (as read)
            dP = (a.flow − v_body) · ((hz+hy)·hx + hz·hy) · a.ρ·E.kρ    // h* = entity bbox half sizes
            dL = 0
        else: dP = dL = 0
        if d ≤ −ext:                                                    // whole entity under water
            Vs = Vp                                                     // scaled volume, no geometry query
            dP += −g · (a.ρ·E.kρ·Vs) · dt                               // force at CoM, no torque
        else:                                                           // partially submerged
            (Vs, cob) = geom.CalculateBuoyancy(plane, pose_p)           // slot 13, §4.3
            J = −g · (a.ρ·E.kρ·Vs) · dt
            dP += J
            dL += (cob − body.pos) × J
        if |dP|²+|dL|² > 0: E.Action(action_impulse{impulse=dP, angImpulse=dL, point=unset, partid=unset, ipart=p, iApplyTime=… (=1? "2"/"1" fields)})
        Vsub += Vs
        Vtot += pgeom.V                                                 // UNSCALED volume (as read)
    if Vsub·Vtot > 0:
        r = min(1, Vsub/Vtot)
        dampEff = max(E.waterDamping(1120), a.damping)·r + (1−r)·E.damping(684)
        E.waterDampEff(692) = max(E[692], dampEff)
        if a.iMedium == 0:
            fracMax = max(fracMax, r)
            if a.ρ·E.kρ·B.V > B.M: E.flags2 |= 0x4000000                // "floats"
E.submergedFraction(1136) = fracMax
```
Impulses go through Action, so they land in the queued P/L (E+956/968) or are applied directly, depending on
Action's sync mode. Either way they reach the body before the gravity term in the same Step.

The flow-drag branch (`res == 0`) is not multiplied by `dt` and uses `v_body − flow` with the opposite sign
convention from the resistance branch. This is copied as read and is probably a bug. With the default
waterResistance = 1000 that branch never runs.

**Effective constants with defaults:** `ρ_eff = area.waterDensity(1000)·kρ`, with `kρ = paramDensity/1000`
(default 0.001 → ρ_eff = 1). So a ship that sets `waterDensity = 1000` gets ρ_eff = 1000. `res_eff = area.waterResistance(1000)·kres`.
The damping blend only works through `GetDamping` (`v *= max(0, 1 − d·dt)`).

### 4.3 CalculateBuoyancy (CTriMesh slot 13 0x351da620; box converts itself to a 12-triangle mesh via 0x3513ac00)

```
R = rotation that maps the plane normal n to +z (0x35040f80: shortest arc from (0,0,1))
transform every vertex: z_i = signed height above the plane, in the plane frame (origin = plane.o)
for each triangle:
    classify the 3 vertices by z<0; clip edges at z=0 (lerp)
    submerged part (1, 2 or 3 verts) → sum of signed vertical prisms between the
    submerged polygon and the surface z=0: volume = area_xy · (mean depth), centroid accumulated.
    sign = orientation of the triangle normal (outward faces subtract/add), so closed meshes give
    V_sub = Σ prisms.
V_sub = Σ;  cob = Σ(Vi·ci)/V_sub + plane.o        (cob is NOT rotated back by Rᵀ: exact only for n=(0,0,1))
return V_sub   (and V_sub ≤ 0 → cob = 0)
```
Exact geometry requires the mesh to be closed. With a world-up water plane this is exact.

### 4.4 CalculateMediumResistance (CTriMesh slot 14 0x351cd0c0 → per triangle 0x35133e50)

For each triangle (world space, outward normal `n`):
```
poly = clip(triangle, keep n_w·x < d_w)                 // part below the water plane
r_i  = poly_i − c0                                      // relative to the CoM
poly = clip(poly, keep (ω×n)·r < n·u)                   // keep only the region moving INTO the medium:
                                                        //   u_n(r) = n·(u + ω×r) > 0
rotate into a frame with z = n; integrate over the polygon with Green's theorem:
    Q   = ∫ u_n dA              (u_n linear in x,y: u_n = n·u + ωx'·y − ωy'·x)
    Mxy = ∫ (x,y)·u_n dA
F  -= n·Q                                               // pressure drag LINEAR in velocity
T  -= ∫ r × n · u_n dA
```
So the resistance is a **linear** (velocity-proportional) pressure drag over the submerged, forward-facing area.
Coefficient = `res_eff`. Units: N per (m/s · m²). The caller applies `F·k·dt` as an impulse.

### 4.5 Splash / water-surface events (still inside ApplyBuoyancy)
- If world water manager W+20848 exists and `|1 − 2·frac| < 0.998`, call 0x351008d0 (water ripple/wave hook *(inferred)*).
- If `W[452]` (max splash events) > 0, E+92 bit 2 is clear, `frac < 0.9`, and the CoM is within `W[468]` of the
  splash reference point `W[20856]`, then work out an impact speed from `v·n` against `W[456..476]` (splash
  thresholds). If it passes, post a 160-byte collision event against the water (pool at W+21088, lock W+22512).
  For bboxes ≥ 0.5 m it emits one event per submerged mesh vertex, up to W[452]. Purely cosmetic.

### 4.6 ApplyAirResistance 0x350c2790 (only if E+92 & 8)
Calls world vf87 again with a 4-record buffer. For records with `iMedium == 1`, for each part with nonzero volume,
it calls the same CalculateMediumResistance with `u = body.v − flow`. Then
`dP = F·dt·E.kAir(1140)`, `dL = T·dt·E.kAir`, sent through Action(impulse). There is no resistance coefficient
from the area: only kAir (default 0.05).

### 4.7 Porting checklist for ships
1. Each substep, before gravity: per floating part, `V_sub, cob` by clipping the mesh with the plane (§4.3).
   Then impulse `−g_fixed·ρ_eff·V_sub·dt` at cob, giving a torque about the CoM.
2. Linear pressure drag on forward-facing submerged triangles (§4.4), scaled by `res_eff·dt`.
3. `waterDampEff = max(waterDamping, area.damping)·r + (1−r)·damping`. The velocity factor is `1 − max(d,waterDampEff)·dt`, applied **after** the solver.
4. Sleep threshold switches to `EminWater` when floating and contact-free.
5. Gravity `M·g·dt` with `g = gravity`/`gravityFreefall`. This g is separate from the buoyancy constant 9.81.

---------------------------------------------------------------------------------------------------
## 5. Wheeled vehicle

CWheeledVehicleEntity (vtable 0x351f2388, 84 slots, object 11256 bytes) **does not override Step**. It hooks in
around the rigid step:

| slot | x64 / x86 | when | what |
|---|---|---|---|
| vf78 | 0x3510c940 / 0x3511afb0 ("CheckAdditionalGeometry") | inside rigid Step, after integration (and in the asleep branch) | wheel contact detection, sets suspension length |
| vf79 | 0x351074a0 / 0x351142f0 | end of rigid Step (the vt+632 hook) | engine/gears (0x351057c0), suspension and drive **impulses applied straight into P/L**, then fills the embedded friction contacts, `v = P/M` clamped to max speed (E+10996), `w = Iinv·L` |
| vf40 | 0x35105e50 / 0x3511ffc0 | before the solver | pushes one embedded contact per grounded wheel into the solver list, then the rigid vf40 |
| vf41 | 0x351050a0 | after the solver | recent-collision timer; Emin = on-ground ? E+11168 : E+11164; then the rigid vf41 |
| vf37 | 0x35104790 / 0x35112fc0 | | maxTimeStep = (recent hit ? E+11176 : E+11172) × (E+92&0x10000 ? 0.75 : 1) |
| vf43 | 0x35104fa0 / 0x35113550 | | no recent hit: `max(0, 1 − dt·E[11180])` |

**Wheels.** Wheels are the last parts (non-wheel part count at E+11156; maximum 18). Wheel parts get part flag
0x40000000 and collider mask 0, so they never make normal rigid contacts.
- Wheel record: `E + 1200 + 544·i`. Key fields: +76 lenMax, +84 k, +88 c, +96 lenInitial (rest), +100 effective mass at
  the pivot, +188 radius, +200 current length, +212 spin ω, +308 suspension impulse N.
- The embedded solver contact sits at wheel+336, in the §3.2 layout.

**Detection (vf78).** By default the wheel cylinder is swept down by lenMax against the ground candidates; with flag
bit 3 a ray of length 2·lenMax + R is used instead. A hit is accepted if `|up·n| ≥ E[11232]`. Suspension length:
`len = min(len + 7·dt, lenMax − depth)`, so the wheel extends at most 7 m/s and compresses instantly. If depth exceeds
lenMax, a push-out vreq of `min(2·(depth − lenMax)·W[260], 0.7·W[264])` is used.

**Suspension (vf79).**
`F = (lenMax − max(len, lenInit − W80))·k − c·v_up − anti-roll·(len − len_partner)·k`, then `N = max(0, F·dt)`.
The impulse `N·up` is applied at the pivot. If wheel damping ≤ 0, the damping ratio form is used:
`c = −ζ·√(4·k·m_eff)`.

**Drive.**
- Engine torque comes from a sine-shaped power curve over RPM, times the gear ratio and clutch; gear 1 is neutral and
  0 is reverse.
- Per-wheel torque = axle friction + brake + drive share.
- The drive impulse `τ/R·dt` along the rolling direction is applied directly and capped by `Fcap = N·μ`.
- μ is shaped by slip, load and low speed.

**Lateral friction** is the only part that goes through the contact solver. It uses the embedded contact with
direction = axle, flags 0x18000|0x800000 (the impulse limit is the lateral cap), and friction = W156·μ.

**Ground reaction.** If the ground body's Minv is less than 10 × the vehicle's, the ground gets `−N·up` as an impulse.

---------------------------------------------------------------------------------------------------
## 6. Determinism notes

- **Float model:** both builds use SSE scalar single precision (x64 decompile has ~2300 `_mm_` uses, x86 ~4900).
  There is no x87 extended precision in the hot paths. `fsqrt` is `sqrtss`, and normalisation is `1/sqrtss`
  (not `rsqrtss`). Hex-Rays shows some `double` locals for contact depth (`*(double*)` in the contact table
  `+0`), so contact depth is stored as double.
- **min/max:** the code writes min/max as `(a+b ± |a−b|)·0.5`. This is **not** bit-identical to `fminf/fmaxf`
  (it rounds and can lose the last ulp). To be bit-exact, port the expression literally.
- **Branch-free selects** use sign-bit tricks (`COERCE_UNSIGNED_INT(x) >> 31`). `−0.0` therefore counts as negative.
- **Quaternion product** is SSE shuffles + xor sign mask (`xmmword_351F27B0`). The order of the products and
  additions matters for bit-exactness: use `(a·b + c·d) ^ sign − e·f + g·h` as in the decompile.
- **Time quantisation:** world dt is rounded with the `+12582912.0` magic-number trick (round-to-nearest-even at the
  fixed step).
- **Randomness:** CryRandom/CryRandomFloat imports are used only in area/geometry random-point sampling
  (CPhysArea vf37, geometry GetRandomPos-like vf43, rope/soft/particles). **None** are used in the rigid-body
  Step, buoyancy or the solver path that was examined.
- **Multithreading:** islands are stepped in parallel by the physics worker threads ("PhysicsWorkerThread",
  per-thread data `qword_352186E0[tid]` "RB thread data", per-thread contact table `qword_35218710[tid]`).
  Inside an island the order is deterministic (linked list E+144). But the world event queues, entity
  re-registration and area/splash events use spin locks, and their order can vary between runs. Contact
  generation order depends on the collider array order (E+208), which depends on grid query order.
  For bit-exact reproduction, run one island at a time and keep the collider order.
- **Iteration-count dependence:** substep dt is `min(GetMaxTimeStep)` per island, so results depend on which bodies share an island.
- **Solver ordering depends on heap addresses.** The solver quicksorts (0x350995c0, 0x350996d0, 0x35099840) compare
  **body pointer values** and are unstable. Gauss-Seidel order, and with it the results, therefore depend on heap
  layout. A port should sort by a stable id; exact bit reproduction of the original is not achievable here anyway.
- Per-thread body solver index is a `short` at B+200+2·tid, so there are at most 4 solver threads.

---------------------------------------------------------------------------------------------------
## 7. Uncertainties / to verify

1. ~~Stage 1 iteration cap decodes as `max(4·nC, 6000)`. It may be `min`.~~ **Resolved: `max`** (disassembly at 0x3509FCDF; CRYPHYSICS-MAP Appendix A).
2. E0, the energy reference for the solver divergence guards, comes through a garbled argument list. It looks like an
   island kinetic-energy estimate scaled by W+1748.
3. ~~Quaternion product order in `q_ent = B.q ⊗ qfb`.~~ **Resolved: `normalize(B.q ⊗ qfb)`**, bit-exact against the oracle (CRYPHYSICS-MAP Appendix A).
4. ~~Whether the ApplyBuoyancy impulse is applied immediately or queued.~~ **Resolved: immediately, same substep** (`iApplyTime`(+72) = 0, `iSource`(+76) = 1, bThreadSafe = 1; |Δv|,|Δω| ≤ 5, no wake; oracle experiment, CRYPHYSICS-MAP Appendix A).
5. The flow-drag branch has no dt, the Vtot sum is unscaled, and cob is not rotated back for tilted planes. These are
   copied from the binary and may be genuine bugs. Keep them for fidelity, or fix them deliberately.
6. GetMaxTimeStep constants (0.7, 1.7, 0.005, ×1.5 factor) and the "post-step zeroing" (E+504 bit 0x800) purpose are unclear.
7. The meaning of E+640 ("constant angular acceleration") is unconfirmed: `L += I_world·α·dt`, default 0.
8. Water level and waves for ArcheAge probably come from area records or area callbacks (event type 11, area+520).
   The DLL itself has no wave model, apart from the optional water-manager hook W+20848.

### Notes for the oracle harness (box sinking/bouncing)
Fields a fresh setup must have set, or resting contact breaks:
- **World solver vars.** W+104..+264 must hold the ctor defaults (§3.5). With zeros: tolN = 0, contact gap 0,
  `vreq` scale 0, maxIt = 0. The result is no penetration recovery, and a box sinks and jitters.
  In particular W+260 (10) and W+264 (2.5) drive the only depenetration in the default path; W+240 (0.01) is the allowed gap.
- **Per-island values** written by the island driver 0x3507f9b0:
  - `W[80·tid+21936]` = min maxFriction (μ cap; 0 means frictionless).
  - `W[80·tid+21932]` = island dt.
  - `W[80·tid+21928]` = island flag.
  If entities are stepped directly rather than through world TimeStep, these stay 0.
- **Per-frame StartStep.** StartStep (vf26) must run every frame (`E+596 = dt`, `E+600 = 0`). Otherwise Step returns
  early after the first 0.01 s.
- **Order of a substep.** Step (gravity added after contact registration) → RegisterContacts → solver → Update. If
  the solver does not run after Step, gravity accumulates with no contact response.
- **Restitution.** The bounce impulse is applied immediately inside RegisterContactPoint when `vn < −6 m/s` and e > 0.
