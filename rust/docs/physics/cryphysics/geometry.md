# CryPhysics geometry and collision detection (draft)

This draft covers geometry types and collision detection, written for the later Rust port.

**Binaries**
- x64 10.x `cryphysics_dedicate.dll`: base `0x35000000`, has RTTI. This is the primary source.
- x86 1.2 client `cryphysics.dll` (unpacked dump): base `0x34ff0000`, no RTTI.

**How to read addresses**
- Addresses are full VAs unless noted.
- In the big tables, x64 cells drop the leading `0x35`. For example, `13f550` means `0x3513f550`.
- Where two addresses are given as `x64 / x86`, the x86 address comes from the same table cell or vtable slot in the x86 image. This mapping is reliable. `compare/func_match.csv` is wrong for several of these functions, for example intersect[box][tri].

**Naming rules**
- Names from RTTI or binary strings are used as-is.
- Everything else is named from behaviour and marked **(inferred)**.

**Sources.** All derived from the decompiles and disassembly; sections 3, 5 and 6 were produced by focused sub-passes over the same binaries.

---------------------------------------------------------------------------------------------------

## 1. Geometry type ids and data layouts

### 1.1 GetType (vtable slot 1)

| id | class (RTTI) | string in `.rdata` | slot-1 impl (x64) |
|---|---|---|---|
| 0 | CBoxGeom | (no string; name table at 0x351f2910 starts "unknown","voxel","sphere","capsule","cylinder","heightfield","trimesh" — "box"/"ray" not present as separate strings) | shared `return 0` stub 0x350376d0 |
| 1 | CTriMesh | "trimesh" | shared `return 1` 0x35076030 |
| 2 | CHeightfield | "heightfield" | 0x350fe380 |
| 3 | CRayGeom | – | 0x35024cd0 |
| 4 | CSphereGeom | "sphere" | 0x35037c40 |
| 5 | CCylinderGeom | "cylinder" | 0x35038a00 |
| 6 | CCapsuleGeom (derives Cylinder) | "capsule" | 0x35020a60 |
| 7 | CVoxelGeom (derives CTriMesh) | "voxel" | 0x350ea790 |

**BV-tree ids** use their own numbering (BV slot 1):
- OBB = 0, AABB = 1, SingleBox = 2, Ray = 3, Heightfield = 4, Voxel = 5.

**BV-node primitive types** in the temp BV records reuse the geometry ids: box 0, tri 1, hf 2, ray 3, sphere 4, voxel 7.

### 1.2 CGeometry common header (all geometries)

The constructors are inline in each class's ctor, e.g. box 0x35075190, sphere 0x35038c20, cylinder 0x35038960, ray 0x35038dd0, trimesh 0x351bf470.

| off | type | meaning |
|---|---|---|
| +0 | vptr | |
| +8 | int | RW spin lock. Initialised to −1. Readers do `+1` and wait while byte +10 ≠ 0; a writer CASes 0 → 0x10000. Used by slots 4/5. |
| +12 | int | refcount, starts at 1 (slots 2/3) |
| +16 | float | m_minVtxDist (inferred): `0.0001·(sx+sy+sz)` for a box, 1.0 for a ray. Used in contact merging: `min(geomA.+16, geomB.+16)²`. |
| +20 | int | collision priority (inferred): ray 0, trimesh/heightfield 1, primitives 3. The higher-priority geometry drives contact registration (`gtB+20 − gtA+20` swap). |
| +24 | int | 1 for primitives (inferred: bIsConvex-like) |
| +32 | void* | foreign data (slots 35/37). Released by the dtor when +40 == 1. |
| +40 | int | iForeignData |
| +48.. | | class data |

### 1.3 Primitive data (what slot 17 GetPrimitive copies and slot 30 SetData accepts)

**Box (CBoxGeom, sizeof 200; slot 52 returns 200)**

| off (obj) | prim off | field |
|---|---|---|
| +48 | +0 | Matrix33 Basis (row-major; rows are the box axes) |
| +84 | +36 | int bOriented. SetData sets it to `!IsIdentity(Basis)` (sub_3513A010). |
| +88 | +40 | Vec3 center |
| +100 | +52 | Vec3 size (half-extents) |
| +112 | | embedded CSingleBoxTree (88 bytes; its +8 points back to the geometry) |

- `V = 8·sx·sy·sz`.
- CalcPhysicalProperties (0x3513f550) writes `Ibody = V/3·(sy²+sz², sx²+sz², sx²+sy²)`, `q = quat(Basisᵀ)` (identity if not oriented), and `origin = center`.

**Sphere (sizeof 152)**
- `+48` center Vec3, `+60` r. BV `+64` (CSingleBoxTree).
- `V = 4/3·π·r³` (constant 4.188790204786391).

**Cylinder (sizeof 176)**
- `+48` center, `+60` axis (unit), `+72` r, `+76` hh (half height).
- `+80` int = 18 (inferred: tessellation count for draw/convert). BV `+88`.
- `V = 2π·r²·hh`.

**Capsule**
- Same layout and constructor as the cylinder. Here hh is the half-length of the cylindrical part.
- `V = 2π·r²·hh + 4/3·π·r³`.
- Slot 48 (PreparePrimitive) reuses the cylinder code and returns type 6.

**Ray (sizeof 112)**
- `+48` origin, `+60` dir (full-length segment vector), `+72` ndir (normalised; zero-length → (0,0,1)). BV `+88` (CRayBV).
- `+104` points to `this+48`.
- Center = origin + dir/2. GetPrimitive copies 24 bytes {origin, dir}.

**Plane** (only used as an argument, e.g. the water plane for slots 13/14)
- `{Vec3 n; Vec3 origin;}`.

**Triangle primitive** (CTriMesh slot 17 → 48 bytes)
- `{Vec3 pt[3]; Vec3 n;}`.

**Heightfield primitive** (object +608, 120 bytes; GetPrimitive copies it, slot 29 returns +608)

| prim off | obj off | field |
|---|---|---|
| +0 | +608 | Matrix33 Basis |
| +36 | +644 | int bOriented |
| +40 | +648 | Vec3 origin |
| +52 | +660 | Vec2 step (x, y) |
| +60 | +668 | Vec2 stepr = 1/step |
| +68 | +676 | int2 size (nx, ny) |
| +76 | +684 | int2 stride (inferred) |
| +84 | +692 | float heightscale |
| +88 | +696 | u16 typemask |
| +90 | +698 | u16 heightmask |
| +92 | +700 | int typehole |
| +96 | +704 | int typepower |
| +104 | +712 | `float (*getHeight)(ix, iy)`, called as "primitives::getHeightCallback" (string at 0x351f39f8) |
| +112 | +720 | `u16 (*getSurfType)(ix, iy)` |

- The CHeightfieldBV is at object +728.
- **PointInsideStatus** (0x35154d00):
  1. `ix = floor(x·stepr.x)`, `iy = floor(y·stepr.y)`. Out of range if `ix > nx−2`.
  2. `surf = (getSurfType(ix,iy) & typemask) >> typepower`. If surf == typehole, the point is outside (hole).
  3. Interpolate the height on the cell's triangle. The cell is split on the u+v = 1 diagonal: the lower triangle uses (0,0),(1,0),(0,1); the upper uses (0,1),(1,1),(1,0). Heights are `getHeight·heightscale`.
  4. Inside if `z < h`.
- GetCenter is the grid centre transformed by Basis + origin.

**CTriMesh** (sizeof 0x260 = 608; constructor 0x351bf470; the "mesh data" block starts at +56, which slot 29 returns)

| off | field |
|---|---|
| +48 | CBVTree* tree |
| +56 | int* pIndices (3 per tri) |
| +64 | char* pMats (per tri; slot 16) |
| +72 | int* pForeignIdx (per tri; slot 18) |
| +80 | Vec3 strided pointer to vertices. +88 = stride (default 12). |
| +96 | Vec3* pNormals (per tri; slot 19) |
| +104 | int* pVtxMap (per vertex) (inferred) |
| +112 | int(*)[3] pTopology: neighbour tri across each edge, <0 = open edge (inferred from IsConvex) |
| +120 | nTris |
| +124 | nVertices |
| +152 | flags. Bits 1/4/8 = vertices / mats / foreignIdx not owned. Build flags 0x20 OBB, 0x40 AABB, 0x40000 AABB in principal axes, 0x100/0x400 skip-dim, 0x4000 flat split; see §3. |
| +160 | per-tri auxiliary array (permuted together with indices at build) |
| +476 | ray-test grid ready count (slot 22) |
| +480 / +496 / +512 | IsConvex cache: 4 results / 4 tolerances (init −1) / lock |
| +516..+528 | cached floats, initialised to 0xFFBFFFFF (NaN = "not computed") |
| +540 | counter (slot 38) |
| +544 | foreign data for id −1 |
| +552 | 0x8000000 |
| +560 / +568 / +576 | `bop_meshupdate_thunk` list head (boolean-op update log; RTTI `bop_meshupdate(_thunk)`) |
| +588 | counter (slot 34) |

**CVoxelGeom** (CTriMesh + voxel grid; sizeof 880; constructor 0x3514da60)
- +768 strided vertex pointer (stride +776 = 12).
- Grid dimensions int3 at +684/+688/+692.
- +808 `int* cellStart[nx·ny·nz+1]`, +816 `int* cellTris[]`.
- +824 embedded CVoxelBV, +872 int.
- The memstat string is "voxel grids".

**Per-thread / per-caller scratch arena**
- Base 0x35256F80, stride 0x39FB8 = 237496 bytes, 5 slots, indexed by iCaller (from TLS).
- Holds:
  - world-space primitives produced by slot 48 (box: arena +0x346C, 64 B entries, ring of 2);
  - BV pools (see §3);
  - contact arrays (136 B geom_contact, up to 64);
  - border-point pools (256 points, 32 contact areas of 72 B);
  - the overlap scratch at +0x39F68.
- A Rust port can replace all of this with stack- or arena-allocated per-query buffers.

---------------------------------------------------------------------------------------------------

## 2. Geometry vtable (IGeometry 46 → CGeometry 60 → CTriMesh 62 slots)

The slot order is identical in x86 1.2: all classes have the same slot counts (vtable_match score ≥ 4.7).

**Cell legend**
- `rN` = returns N; `r0.0` = returns 0.0f; `rF` = returns `false`; `nop` = empty; `pure` = `_purecall`.
- `=base` = same function as the CGeometry column.

**Stubs.** Folded stubs carry arbitrary IDA names (CTypeInfo__vf9, CBVTree__vf3, …); the table resolves them.

**Prepare / register / cleanup protocol.** Slots 53–59 are the "geometry_under_test" protocol used by the generic Intersect:
- 53 prepare, 48 make primitive, 56/57 primitive lists, 54 register contact, 55 cleanup.

### 2.1 x64 slot → role → implementation

| slot | role | CGeometry | CBoxGeom | CSphereGeom | CCylinderGeom | CCapsuleGeom | CRayGeom | CTriMesh | CHeightfield | CVoxelGeom |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | scalar deleting dtor | 378d0 | 75340 | 38d70 | 38bc0 | 38bc0 | 38bc0 | 1db570 | fe3c0 | 14dae0 |
| 1 | GetType -> geom type id | pure | r0 | 37c40 | 38a00 | 20a60 | 24cd0 | r1 | fe380 | ea790 |
| 2 | AddRef (++ +12) | 376b0 | =base | =base | =base | =base | =base | =base | =base | =base |
| 3 | Release (--+12; on 0: memstat -= slot52(), delete) | 14a8f0 | =base | =base | =base | =base | =base | =base | =base | =base |
| 4 | Lock(bWrite) – spin RW lock on +8 (0x10000 = writer) | 37850 | =base | =base | =base | =base | =base | =base | =base | =base |
| 5 | Unlock(bWrite) | 378b0 | =base | =base | =base | =base | =base | =base | =base | =base |
| 6 | GetBBox(box*) (prims: forwards to embedded BV slot 2) | pure | 75240 | 38cb0 | 38a10 | 38a10 | 199f50 | 103340 | 103340 | 103340 |
| 7 | CalcPhysicalProperties(phys_geometry*) – V, Ibody (per unit density), q, origin | r0 | 13f550 | 1aeac0 | 145000 | 141b70 | r0 | 1da290 | 1da290 | 1da290 |
| 8 | PointInsideStatus(pt) -> 0/1 (mesh: slot61(pt,0)) | r-1 | 13a660 | 1aeb40 | 141fc0 | 13f7a0 | r-1 | 1db380 | 154d00 | r-1 |
| 9 | Intersect wrapper (sets up caller/lock when last arg==4, then slot 10) (inferred) | 37740 | =base | =base | =base | =base | =base | =base | =base | =base |
| 10 | Intersect(IGeometry*, gwd1, gwd2, intersection_params*, geom_contact*&) -> ncontacts | 14fd70 | 151f20 | 151f20 | 151f20 | 151f20 | =base | 1c9910 | 156e20 | 1dda80 |
| 11 | FindClosestPoint(gwd, iPrim&, iFeature&, ptdst0, ptdst1, ptres, nMaxIters) (inferred) | r0 | 13d020 | 1af660 | 145cc0 | 13fca0 | r0 | 1c6780 | 155cb0 | 1c6780 |
| 12 | CalcVolumetricPressure(gwd, epicenter, k, rmin, ..., P&, L&) (inferred) | nop | 13b0f0 | 1aee70 | 142ee0 | 142ee0 | nop | 1cbe20 | 154cb0 | nop |
| 13 | CalculateBuoyancy(plane*, gwd, Vec3& massCenter) -> submerged V (inferred) | 37800 | 13b1b0 | 1aec00 | 143230 | 13f790 | =base | 1da620 | 1da620 | 1da620 |
| 14 | CalculateMediumResistance(plane*, gwd, Vec3& dPres, Vec3& dLres) (inferred) | 37810 | 13e7c0 | 1afcb0 | 146f40 | 140ea0 | =base | 1cd0c0 | 1cd0c0 | 1cd0c0 |
| 15 | DrawWireframe(IPhysRenderer*, gwd, iLevel, idxColor) (inferred) | 377b0 | 13a440 | 1aeff0 | 141e70 | 141e70 | =base | 1be810 | 1be810 | 1be810 |
| 16 | GetPrimitiveMatId(iPrim) (mesh: pMats[i]) | r-1 | r-1 | r-1 | r-1 | r-1 | r-1 | fe1b0 | fe1b0 | fe1b0 |
| 17 | GetPrimitive(iPrim, prim*) -> sizeof (box 64, sph 16, cyl 32, ray 24, tri 48, hf 120) | pure | 752c0 | 38d40 | 38a70 | 38a70 | 38f70 | 14db60 | 1036c0 | 14db60 |
| 18 | GetForeignIdx(iPrim) (mesh: pForeignIdx[i]) | r-1 | r-1 | r-1 | r-1 | r-1 | r-1 | fe1d0 | fe1d0 | fe1d0 |
| 19 | GetNormal(iPrim, Vec3&) (default (0,0,1); mesh: pNormals[i]) | 37830 | =base | =base | =base | =base | =base | fe1f0 | fe1f0 | fe1f0 |
| 20 | GetFeature(iPrim, iFeature, Vec3 pts[]) -> npts (feature code bits 0x60: 0 vertex/0x20 edge/0x40 face) | r0 | 13a780 | r0 | r0 | r0 | r0 | 1be700 | 1be700 | 1be700 |
| 21 | IsConvex(tolerance) (mesh: cached per tolerance at +480/+496) | r1 | r1 | r1 | r1 | r1 | r1 | 1c2490 | r0 | r0 |
| 22 | PrepareForRayTest(raylen) (inferred; ray: renormalise dir; mesh: builds ray grid, +476) | rF | rF | rF | rF | rF | 38fa0 | 1d7c40 | rF | rF |
| 23 | DrawToOcclusionCubemap(gwd, iStartPrim, nPrims, iPass, cubemap[6], res, rmin, rmax, zscale) – top level, traverses BV (inferred) | 14ebf0 | =base | =base | =base | =base | =base | =base | =base | =base |
| 24 | GetMemoryStatistics(ICrySizer*) | nop | 13a420 | 1ae9c0 | 141e50 | 141e50 | nop | 1bed10 | 154cc0 | 1dcab0 |
| 25 | Save(CMemStream&) | nop | 13b240 | 1afbf0 | 144cd0 | 144cd0 | nop | 1cecb0 | 1cecb0 | 1cecb0 |
| 26 | Load(CMemStream&, ...) (mesh: Load(stream, pVtx, pIdx, pMats)); base -> slot 27 | 37710 | =base | =base | =base | =base | =base | 1d83b0 | 1d83b0 | 1d83b0 |
| 27 | Load(CMemStream&) (prims: read 16..64B prim + rebuild) | nop | 13b280 | 1afc60 | 144d40 | 144d40 | nop | 1033e0 | 1033e0 | 1033e0 |
| 28 | GetPrimitiveCount (prims 1; mesh nTris +120) | r1 | r1 | r1 | r1 | r1 | r1 | fe220 | fe220 | fe220 |
| 29 | GetData() -> &primitive (prims +48, mesh +56 = mesh data, hf +608) | pure | 38e50 | 38e50 | 38e50 | 38e50 | 38e50 | 14dac0 | fe3b0 | 14dac0 |
| 30 | SetData(primitive*) (rebuilds BV, m_minVtxDist) | nop | 75260 | 38cd0 | 38a20 | 38b50 | 38ee0 | nop | nop | nop |
| 31 | GetVolume | r0.0 | 752a0 | 38d10 | 38a40 | 38b80 | r0.0 | 1db0d0 | 1db0d0 | 1db0d0 |
| 32 | GetCenter | 37840 | 75270 | 38ce0 | 38ce0 | 38ce0 | 38e70 | 1be500 | 103420 | 1be500 |
| 33 | Subtract(IGeometry*, gwd1, gwd2, bLogUpdates) – boolean mesh op (inferred) | r0 | r0 | r0 | r0 | r0 | r0 | 114b30 | 114b30 | 114b30 |
| 34 | mesh: returns +588 (error/invalid count, inferred) | r0 | r0 | r0 | r0 | r0 | r0 | fe230 | fe230 | fe230 |
| 35 | GetForeignData(iForeignData) (mesh: -1 -> +544) | 376e0 | =base | =base | =base | =base | =base | fe2b0 | fe2b0 | fe2b0 |
| 36 | GetiForeignData (+40) | 1bdcb0 | =base | =base | =base | =base | =base | =base | =base | =base |
| 37 | SetForeignData(data, id) | 376f0 | =base | =base | =base | =base | =base | fe2d0 | fe2d0 | fe2d0 |
| 38 | mesh: returns +540 (subtractions counter, inferred) | r0 | r0 | r0 | r0 | r0 | r0 | fe2a0 | fe2a0 | fe2a0 |
| 39 | mesh: DestroyAuxData(mask: 1 mats, 2 foreignIdx, 4 vtxMap) (inferred) | nop | nop | nop | nop | nop | nop | fe300 | fe300 | fe300 |
| 40 | mesh: RemapForeignIdx(cur[], new[], n) (inferred) | nop | nop | nop | nop | nop | nop | 1cd640 | 1cd640 | 1cd640 |
| 41 | mesh: AppendVertices(pVtx, pVtxMap, n) (inferred) | nop | nop | nop | nop | nop | nop | 1cdd20 | 1cdd20 | 1cdd20 |
| 42 | GetExtent(form) – 0:8 / 1:edge len / 2:area / 3:volume (particle emit, inferred) | 14df30 | =base | 1aebb0 | 141dd0 | 141dd0 | =base | 1c2040 | 1c2040 | 1c2040 |
| 43 | GetRandomPos(PosNorm&, seed, form) (inferred) | 151a10 | =base | 1af140 | 145120 | 145120 | =base | 1d3460 | 1d3460 | 1d3460 |
| 44 | (unused, nop) | nop | nop | nop | nop | nop | nop | nop | nop | nop |
| 45 | mesh: CompactVertices (drop unreferenced, inferred) | nop | nop | nop | nop | nop | nop | 1bf180 | 1bf180 | 1bf180 |
| 46 | GetBVTree() (prims: &embedded CSingleBoxTree / CRayBV; mesh: +48 ptr; hf +728; voxel +824) | r0 | 75250 | 38cc0 | 38e40 | 38e40 | 38e40 | 1db390 | fe3a0 | 14dad0 |
| 47 | IsAPrimitive() (1 = box/sph/cyl/cap/ray) | r0 | r1 | r1 | r1 | r1 | r1 | r0 | r0 | r0 |
| 48 | PreparePrimitive(gwd, primitive** out, iCaller) -> prim type (world-space prim in per-thread arena) | r-1 | 13b580 | 1af380 | 142240 | 38b60 | 19a020 | 1c2a60 | 1c2a60 | 1c2a60 |
| 49 | UnprojectSphere(center, r, rsep, contact*) (box: overlap[0][4] + unproj[0][0][4]) (inferred) | r0 | 13afc0 | 1afa60 | 142c10 | 140aa0 | r0 | 1d8ea0 | 1d8ea0 | 1d8ea0 |
| 50 | GetTriMesh(bClone) (mesh: this or copy; box 0) (inferred) | r0 | 13a410 | r0 | r0 | r0 | r0 | fe240 | fe240 | fe240 |
| 51 | rasterise prim range into occlusion cubemap – leaf worker of 23 (inferred) | r0 | 13eef0 | 1b0600 | 144aa0 | 144aa0 | r0 | 1cd250 | 156430 | 1dd2b0 |
| 52 | GetSizeFactor / sizeof (box 200, sph 152, cyl/cap 176, ray 112) | r0 | 75290 | 38d00 | 38a30 | 38a30 | 38e60 | r0 | r0 | r0 |
| 53 | PrepareForIntersectionTest(geometry_under_test*, IGeometry* other, gtest_other, bKeepPrevContacts) | r1 | 13a330 | 1ae910 | 141cc0 | 13f750 | 19a5c0 | 1d3620 | 155180 | 1d3620 |
| 54 | RegisterIntersection(prim1, prim2, gtest1, gtest2, prim_inters*) -> fills geom_contact (unprojection) | 14f3f0 | =base | =base | =base | =base | 19a380 | 1d3950 | 1d3950 | 1d3950 |
| 55 | CleanupAfterIntersectionTest(gtest) | nop | nop | nop | nop | nop | nop | 1bdf60 | 1bdf60 | 1bdf60 |
| 56 | GetPrimitiveList: prims of a BV leaf in gtest frame (called from BV slot 23) (inferred) | r0 | 13b810 | 1af480 | 1423d0 | 1423d0 | 19a340 | 1c3c60 | 1c3c60 | 1c3c60 |
| 57 | GetUnprojectionCandidates(iSide, contact, prim**, iFeature*, gtest) (inferred) | 37720 | 13baf0 | 1af570 | 142410 | 13f9a0 | 19a2f0 | 1c3380 | 1c3380 | 1c3380 |
| 58 | feature-polygon helper A (border/contact-area, polyhedral prims) (inferred) | r0 | 13c1c0 | r0 | 1455a0 | 1455a0 | r0 | 1c4610 | 1c4610 | 1c4610 |
| 59 | feature-polygon helper B (border/contact-area) (inferred) | r0 | 13c990 | r0 | 1427d0 | 1427d0 | r0 | 1c5b90 | 1c5b90 | 1c5b90 |
| 60 | mesh only: CompactMemory(bLocked) -> BV slot 2 (inferred) |  |  |  |  |  |  | 103390 | 103390 | 103390 |
| 61 | mesh only: PointInsideStatus(pt, bLocal) (inferred) |  |  |  |  |  |  | 1c8600 | 1c8600 | 1c8600 |

**Notes**
- The box's buoyancy (13) and pressure (12) convert the box to a temporary 12-triangle CTriMesh (sub_3513AC00) and call the mesh versions. Medium resistance (14) has its own box code.
- Capsule slot 13 (`13f790`) is a thunk to the cylinder's slot 13.
- Primitives use one shared Intersect, `0x35151f20` (IDA name `CCylinderGeom__vf10`). If `other->IsAPrimitive()` (slot 47) is 0, it falls back to the generic `CGeometry::Intersect` 0x3514fd70.
- CTriMesh, CHeightfield and CVoxelGeom Intersect special-case a ray (`other->GetType()==3`):
  - CTriMesh: only when the ray grid is built (+476>0); otherwise it uses the generic path.
  - CHeightfield: grid ray-march sub_35158BA0, then tri-vs-ray.
  - CVoxelGeom: own walk.
  - Every other pair goes through the generic tree-vs-tree path (§3.4).

### 2.2 x86 1.2 slot → implementation (same slot roles)

In this table `=base` means the same address as the x86 CGeometry slot.

| slot | CGeometry | CBoxGeom | CSphereGeom | CCylinderGeom | CCapsuleGeom | CRayGeom | CTriMesh | CHeightfield | CVoxelGeom |
|---|---|---|---|---|---|---|---|---|---|
| 0 | 35029960 | 3505c1a0 | 3502b950 | 3502b680 | 3502b7d0 | 3502bc50 | 351d82f0 | 3510cbe0 | 35154460 |
| 1 | 351f4f9a | r0 | 3502b890 | 3502b5b0 | 3502b750 | 3502b9c0 | r1 | 3510cb90 | 35154400 |
| 2 | 350293f0 | =base | =base | =base | =base | =base | =base | =base | =base |
| 3 | 351531c0 | =base | =base | =base | =base | =base | =base | =base | =base |
| 4 | 350298a0 | =base | =base | =base | =base | =base | =base | =base | =base |
| 5 | 35029920 | =base | =base | =base | =base | =base | =base | =base | =base |
| 6 | 351f4f9a | 3505c0b0 | 3502b8a0 | 3502b5c0 | 3502b5c0 | 351a57f0 | 3510cd10 | 3510cd10 | 3510cd10 |
| 7 | 35029400 | 35146950 | 351c50e0 | 3514a360 | 35147060 | =base | 351eef90 | 351eef90 | 351eef90 |
| 8 | 35029420 | 35140da0 | 351c5170 | 35149830 | 35146c60 | =base | 351d4d00 | 3515d050 | 35154410 |
| 9 | 35029720 | =base | =base | =base | =base | =base | =base | =base | =base |
| 10 | 35158610 | 3515a6d0 | 3515a6d0 | 3515a6d0 | 3515a6d0 | =base | 351dfbe0 | 3515fe80 | 351f2370 |
| 11 | 35029410 | 351441c0 | 351c5bf0 | 35150950 | 35147600 | =base | 351dcc60 | 3515e810 | 351dcc60 |
| 12 | 35029430 | 35141d90 | 351c5780 | 3514b760 | 3514b760 | =base | 351e24d0 | 3515ce30 | 35154420 |
| 13 | 35029800 | 35141e30 | 351c5570 | 3514ba80 | 35146e30 | =base | 351ef500 | 351ef500 | 351ef500 |
| 14 | 35029820 | 35145950 | 351c6160 | 3514d360 | 35148b10 | =base | 351d7250 | 351d7250 | 351d7250 |
| 15 | 350297c0 | 35141ec0 | 351c58f0 | 3514a060 | 3514a060 | =base | 351d76d0 | 351d76d0 | 351d76d0 |
| 16 | 35029440 | =base | =base | =base | =base | =base | 3510c980 | 3510c980 | 3510c980 |
| 17 | 351f4f9a | 3505c130 | 3502b8c0 | 3502b5e0 | 3502b5e0 | 3502b9f0 | 351544c0 | 3510cf80 | 351544c0 |
| 18 | 35029450 | =base | =base | =base | =base | =base | 3510c9a0 | 3510c9a0 | 3510c9a0 |
| 19 | 35029850 | =base | =base | =base | =base | =base | 3510c9c0 | 3510c9c0 | 3510c9c0 |
| 20 | 350294c0 | 35140ee0 | =base | =base | =base | =base | 351d4ed0 | 351d4ed0 | 351d4ed0 |
| 21 | 35029460 | =base | =base | =base | =base | =base | 351da490 | 3510cba0 | 35154430 |
| 22 | 35029470 | =base | =base | =base | =base | 3502bb90 | 351df440 | 3510cbb0 | 35154440 |
| 23 | 35157510 | =base | =base | =base | =base | =base | =base | =base | =base |
| 24 | 35029590 | 35140d80 | 351c5000 | 35149810 | 35149810 | =base | 351d7a30 | 3515ce40 | 351f1600 |
| 25 | 350295a0 | 35146890 | 351c6f50 | 3514f8b0 | 3514f8b0 | =base | 351e4e40 | 351e4e40 | 351e4e40 |
| 26 | 350295c0 | =base | =base | =base | =base | =base | 351ed480 | 351ed480 | 351ed480 |
| 27 | 350295b0 | 351468f0 | 351c6fb0 | 3514f930 | 3514f930 | =base | 3510ce50 | 3510ce50 | 3510ce50 |
| 28 | r1 | =base | =base | =base | =base | =base | 3510c9f0 | 3510c9f0 | 3510c9f0 |
| 29 | 351f4f9a | 3505c0d0 | 3502b8f0 | 3502b620 | 3502b620 | 3502ba20 | 351543f0 | 3510cbd0 | 351543f0 |
| 30 | 35029500 | 3505c0e0 | 3502b900 | 3502b630 | 3502b760 | 3502bab0 | =base | =base | =base |
| 31 | r0.0 | 3505c120 | 3502b940 | 3502b670 | 3502b790 | =base | 351f0300 | 351f0300 | 351f0300 |
| 32 | 35029880 | 3505c0f0 | 3502b910 | 3502b640 | 3502b640 | 3502ba40 | 351d4d30 | 3510ce90 | 351d4d30 |
| 33 | 350294e0 | =base | =base | =base | =base | =base | 35123d40 | 35123d40 | 35123d40 |
| 34 | r0 | =base | =base | =base | =base | =base | 3510ca00 | 3510ca00 | 3510ca00 |
| 35 | 35029530 | =base | =base | =base | =base | =base | 3510cab0 | 3510cab0 | 3510cab0 |
| 36 | 35029550 | =base | =base | =base | =base | =base | =base | =base | =base |
| 37 | 35029560 | =base | =base | =base | =base | =base | 3510cae0 | 3510cae0 | 3510cae0 |
| 38 | r0 | =base | =base | =base | =base | =base | 3510caa0 | 3510caa0 | 3510caa0 |
| 39 | 35029670 | =base | =base | =base | =base | =base | 3510cb20 | 3510cb20 | 3510cb20 |
| 40 | 35029680 | =base | =base | =base | =base | =base | 351e3820 | 351e3820 | 351e3820 |
| 41 | 35029690 | =base | =base | =base | =base | =base | 351e3e00 | 351e3e00 | 351e3e00 |
| 42 | 351569a0 | =base | 351c51e0 | 35149790 | 35149790 | =base | 351da040 | 351da040 | 351da040 |
| 43 | 3515a560 | =base | 351c59d0 | 3514a610 | 3514a610 | =base | 351e9220 | 351e9220 | 351e9220 |
| 44 | 350296a0 | =base | =base | =base | =base | =base | =base | =base | =base |
| 45 | nop | =base | =base | =base | =base | =base | 351d7e50 | 351d7e50 | 351d7e50 |
| 46 | r0 | 3505c0c0 | 3502b8b0 | 3502b5d0 | 3502b5d0 | 3502b9e0 | 351d4d20 | 3510cbc0 | 35154450 |
| 47 | r0 | r1 | r1 | r1 | r1 | r1 | =base | =base | =base |
| 48 | 350294b0 | 35142410 | 351c5230 | 35149ad0 | 3502b770 | 351a5240 | 351d5280 | 351d5280 | 351d5280 |
| 49 | 350294d0 | 35145870 | 351c5ff0 | 3514b550 | 351486b0 | =base | 351eddc0 | 351eddc0 | 351eddc0 |
| 50 | 35029510 | 35140d70 | =base | =base | =base | =base | 3510ca10 | 3510ca10 | 3510ca10 |
| 51 | 35029580 | 35146130 | 351c6b70 | 3514f6f0 | 3514f6f0 | =base | 351d7380 | 3515eea0 | 351f1d10 |
| 52 | r0 | 3505c110 | 3502b930 | 3502b660 | 3502b660 | 3502ba30 | =base | =base | =base |
| 53 | 35029600 | 35140cb0 | 351c4f60 | 351496b0 | 35146c20 | 351a5900 | 351e9370 | 3515d6c0 | 351e9370 |
| 54 | 351578c0 | =base | =base | =base | =base | 351a55d0 | 351e9650 | 351e9650 | 351e9650 |
| 55 | 35029610 | =base | =base | =base | =base | =base | 351d46f0 | 351d46f0 | 351d46f0 |
| 56 | 35029620 | 35142760 | 351c5340 | 35149c70 | 35149c70 | 351a55a0 | 351d5ae0 | 351d5ae0 | 351d5ae0 |
| 57 | 35029630 | 351429e0 | 351c5450 | 3514ab30 | 35147340 | 351a5550 | 351da6f0 | 351da6f0 | 351da6f0 |
| 58 | 35029650 | 35143390 | =base | 3514aea0 | 3514aea0 | =base | 351daf30 | 351daf30 | 351daf30 |
| 59 | 35029660 | 35143b40 | =base | 35149ca0 | 35149ca0 | =base | 351dc260 | 351dc260 | 351dc260 |
| 60 | | | | | | | 3510cdb0 | 3510cdb0 | 3510cdb0 |
| 61 | | | | | | | 351df070 | 351df070 | 351df070 |

The x86 thiscall stubs (`ret N`) were not auto-classified. x86 vtables: CGeometry 0x351fa930, Box 0x351fba30, Sphere 0x351fae30, Cylinder 0x351fac48, Capsule 0x351fad38, Ray 0x351faf20, TriMesh 0x351fe350, Heightfield 0x351fd788, Voxel 0x351fde60.

### 2.3 Other structures seen at the geometry interface

**phys_geometry** (written by slot 7; field names inferred)
- `{IGeometry* pGeom @0; Vec3 Ibody @8; quat q @20; Vec3 origin @36; float V @48; …}`

**geom_world_data** (inferred)
- `{Vec3 offset @0; Matrix33 R @12 (row-major); float scale @48; Vec3 v @52; Vec3 w @64; Vec3 centerOfMass @76}`

**intersection_params**: see §5.1 for the full table. Key fields: `+0` iUnprojectionMode, `+28` time_interval, `+96` bSweepTest, `+128` contacts out.

**geometry_under_test ("gtest", 392 bytes per side, inferred)**
- +0 geom, +8 BV tree, +16 used-node map, +40 stop flag
- +48 R, +120 offset, +144 scale (the transform used by slots 56/57)
- +160 flag
- +164 v, +176 w, +188 centerOfMass, +200 rotation axis
- +216 intersection_params*
- +280 prim type, +288 prim buffer, +296 unprojection prim buffer, +304 nprims, +320 id buffer, +328 prim stride, +336/+344 aux buffers
- +360 minPtDist
- +368 contacts array, +376 int* pnContacts, +384 nMaxContacts, +388 iCaller

---------------------------------------------------------------------------------------------------

## 3. BV trees (26-slot vtable)

**x86 vtables:** CBVTree 0x351fa7a0, CAABBTree 0x351fe2e8, COBBTree 0x351fe280, CSingleBoxTree 0x351fabe0, CHeightfieldBV 0x351fd720, CVoxelBV 0x351fddc8, CRayBV 0x351fa8d0 (only 22 slots detected for CRayBV).

**x64 vtables:** CBVTree 0x351edc68, CAABBTree 0x351f30e8, COBBTree 0x351f3008, CSingleBoxTree 0x351ee4a8, CHeightfieldBV 0x351f2068, CVoxelBV 0x351f2988, CRayBV 0x351edec8.

All roles below are (inferred) unless noted.

### 3.1 Slot table

Stubs: `r0` 0x350376d0, `r1` 0x35076030, `nop` 0x35037700, `r0.0` 0x35024c80, `pure` 0x351eafee.

| # | role (inferred) | base | CAABBTree | COBBTree | CSingleBoxTree | CHeightfieldBV | CVoxelBV | CRayBV |
|---|---|---|---|---|---|---|---|---|
| 0 | scalar deleting dtor | 35024ca0 | 351db250 (frees +16, +96) | 351bdce0 (frees +16, +32) | base | 350fe160 (frees +48) | base | base |
| 1 | GetType (BV type id) | pure | r1 → 1 | r0 → 0 | 350fe380 → 2 | 35037c40 → 4 | 35038a00 → 5 | 35024cd0 → 3 |
| 2 | GetBBox(box*): fills the 64-B box prim | nop | 351365f0 | 35194210 (root node) | 351ae1d0 | 35154010 (cell rect × step) | 351db810 | nop |
| 3 | MaxPrimsInNode | r1 | 351db220 (+116) | 351bdcb0 (+40) | 35038940 (+80) | 350fe150 (2·w·h) | 3514d720 (+48) | r1 |
| 4 | SetGeometry / Build(geom) → float cost | pure | 35139490 | 35195210 | 351adf90 (8·vol) | 35153f60 | 35024ce0 (store +8) | 35024ce0 |
| 5 | SetGeomConvex (mark all nodes "single-collide") | nop | 35136410 (node.flag=1) | 35191ea0 (maxSkip = 10·Σroot.size) | 35038930 (+84 = 1) | nop | nop | nop |
| 6 | PrepareForIntersectionTest(gtest[, collider geom, collider gtest]) | 35024c60 (clears map) | 35136780 | 351921e0 | 351adf70 (map = &gtest+32) | base | 351dc3b0 (cell range of collider bbox, swept if needed) | base |
| 7 | CleanupAfterIntersectionTest(gtest): clear used-node bits | nop | 35136520 | 35191fa0 | nop | nop | 351db6f0 | nop |
| 8 | GetNodeBV(R, off, scale, BV**, sweepDir, sweepLen, iNode, iCaller): transformed + swept | pure | nop | nop | 351ae620 | nop | 3514d740 (→ 9) | nop |
| 9 | GetNodeBV(R, off, scale, BV**, iNode, iCaller): root → world | pure | 35139de0 | 351945d0 | 351ae3f0 | 351549a0 | 351dc630 | 35199dc0 |
| 10 | GetNodeBV(BV**, sweepDir, sweepLen, iNode, iCaller): swept local root | pure | 35137c00 (136-B BV) | 35194420 | 351ae080 | nop | 3514d730 (→ 11) | nop |
| 11 | GetNodeBV(BV**, iNode, iCaller): local | pure | 35137b50 (root; iNode ignored) | 351942d0 (iNode clamped to nNodes−1) | 351adfc0 | 351540d0 | 351db8c0 | 35199d40 |
| 12 | SplitPriority(BV*) = size.x·y·z if internal, else 0 | r0.0 | 35136660 | 35192070 | r0.0 | r0.0 | r0.0 | r0.0 |
| 13 | GetNodeChildrenBVs, swept (parent, dir, len, &l, &r, iCaller) | nop | 351387f0 | 35194f80 | nop | nop | nop | nop |
| 14 | GetNodeChildrenBVs, local (parent, &l, &r, iCaller) | nop | 351381f0 | 35194d70 | nop | nop | nop | nop |
| 15 | GetNodeChildrenBVs, transformed (R, off, scale, parent, &l, &r, iCaller) | nop | 35137cd0 | 35194890 | nop | nop | nop | nop |
| 16 | ReleaseLastBVs(iCaller): BV-pool counter −= 2 | nop | 35136440 | 35191ed0 | nop | nop | nop | nop |
| 17 | ReleaseLastSweptBVs(iCaller) | nop | 35136460 (swept pool −= 2) | 35191ef0 (normal pool −= 2) | nop | nop | nop | nop |
| 18 | ResetCollisionArea (region = whole grid) | nop | nop | nop | nop | nop | 3514d770 | nop |
| 19 | GetMaxSkipDim (maxSkip / max root size) | r0.0 | 351db2c0 | 351db320 | r0.0 | r0.0 | r0.0 | r0.0 |
| 20 | GetMemoryStatistics(sizer) | nop | 351368c0 ("AABB trees") | 35192310 ("OBB trees") | 351ae240 ("SingleBox trees", 88) | nop | nop | nop |
| 21 | Save(stream) | nop | 35138e80 | 351923a0 | 351ae320 | nop | nop | nop |
| 22 | Load(stream, geom) | nop | 351390a0 | 35192510 | 351ae380 | nop | nop | nop |
| 23 | GetNodeContents(iNode, colliderBV, bColliderUsed, bColliderLocal, gtest, gtestOp) | pure | 35136480 | 35191f10 | 351adf00 | 351546a0 | 351dc060 | 35199cd0 |
| 24 | GetNodeContentsIdx(iNode, &iStartPrim) → nPrims | 35024c90 | 351db230 | 351bdcc0 | 35038950 | base | base | base |
| 25 | MarkUsedTriangle(iTri, gtest) | nop | 351366a0 | 351920b0 | 351ae1b0 | 35153fa0 (bit in +48) | 351db7c0 | nop |

**Slot 23 dispatch.** Every class ends up calling geometry slot 56 (GetPrimitiveList, vtable +448) with `(firstPrim, nPrims, colliderType, colliderPrim, bLocal, gtest, gtestOp, gtest+288 prim buffer, gtest+320 id buffer)`. The heightfield and voxel BVs generate triangles for the overlapped cells here.

**x86 1.2 BV addresses** (from func_match, sim ≥ 3):

| class | slot:x86 |
|---|---|
| CBVTree | 0:35029140, 6:35029030, 24:35029110 |
| CAABBTree | 0:351d4a10, 2:3513d1a0, 3:351d49c0, 4:351403d0, 5:3513ce00, 6:3513e550, 7:3513ced0, 9:3513fb10, 10:3513d2b0, 11:3513d210, 12:3513d350, 13:3513de60, 14:3513d8b0, 15:3513d380, 16:3513ce30, 17:3513ce50, 19:351d4a70, 20:3513e650, 21:3513fe10, 22:35140030, 23:3513ce70, 24:351d49e0, 25:3513e4a0 |
| COBBTree | 0:351d45b0, 2:3519e600, 4:351a1990, 5:3519e1d0, 6:3519e470, 7:3519e290, 9:3519e900, 10:3519e7d0, 11:3519e6b0, 12:3519e330, 13:3519f430, 14:3519f270, 15:3519ec40, 16:3519e1f0, 17:3519e210, 19:351d4ac0, 20:3519e560, 21:3519f630, 22:3519f760, 23:3519e230, 24:351d4580, 25:3519e360 |
| CSingleBoxTree | 1:3502b4d0, 2:351c4660, 3:3502b4f0, 4:351c4480, 5:3502b4e0, 6:351c4460, 8:351c4b30, 9:351c4840, 10:351c4550, 11:351c44a0, 20:351c46d0, 21:351c4eb0, 22:351c4f20, 23:351c4410, 24:3502b500, 25:351c4640 |
| CHeightfieldBV | 0:3510c900, 2:3515c420, 3:3510c8d0, 4:3515c3c0, 9:3515c5d0, 11:3515c500, 23:3515cc60, 25:3515c3f0 |
| CVoxelBV | 2:351f04a0, 3:351534b0, 6:351f13f0, 7:351f0400, 8:351534e0, 9:351f0830, 10:351534c0, 11:351f05d0, 18:35153540, 23:351f10a0, 25:351f0580 |
| CRayBV | 9:351a50b0, 11:351a5050, 23:351a5010 |

### 3.2 Object layouts

**CAABBTree** (sizeof 128)

| off | field |
|---|---|
| +8 | geometry |
| +16 | node array (12 B per node) |
| +24 | nNodes |
| +28 | nNodesAlloc (2·nTris+1, grows by 32, shrunk to fit at the end) |
| +32 | root centre |
| +44 | root half-size (each axis clamped ≥ 0.001·max) |
| +56 | root basis Matrix33 |
| +92 | short bOriented |
| +94 | short split-axis restriction (−1 = off; 0 = compute at build; else axisA \| axisB<<2) |
| +96 | tri→leaf map, bit-packed (8 bits if nNodes ≤ 256, else 16) |
| +104 | log2(bits per map entry) |
| +108 | max tris per leaf |
| +112 | min tris per side |
| +116 | actual largest leaf |
| +120 | maxSkipDim (relative input, × max root half-size during build) |

**COBBTree** (sizeof 72)

| off | field |
|---|---|
| +8 | geometry |
| +16 | node array (72 B per node) |
| +24 | nNodes |
| +28 | nNodesAlloc (starts at 256, grows by 256) |
| +32 | int[nTris] tri→leaf node |
| +40 | largest leaf tri count |
| +44 | min tris per side (ctor sets 2) |
| +48 | max tris per leaf (ctor sets 4) |
| +52 | maxSkipDim |
| +56 / +64 | build-only scratch |

**Other trees**
- **CSingleBoxTree** (sizeof 88, embedded in primitive geoms): +8 geometry; +16 box prim (basis +16, bOriented +52, centre +56, size +68); +80 nPrims; +84 bIsConvex.
- **CHeightfieldBV** (at hf +728): +8 geometry; +16 hf prim ptr; +24/+28 origin cell (ix, iy); +32/+36 region w/h in cells; +40/+44 hmin/hmax; +48 used-tri bitmask.
- **CVoxelBV**: +8 geometry; +16 voxel prim ptr; +24..+44 cell range (ixmin, iymin, izmin, ixmax, iymax, izmax); +48 nPrims.
- **CRayBV**: +8 geometry; +16 ray prim ptr.

### 3.3 Node layouts

**AABB node (12 B).** Bounds are quantised to 7 bits relative to the parent box, in the tree basis.

| off | type | meaning |
|---|---|---|
| +0 | int | ichild (children are ichild and ichild+1, allocated in pairs) or first tri for a leaf |
| +4..+9 | u8 ×6 | minx, maxx, miny, maxy, minz, maxz, each 0..127 |
| +10 | u8 | nTris (0 = internal) |
| +11 | u8 | bSingleColl (leaf max dim < maxSkipDim, or set by slot 5) |

- **Decode** (parent half-size s): `lo = q·s/64`, `hi = (q+1)·s/64`; `childCentre = parentCentre + ((lo+hi)/2 − s)` (rotated by the basis if oriented); `childSize = (hi−lo)/2`. See sub_3513A120 and slots 13–15.
- **Encode** (sub_351369E0): `q = clamp(⌊(64/s)·(bound − (parentCentre − s))⌋ …, 0, 127)`. An axis with `s ≤ 0.001·max` is stored as 0 / 127.
- **Legacy stream format** (slot 22, when the first dword ≥ 0): three packed dwords per node — w0 = minx:7 \| maxx:7 \| miny:7 \| maxy:7; w1 = minz:7 \| maxz:7 \| ichild:15 (bit 14); w2 = ntris:6 \| bSingleColl:1. This is presumably the "AABBnode" string's struct. The current format writes −1, nNodes, then the raw 12-B array.

**OBB node (72 B)**

| off | meaning |
|---|---|
| +0 | axes Matrix33 (rows = unit axes) |
| +36 | centre |
| +48 | half-size |
| +60 | iParent (−1 for root) |
| +64 | ichild, or first tri for a leaf |
| +68 | nTris (0 = internal) |

- Node 0 is the root.
- MarkUsed (slot 25) climbs iParent while the sibling (i^1) is also marked, only for nodes with max size < maxSkipDim.

**Other "nodes"**: SingleBox = one 64-B box; heightfield = cell rectangle {ix, iy, w, h} plus z-range; voxel = integer 3D cell range.

### 3.4 Temp BV records, arena, and traversal

**Per-caller arena layout** (base 0x35256F80 + iCaller·0x39FB8, 5 slots; static initialiser 0x35153F10)

| arena off | content |
|---|---|
| 0x46B0 | used-node bitmask pool (0x2000 dwords); counter at 0xC6B0 |
| 0xC6B4 | used-node index lists (64 ints); counter at 0xC7B4 |
| 0x2C9DC | BV pool: 512 × 72 B; counter at 0x359DC |
| 0x359E0 | swept-AABB BV pool: 128 × 136 B; counter at 0x39DE0 |
| 0x39DE8 | heightfield BV record (type 2) |
| 0x39E68 | voxel BV record (type 7) |
| 0x39F48 | ray BV record (type 3) |
| 0x39F68 | overlap scratch (3rd argument of OVERLAP) |

**BV record (72 B)**: `{int type; int iNode; box{Matrix33 basis @8; int bOriented @44; Vec3 centre @48; Vec3 size @60}}`. Child BVs are taken and released two at a time. OBB child BVs set bOriented = iNode+1 (always non-zero).

**Swept AABB BV (136 B)**: `{int type, iNode; box swept @8; box orig @72}`. Built by sub_35135490 from a box, unit direction d and length L: an oriented box with one axis along d (the second axis is the box axis with the largest perpendicular extent), half-length along d = `proj + |L|/2`, centre shifted by `d·L/2`.

**Tree-vs-tree traversal: sub_3514DFD0** (x86 0x35156af0)

Driver: CGeometry::Intersect (slot 10) 0x3514fd70 (x86 0x35158610).
1. Call slot 53 (PrepareForIntersectionTest) on both geometries; for trees this reaches BV slot 6.
2. Get root A with BV slot 11 (local frame; A starts at a cached node and restarts at 0 if there are no contacts) and root B with BV slot 9 (transformed into A's frame).
3. Call `traverse(ctx, bvA, bvB)`.
4. Call slot 55 (cleanup) on both.

ctx is two 392-B gtest records: A at +0, B at +392.

`traverse(A, B)`:
1. If both nodes are already set in their used-node maps (`map[iNode>>5]` bit), return 0.
2. If `OVERLAP[A.type][B.type](&A.prim, &B.prim, scratch) == 0`, return 0 (call at 0x3514e0df).
3. Compute `vA = BV slot12(A)`, `vB = BV slot12(B)` (0 for leaves).
4. **Descend into the node with the larger volume:**
   - If `vA > vB` and `vA > 0`: children of A via slot 14; recurse (A1,B); if neither stop flag (gtest+40) is set, recurse (A2,B); then slot 16.
   - Else if `vB > 0`: children of B via slot 15 (with B's R/offset); recurse (A,B1), then (A,B2) under the same early-out; then slot 16.
5. **Both leaves:**
   - `nA = slot23` on tree A (colliderBV = B, bLocal = 1); `nB = slot23` on tree B (colliderBV = A, bLocal = 0). These fill the prim/id buffers.
   - Double loop over prim pairs with `INTERSECT[tA][tB]` (0x35209D50). On a hit, call slot 54 (RegisterIntersection) on the higher-priority geometry (swap if `gtB+20 − gtA+20 < 0`).
   - Return as soon as a stop flag is raised.
   - The merge tolerance is `min(geomA+16, geomB+16)²`.

**Other traversals**
- **Sweep variant: sub_3514E580** (x86 0x35156f70). Same descend rule, but A's root comes from slot 10, children from slot 13, release from slot 17. At leaves it calls UNPROJECT (call at 0x3514e991) and keeps the minimum-t contact.
- **Tree vs sphere: sub_3514DCF0** (x86 0x35156350) and **sub_3514DE30** (x86 0x35156440). They use `OVERLAP[type][sph]` (0x3520A400), split via slot 14 and read leaves via slot 24. 3514DCF0 calls geometry slot 51 at leaves (cubemap rasteriser); DE30 returns the first leaf prim + 1.

Inc/dec-mod-3 index tables: {1,2,0} at 0x35209D30, {2,0,1} at 0x35209D40.

### 3.5 Building (CTriMesh constructor sub_351CF1B0, x86 0x351e53d0)

**Mesh flag bits:**

| bit | effect |
|---|---|
| 0x40 | axis-aligned AABB tree |
| 0x40000 | AABB tree in the principal-axis basis (hull sub_35128440 + covariance fit sub_35195400); cost × 1.01 |
| 0x20 | OBB tree; cost × a float ctor argument |
| 0x400 / 0x100 / neither | maxSkipDim = 0 / 10.0 / 0.1 |
| 0x4000 | flat-axis split restriction |

- Ctor arguments set min tris per side and max tris per leaf.
- Each candidate tree is built on a copy of the index/material/id/+160 arrays, because building permutes them. The tree with the lowest Build() cost is kept.
- Load (mesh slot 26, 0x351d83b0) reads the tree type id (0 OBB, 1 AABB, 2 SingleBox), then calls BV slot 22, and slot 5 if the mesh is convex.

**AABB build** (slot 4 0x35139490, recursion sub_351369E0, x86 0x3513e800):
1. Per-tri bbox + centroid in the tree basis.
2. Leaf if `n ≤ maxLeaf`.
3. Otherwise, for each candidate axis (all 3, or the 2 "large" axes when one axis is < 1/10 of the others), partition by centroid against the box centre.
4. Score = `childExtentAlongAxis × crossSectionArea`, penalised by `8·size` if a side has fewer than minTris. Pick the minimum.
5. Partition in place; if a side has fewer than minTris, make a leaf; otherwise allocate a child pair and recurse.
6. Build returns `8·Σvol` (+1e10 if a leaf has > 63 tris, because of the 6-bit legacy count).

**OBB build** (slot 4 0x35195210, recursion sub_35192660, x86 0x351a0260):
1. Collect the node's unique vertices; at depth ≤ 4 with > 100 tris, take their hull first.
2. Fit an OBB by double-precision covariance.
3. Split along the node's own axes (axes ≥ 10× the smallest) by centroid; choose by volume.
4. Leaf when `n ≤ maxLeaf` or a side has fewer than minTris; children are allocated in pairs with iParent set.

### 3.6 BV uncertainties

- **AABB used-node marking looks disabled.** AABB slot 6 always writes 0 to gtest+16 (0x3513689a), so AABB used-node marking and the both-used skip never fire. This looks like a bug or deliberate disable; OBB does not do this.
- **Slots 12 and 19** are inferred; the caller of slot 19 was not traced.
- **Slot 3 semantics differ per class.**
- **The OBB split heuristic** was only skimmed.
- **CRayBV in x86** shows only 22 slots.

---------------------------------------------------------------------------------------------------

## 4. Intersection dispatch tables

There are three static `.rdata` tables of function pointers, with no init code; they are indexed by the geometry type ids from §1.1. The x86 1.2 tables have exactly the same shape and contents order.

| table | x64 | x86 | shape | index | signature (inferred) |
|---|---|---|---|---|---|
| OVERLAP (bool, BV/prim) | 0x3520A3E0 | 0x35204808 | [8][8] | `tA*8+tB` | `int f(primA*, primB*, scratch*)` |
| INTERSECT (contact) | 0x35209D50 | 0x352044A8 | [8][8] | `tA*8+tB` | `int f(primA*, primB*, prim_inters* out)` |
| UNPROJECT | 0x35209FA0 | 0x352045D8 | [2][8][8] | `mode*64 + tA*8 + tB` | `int f(unproj_params*, primA*, iFeatureA, primB*, iFeatureB, contact* out(t double @0), border*)` |

- Between the tables are small const blocks (`{1,2,0,…}` int tables at 0x35209D30/40/F50, 0x3520A3A0, …).
- Decompiler aliases: `funcs_3514E0DF` = OVERLAP, `funcs_3514E3F7` = INTERSECT, `funcs_3514E991` = UNPROJECT, `off_3520A400` = OVERLAP column [*][sphere].
- Call sites: OVERLAP at 0x3514e0df (tree traversal) and in the shared primitive Intersect 0x35151f20; INTERSECT at 0x3514e3f7; UNPROJECT at 0x3514e991, and also in 0x3514f3f0, 0x35151f20, 0x35156e20, 0x351c9910, 0x351d3950 and 0x351dda80.

Rows and columns are typeA / typeB = box 0, tri 1, hf 2, ray 3, sph 4, cyl 5, cap 6, vox 7. Cells are `x64 / x86`; `ret0` / `ret1` are stub functions.

### 4.1 INTERSECT (contact generation) 0x35209D50

| A \ B | box | tri | hf | ray | sph | cyl | cap | vox |
|---|---|---|---|---|---|---|---|---|
| **box** | 3516ae00 / 351630f0 | 35164780 / 3516f1a0 | ret0 | 3515f110 / 351673e0 | 3515d460 / 35161140 | 3515ba70 / 35163d70 | 3515d8a0 / 35165a90 | ret0 |
| **tri** | 35162aa0 / 3516d020 | 35159410 / 351614e0 | ret0 | 3515b1d0 / 35163080 | 35159de0 / 35161f10 | 35164830 / 3516f230 | 35168280 / 35172fc0 | ret0 |
| **hf** | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 |
| **ray** | 3515f690 / 35167850 | 3515ab20 / 35162a60 | ret0 | ret0 | 35161180 / 3516bb40 | 35160970 / 3516b640 | 35162a50 / 3516cfe0 | ret0 |
| **sph** | 3515d820 / 35161470 | 3515aaa0 / 35162a00 | ret0 | 35160ea0 / 3516b850 | 35160cb0 / 3516b680 | 3515fdf0 / 351698d0 | 35161ea0 / 3516c480 | ret0 |
| **cyl** | 3515d3f0 / 35165a20 | 35168200 / 35172f60 | ret0 | 3515fe40 / 3516ab00 | 3515fa20 / 35169520 | 3516b7f0 / 35167890 | 3516d2e0 / 35169910 | ret0 |
| **cap** | 3515f0a0 / 35167370 | 3516ad80 / 351759a0 | ret0 | 35161ef0 / 3516c4c0 | 35161c80 / 3516c240 | 3516e320 / 3516aac0 | 35161550 / 3516bb80 | ret0 |
| **vox** | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 |

**Unique implementations (20).** The other non-stub cells are swap wrappers that call the mirrored pair with `(b, a, out)` and then fix up the output.

| pair | x64 | size | | pair | x64 | size |
|---|---|---|---|---|---|---|
| box–box | 3516ae00 | 2534 | | tri–tri | 35159410 | 2501 |
| box–ray | 3515f110 | 1392 | | tri–sph | 35159de0 | 3257 |
| box–sph | 3515d460 | 940 | | tri–cyl | 35164830 | 14789 |
| box–cyl | 3515ba70 | 6522 | | tri–cap | 35168280 | 10996 |
| box–cap | 3515d8a0 | 6128 | | ray–tri | 3515ab20 | 1705 |
| tri–box | 35162aa0 | 7374 | | sph–ray | 35160ea0 | 721 |
| sph–sph | 35160cb0 | 481 | | cyl–ray | 3515fe40 | 2858 |
| cyl–sph | 3515fa20 | 964 | | cyl–cyl | 3516b7f0 | 6890 |
| cyl–cap | 3516d2e0 | 4154 | | cap–ray | 35161ef0 | 2894 |
| cap–sph | 35161c80 | 538 | | cap–cap | 35161550 | 1819 |

**Wrapper fix-ups.** The fix-up depends on the pair:
- Swap pt[0] and pt[1], and swap iFeature[0][*] with iFeature[1][*] (bytes 36↔39, 37↔38).
- Additionally negate n, for wrappers whose callee returns n in the other orientation:
  - box–tri 35164780 (pts + feat + n);
  - tri–ray 3515b1d0, sph–box 3515d820, cyl–box 3515d3f0, cap–box 3515f0a0 (feat + n);
  - sph–tri 3515aaa0, cyl–tri 35168200, cap–tri 3516ad80 (pts + feat only);
  - ray–box/sph/cyl/cap, sph–cyl/cap, cap–cyl (feat only).
- Port these exactly; the normal convention differs per callee.

**Missing pairs.** There are no hf or vox rows or columns. Heightfields and voxels are never intersected as primitives here: their BV (CHeightfieldBV / CVoxelBV, slot 23) emits triangles (type 1) for the overlapped cells, so terrain is collided as triangle soup. ray–ray is 0.

**Worked examples.**
- **sph–sph (0x35160cb0):**
  1. Fail if `|c1−c2|² > (r1+r2)²`.
  2. `n = normalize(c1−c2)`, defaulting to (0,0,1).
  3. `pt = (c1 + c2 + n·(r1−r2))/2`, written to both pt[0] and pt[1].
  4. `out.n = c2 − c1` (not normalised); features = 0x40 for all four.
- **sph–ray (0x35160ea0):**
  1. Solve the quadratic for the segment parameter `t∈[0,1]`, taking the first root, else the second.
  2. `pt = o + t·d`; `n = normalize(pt − c)`.
  3. iFeature = {0x40,0x20,0x40,0x20} (sphere face, ray "edge").

### 4.2 prim_inters (output of INTERSECT, inferred; ≥88 bytes, filled by the pair function)

| off | field |
|---|---|
| +0 | Vec3 pt[0] (point on A / deepest point) |
| +12 | Vec3 pt[1] |
| +24 | Vec3 n (contact normal; orientation per pair, see wrappers) |
| +36 | u8 iFeature[2][2]. Layout `[A.feat0, A.feat1, B.feat0, B.feat1]`; byte codes: bit 7 = side flag, bits 5–6 = 0 vertex / 0x20 edge / 0x40 face, bits 0–4 = index. |
| +40 | float (input tolerance for some pairs, e.g. read by ray–tri) |
| +44 | short id[2] (material / foreign ids → geom_contact.id) |
| +48 | int iNode[2] → geom_contact +76/+80 |
| +56 | Vec3* ptborder (intersection border polyline, e.g. box–box) |
| +64 | int nborderpt |
| +68 | int nbordersz |
| +72 | Vec3 alternative normal (tri pairs) |
| +84 | int (set to 1000 by tri–sph) |

### 4.3 geom_contact (136 = 0x88 bytes; written by RegisterIntersection 0x3514f3f0 / CTriMesh 0x351d3950)

| off | field |
|---|---|
| +0 | double t (unprojection depth or time) |
| +8 | Vec3 pt |
| +20 | Vec3 n |
| +32 | Vec3 dir (unprojection direction or axis) |
| +44 | int iUnprojMode (0 linear, 1 rotational) |
| +48 | float vel (|relative velocity| or angular speed) |
| +52 | int id[2] (from prim_inters.id, sign-extended shorts) |
| +60 | int iPrim[2] (triangle index for meshes, −1 for primitives) |
| +68 | int iFeature[2] |
| +76 | int iNode[2] (from prim_inters +48/+52) |
| +88 | Vec3* ptborder (defaults to &pt, i.e. this+8) |
| +96 | int(*)[2] idxborder; each entry is `iPrim \| iFeature<<23` |
| +104 | int nborderpt (initialised to 1) |
| +112 | Vec3 center (of the border) |
| +124 | u8 bBorderConsecutive |
| +128 | geom_contact_area* (72 B; per-thread pool of 32 areas × 256 pts) |

**geom_contact_area (72 B, inferred)**
- +4 npt, +8 nmaxpt, +12 minedge
- +16 / +24 / +32 / +40 int* piPrim[0], piFeature[0], piPrim[1], piFeature[1]
- +48 Vec3* pt
- +12 is set to `min(gtA.minPtDist, gtB.minPtDist)`.

### 4.4 RegisterIntersection (CGeometry slot 54, 0x3514f3f0; x86 0x351578c0): from prim contact to unprojected contact

1. **Pick the direction.**
   - If `params.iUnprojectionMode==0`: `dir = (vB + wB×(pt−comB)) − (vA + wA×(pt−comA))`, using gtest +164/+176/+188.
     - If `|dir| < params.minAxisSpeed` (+32), fall back to `dir = n` (orthonormalised, sub_35021020) with mode 0 and no time limit.
   - If mode 1: `axis = normalize(wB − wA)`; centre = whichever of the two gtests' +200 vectors is longer; speed = |Δw|; tmax = |Δw|·dt.
2. **Unproject.** Call `UNPROJECT[mode][tA][tB](&up, primA, −1, primB, −1, &contact, area)`.
   - If it fails, the pair produces no contact.
   - If the returned `t > |dir|·params.time_interval` (+28), retry along n (the "penetration" fallback).
3. **Fill geom_contact.** Write t, pt, n, dir, mode, vel, ids, nodes, and the contact area.
4. **Bump the count.** Increment *pnContacts. Set the stop flag (gtest+40) when `nContacts ≥ nMax`.

### 4.5 OVERLAP table 0x3520A3E0 (cheap reject; default `ret1` = "maybe overlaps")

| A \ B | box | tri | hf | ray | sph | cyl | cap | vox |
|---|---|---|---|---|---|---|---|---|
| **box** | 351981b0 / 351a2370 | 351968f0 / 351a2be0 | 351986e0 / 351a39e0 | 35195ea0 / 351a1b60 | 35196ce0 / 351a1f00 | ret1 | ret1 | 35199140 / 351a44c0 |
| **tri** | 35196cc0 / 351a3370 | ret1 | ret1 | ret1 | 35197530 / 351a3940 | ret1 | ret1 | ret1 |
| **hf** | 35199120 / 351a44a0 | ret1 | ret1 | ret1 | 351975e0 / 351a2140 | ret1 | ret1 | ret1 |
| **ray** | 35196010 / 351a1ee0 | ret1 | ret1 | ret1 | ret1 | ret1 | ret1 | ret1 |
| **sph** | 35196e50 / 351a20b0 | 35197590 / 351a3990 | 35197860 / 351a2350 | ret1 | 35196030 / 351a20d0 | ret1 | ret1 | ret1 |
| **cyl** | ret1 | ret1 | ret1 | ret1 | ret1 | ret1 | ret1 | ret1 |
| **cap** | ret1 | ret1 | ret1 | ret1 | ret1 | ret1 | ret1 | ret1 |
| **vox** | 35199b10 / 351a4ff0 | ret1 | ret1 | ret1 | ret1 | ret1 | ret1 | ret1 |

**Real tests**
- box–box 351981b0 (SAT, oriented or not via bOriented)
- box–tri 351968f0
- box–hf 351986e0 (hf cell rectangle with height range)
- box–ray 35195ea0
- box–sph 35196ce0
- box–voxBV 35199140 (both operands read as boxes)
- tri–sph 35197530 (closest point on triangle via sub_35196E70, compared with r²)
- hf–sph 351975e0
- sph–sph 35196030 (`|c1−c2|² < (r1+r2)²`)

The cells with IDA names `?ApplyTo@HardwareAffinity…` are identical-code-folded swap wrappers.

In practice BV nodes are boxes (AABB/OBB/SingleBox), heightfield regions, voxel ranges, rays or spheres. Cylinder and capsule never appear as BV types, so their rows are all `ret1`.

### 4.6 UNPROJECT tables 0x35209FA0

**Mode 0** (linear unprojection / sweep along a direction):

| A \ B | box | tri | hf | ray | sph | cyl | cap | vox |
|---|---|---|---|---|---|---|---|---|
| **box** | 3517cd60 / 35180410 | 35171570 / 351792d0 | ret0 | 351794f0 / 3518dae0 | 35185b80 / 3518a9a0 | 351801a0 / 35183ad0 | 351870d0 / 3518c0d0 | ret0 |
| **tri** | 3516fdb0 / 35177810 | 3516eae0 / 35176460 | ret0 | 35176b40 / 3517f190 | 35175490 / 3517db70 | 351716d0 / 35179410 | 351767c0 / 3517ee80 | ret0 |
| **hf** | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 |
| **ray** | 35178540 / 3518caa0 | 35177b40 / 351802d0 | ret0 | ret0 | 3517bda0 / 35190a00 | 35179650 / 3518dc20 | 3517c9e0 / 351915d0 | ret0 |
| **sph** | 35186f70 / 3518bf90 | 35176660 / 3517ed40 | ret0 | 3517c880 / 35191490 | 35178110 / 3518c690 | 35189260 / 35193720 | 351916f0 / 3519d920 | ret0 |
| **cyl** | 35185aa0 / 3518a8f0 | 35175330 / 3517da30 | ret0 | 3517bc40 / 351908c0 | 35187780 / 351918e0 | 351893c0 / 35193860 | 35190570 / 3519c9d0 | ret0 |
| **cap** | 351876a0 / 3518c5e0 | 351769e0 / 3517f050 | ret0 | 3517cc00 / 351917a0 | 35191cb0 / 3519e020 | 35190b40 / 3519cee0 | 35190c20 / 3519cf90 | ret0 |
| **vox** | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 |

**Mode 1** (rotational unprojection about an axis):

| A \ B | box | tri | hf | ray | sph | cyl | cap | vox |
|---|---|---|---|---|---|---|---|---|
| **box** | ret0 | ret0 | ret0 | 351a0a40 / 351ace40 | ret0 | ret0 | ret0 | ret0 |
| **tri** | ret0 | 3519a730 / 351a59a0 | ret0 | 3519edc0 / 351aabd0 | ret0 | ret0 | ret0 | ret0 |
| **hf** | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 |
| **ray** | 3519ef90 / 351aaf30 | 3519d340 / 351a8cc0 | ret0 | ret0 | 351a7cc0 / 351bd280 | 351a0c10 / 351ad1a0 | 351a4630 / 351b5480 | ret0 |
| **sph** | ret0 | ret0 | ret0 | 351a9170 / 351bfb70 | ret0 | ret0 | ret0 | ret0 |
| **cyl** | ret0 | ret0 | ret0 | 351a4460 / 351b5120 | ret0 | ret0 | ret0 | ret0 |
| **cap** | ret0 | ret0 | ret0 | 351a7af0 / 351bcf20 | ret0 | ret0 | ret0 | ret0 |
| **vox** | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 | ret0 |

**Mode 0 details**
- Every pair among {box, tri, ray, sph, cyl, cap} is present except ray–ray.
- Entries of 215 / 335 / 447 bytes are swap wrappers. Examples: sph–box 35186f70 and cyl–box 35185aa0 are 335 B and 215 B respectively.

**Mode 1 details**
- Mode 1 only exists for "point-like vs surface" pairs, i.e. ray against everything, plus tri–tri.
- The rotational unprojection of solids is probably decomposed into feature-vs-feature pairs (a vertex sweeps an arc, which is treated as a ray-like primitive). See §5.

**Callers inside the geometry code**
- **Box slot 49** (UnprojectSphere 0x3513afc0) tests `OVERLAP[box][sph]` (0x35196ce0), then calls `UNPROJECT[0][box][sph]` (0x35185b80). It then shifts the contact point by `−n·t`.
- **CHeightfield::Intersect** with a ray calls `UNPROJECT[mode][tri][ray]` (index `64·mode + 11`).

---------------------------------------------------------------------------------------------------

## 5. Unprojection / continuous collision

**Confirmed:**
- **Mode 0 is linear.**
- **Mode 1 is rotational.** Rotational routines return (sin φ, cos φ) and callers take `atan2`.

### 5.1 Structures

**unproj_params (inferred, ≥ 52 B)**

| off | type | meaning |
|---|---|---|
| +0 | int | mode (the table plane) |
| +4 | Vec3 | linear: direction. Rotational: axis. **Zero in linear mode means "find the least-penetration direction"**, and the routine writes the chosen direction back here. |
| +16 | Vec3 | rotation centre |
| +28 | float | speed (|v| or |w|; copied to contact.vel) |
| +32 | float | tmax: linear maximum distance; rotational **sin(max angle)**, with the angle ≤ π/2 |
| +36 | float | tmin (reject t ≤ tmin; callers pass 0) |
| +40 | float | minPtDist = min(geomA+16, geomB+16) |
| +44 | float | maxcos: parallel-face threshold for area contacts (0.1, or `1 − θ²/2` from params+36) |
| +48 | int | flag (1 for sweeps; no reader found) |

**Call signature:** `int f(unproj_params*, primA*, iFeatA = −1, primB*, iFeatB = −1, unproj_result* out, contact_area* parea /*nullable*/)`.

**unproj_result (48 B)**

| off | type | linear | rotational |
|---|---|---|---|
| +0 | double | t | sin φ |
| +8 | double | (unused) | cos φ |
| +16 | Vec3 | pt | pt |
| +28 | Vec3 | n (points toward A, e.g. `−dir` for sph–sph) | n |
| +40 | int[2] | iFeature A/B | iFeature A/B |

Feature codes:
- `0x40|i` face.
- `0x80|i` vertex.
- `0xA0|i` triangle or ray edge (the ray segment is 0xA0).
- `0x20|i` box edge.

**intersection_params (inferred names; defaults are inlined in every constructor)**

| off | field | default |
|---|---|---|
| +0 | int iUnprojectionMode | 0 |
| +4 | Vec3 rotation centre | 0 |
| +16 | Vec3 rotation axis | 0 |
| +28 | float time_interval | 100 |
| +32 | float vrel_min: velocity-directed unprojection only if |vrel| > this; ≥ 1e9 disables it (callers use 1e10) | 1e-6 |
| +36 | float maxSurfaceGapAngle | 1° (0.0174533) |
| +40 | float minAxisDist | 0 |
| +56 | Vec3 preferred unprojection / "up" direction | (0,0,1) |
| +68 | float maxUnproj (tmax for the minimum-distance fallback) | 1e10 |
| +72, +84 | Vec3 ×2 outside pivots | 1e11 |
| +96 | u8 bSweepTest | |
| +97 | u8 keep previous contacts | |
| +98 | u8 (not decoded) | |
| +99 | u8 no area contacts | |
| +100 | u8 (not decoded) | |
| +108 | int skip contact generation, unproject only | |
| +128 | geom_contact* out | |
| +136 | lock ptr (→ +116) | |

### 5.2 Algorithms

**Linear (mode 0).** Find the smallest t ≥ tmin with t ≤ tmax such that moving A by `dir·t` separates A and B. With dir = 0, the routine picks the minimum-penetration direction and writes it back.

**Rotational (mode 1).** Find the smallest angle φ (kept as sin φ ≤ tmax) such that rotating A about (centre, axis) separates.

**sph–sph linear, 0x35178110** (x86 0x3518c690)
- dir = 0: `dir = normalize(cA − cB)`, `t = rA + rB − |d|`, `n = −dir`, `pt = cB − n·rB`.
- Otherwise: solve `|d + dir·t|² = (rA+rB)²` and take the larger root `(√disc − b)/a`. Reject if disc < 0, if there is no positive root, or if t ≤ tmin. Then `n = normalize(cB − (cA + dir·t))`.
- Features 0x40 / 0x40.

**box–box linear, 0x3517cd60** (x86 0x35180410, 13 KB)
- B is moved into A's frame (sub_35022380) and the work is done in double precision.
- SAT over 15 axes: 3 + 3 faces and 9 edge crosses, skipping crosses with |c|² < 1e-8.
- With dir = 0: take the axis of least penetration. With a fixed dir: compute per-axis t from the overlap and the axis component of dir, bounded by tmax.
- Contact point per feature pair: face–vertex, or edge–edge closest points.
- If parea is set and two faces are near-parallel (`> maxcos`), clip the face polygons to produce area points. Constants: 0.003·size, 0.01, 0.05.

**tri–tri rotational, 0x3519a730** (x86 0x351a59a0)
- Each vertex is decomposed relative to the centre into an axial part h and a radial part r. It moves along `c + h + r·cosφ + (axis×r)·sinφ`.
- Vertex against plane: `n·(c+h−p) + (n·r)·cosφ + (n·(axis×r))·sinφ = 0`, solved as a quadratic.
- The root is checked against the triangle edges inflated by minPtDist. Edge–edge pairs are handled the same way.
- The minimum angle is kept as a rational value num/den.

**ray–box rotational, 0x3519ef90**
- Ray endpoints follow circles that are tested against the box face planes (sqrt solve).
- Box edges are tested against the ray segment.

**Wrappers and compositions**
- **335 B** linear swap, e.g. 0x35171570: negate dir, call the mirrored pair, `pt −= dir·t`, negate n, swap features, restore dir.
- **215 B**: the same, plus flipping the area-contact normals and features (sub_3516E850).
- **447 B** rotational swap, e.g. 0x351a0a40: negate the axis, call the mirror, rotate pt and n by −φ (sub_3519A670 / sub_35020EA0), negate n.
- **Capsule entries** of 201 / 212 / 425 B are compositions: capsule = cylinder + 2 spheres. For example, cap–cap 0x35190c20 calls sph–sph, cyl–sph 0x35187780 and cyl–cyl 0x351893c0, and keeps the best result.

### 5.3 Sweep and unprojection inside Intersect

**Primitive Intersect 0x35151f20** (x86 0x3515a6d0)

*Sweep path* (`ip+96`):
1. `L = |gwdA.v|·time_interval`; `v̂ = v/|v|`.
2. Build the swept BV (BV slot 8). Prepare A's primitive at the end pose (offset + v̂·L).
3. If OVERLAP passes, call `UNPROJECT[0][tA][tB]` with `dir = −v̂`, `tmax = L`, `tmin = 0`, `maxcos = cos(gap)`, `flag = 1`. The call succeeds only if t < L.
4. If `n·dir > 0` (the end pose already tunnelled through), back up:
   - take the other geometry's bbox axis with the largest sweep component;
   - `t' = t + 0.1·halfsize/|component|`;
   - re-prepare at `offset + v̂·(L − t')`, re-unproject, then `t += t'`.
5. Output contact: **`t = L − t_unproj`** (distance travelled before impact), `dir = −v̂`, `vel = L`, mode 0.

*Non-sweep path*:
1. Run INTERSECT, unless `ip+108` is set.
2. If `ip.mode == 1`: rotational unprojection about ip centre/axis. If the axis is 0, use `n × (pt − centre)`, or `n × raydir` for rays. Then `t = atan2(sin, cos)`.
3. Else, if `vrel_min < 1e9` and there are no area points: compute vrel at the contact point. If `|vrel|² > vrel_min²`, run linear unprojection along `−v̂rel` with `tmax = |vrel|·time_interval`.
4. Fallback: minimum-distance unprojection (dir = 0, tmax = maxUnproj at +68).

**Generic / mesh Intersect 0x3514fd70** (x86 0x35158610)
- **Sweep with v = 0:** the sweep flag is cleared.
- **Sweep with v ≠ 0:**
  - The BV tree is swept and the contact is pre-seeded with `t = L`, `dir = −v̂`.
  - sub_3514E580 traverses the trees. At each leaf it shifts A by v̂·L and calls `UNPROJECT[0][tA][tB]` (dir = −v̂, tmax = L, maxcos 0.1).
  - A result is accepted if `n·v̂ > 0`, `t ≤ L` and `L − t < best`, so only the single earliest contact is kept.
- **Non-sweep:** primitive pairs go through RegisterIntersection (slot 54: CGeometry 0x3514f3f0, CTriMesh 0x351d3950). The mesh version walks adjacent triangles and uses `sin(min(|w|·dt, π/2))` for the rotational tmax.
- **Ray special path** (mesh 0x351c9910, heightfield 0x35156e20, voxel 0x351dda80):
  - Uses `UNPROJECT[mode][tri][ray]` (index `64·mode + 11`) to push the ray out of the mesh.
  - Hops across up to 3 neighbouring triangles. Step tolerance: geometry+16 for linear, 0.001 rad for rotational.
  - Mode-0 direction is the triangle normal × the ray, i.e. a sideways push.

### 5.4 Callers (entity/world side)

| x64 | x86 1.2 | what |
|---|---|---|
| `CRigidEntity__vf34` 0x350c9df0 (Step, inferred) | 0x350d84a0 | See the step breakdown below. |
| 0x350b3440 | `CRigidEntity::CheckForNewContacts` 0x350cb650 (binary string) | Expands the bbox by the sweep vector, collects parts, calls `geom->Intersect` (slot 10). Parts with flag 0x100000 and no sweep get `vrel_min = 1e10`. |
| `CRigidEntity__vf37` 0x350bcaa0 (max time step, inferred) | 0x350c19a0 | If `|v|·dt·size > 0.7·dim`, builds a swept-bbox box and calls the primitive Intersect with sweep = 1, then sweep = 0. On a hit, clamps `dt = max(0.005, 1.7·dim/|v|)`. Disabled by flag 0x20000 at entity+504. |
| `CPhysicalWorld__vf59` 0x35086db0 | `CollideEntityWithBeam` 0x3508e860 | time = 1; sweep = (|sweepDir| > 0) |
| `CPhysicalWorld__vf60` 0x350874b0 | `CollideEntityWithPrimitive` 0x3508f150 | the same, when no ip is passed |
| `CPhysicalWorld__vf65` 0x35093100 | 0x3509b8c0 | local ip with sweep = 1, +98..+100 = 1 |
| `CWheeledVehicleEntity__vf78` 0x3510c940 | `CheckAdditionalGeometry` 0x3511afb0 | toggles sweep |
| 0x350e3680 (rope, via 0x350e5020) | 0x350ef5e0 | **The only mode = 1 user**: centre = the rope vertex, axis derived as `n × raydir` |
| 0x350c0e50 | `CRigidEntity::ApplyBuoyancy` 0x350d1d80 | no sweep, +99 = 1 |
| 0x350827b0 | 0x350889b0 | world constructor: registers the "rb_intersect" profiler label (timing only) |

**`CRigidEntity__vf34` step** builds the intersection params as follows:
- mode 0; centre = position; time = dt.
- gap angle = clamp(Σ|w|·dt, 0.1, 0.19), then 0.0611, then 0 on later passes.
- +56 = −normalize(entity+616); maxUnproj = the largest bbox extent.

**CCD pass**, taken when `|v|·dt·(+588) > 0.71·(+592)`:
1. Restore the previous pose.
2. Set `ip+96 = 1` and call CheckForNewContacts with sweep = v·dt.
3. On a hit: `pos −= dir·(t + world[+240]/2)` and `v −= n·(v·n)·1.1`. Without a hit, restore the pose.
4. Check other entities through world slot 488, and halve v, w, P and L if blocked.

**Normal passes** follow:
- sweep off, time = 3·dt, vrel_min = 1e10 (minimum-distance only);
- a final pass with gap angle = 0.

### 5.5 Unprojection uncertainties

- **Undecoded fields:** unproj+48, and intersection_params +98, +100, +104 and +120.
- **Sign convention** of the post-sweep position correction.
- **Mode-1 centre choice** (the gtest +200 vectors) and the meaning of entity +588 / +592.
- **CGeometry slot 54, mode 1,** compares sin against an angle (small-angle approximation).
- **Ray rotational routines** were only checked by size and the fields they read.
- **x86 UNPROJECT** mode-1 plane base is 0x352046D8 (mode 0 at 0x352045D8).

---------------------------------------------------------------------------------------------------

## 6. Buoyancy and medium resistance (slots 12/13/14)

This is the most important section for ship physics. Roles are confirmed from the code:
- **Slot 13** (vtable +0x68) returns the submerged volume and writes its centre.
- **Slot 14** (+0x70) accumulates linear drag into `dP` and `dL`.
- **Slot 12** (+0x60) is an explosion / pressure impulse.

### 6.1 Functions

| role | x64 | x86 1.2 |
|---|---|---|
| Box slot 12 / 13 / 14 | 3513b0f0 / 3513b1b0 / 3513e7c0 | 35141d90 / 35141e30 / 35145950 |
| TriMesh (+ Heightfield) slot 12 / 13 / 14 | 351cbe20 / 351da620 / 351cd0c0 | 351e24d0 / 351ef500 / 351d7250 |
| Sphere slot 12 / 13 / 14 | 351aee70 / 351aec00 / 351afcb0 | 351c5780 / 351c5570 / 351c6160 |
| Cylinder slot 12 / 13 / 14 | 35142ee0 / 35143230 / 35146f40 | 3514b760 / 3514ba80 / 3514d360 |
| Capsule slot 13 (thunk → cylinder 13) / slot 14 | 3513f790 / 35140ea0 | 35146e30 / 35148b10 |
| PolygonDragIntegrate (inferred) | 35133e50 | 3513a410 |
| ClipPolygonByPlane (inferred; keeps n·p < d) | 35133970 | 35131d20 |
| RotateAxisCosSin, Rodrigues (inferred) | 35020ea0 | – |
| TetVolumeCentroid (inferred; \|det\|/6) | 351ccf20 | – |
| GetTriangleWorld (inferred; 3 verts + rotated normal) | 351c27a0 | 351d4f90 |
| QuatRotationArc(from, to) (inferred) | 35040f80 | – |
| QuatToMatrix33 (inferred) | 350247b0 | 35001f40 |
| BoxToTempTriMesh (inferred) | 3513ac00 | 35141620 |
| CircleClipByLine (inferred; cylinder caps) | 351440e0 | 3514ca50 |
| ApplyBuoyancy / water caller (inferred name) | 350c0e50 | 350d1d80 |
| ApplyAirResistance (inferred) | 350c2790 | – |

### 6.2 Caller: ApplyBuoyancy (x64 0x350c0e50 / x86 0x350d1d80; entity-side code, inferred name)

**Arguments:** `(entity, dt, gravity*, mediumArray*, count)`. Each medium element is 80 B (inferred):
- +4 density, +12 damping, +16 resistance
- +24 flow velocity (Vec3)
- +40 plane n, +52 plane origin
- +72 medium type: 0 = water, 1 = air; values ≥ 2 are skipped.

**Entity fields (inferred):**
- +1124 kDens, +1128 kRes. If +824 or kDens is 0, the function exits and sets +1136 = 0.
- +476.. world AABB.
- +280 parts array (stride 136), +288 count.
- +448 position, +460 quaternion.
- +1120 own damping, +684 default damping, +692 effective damping.
- +1136 submerged fraction.

**Per medium:**
1. If `n.z == 1`, replace origin.xy with the entity position (+408/+412) and subtract entity +1192 from origin.z.
2. AABB early-out: skip the medium if `n·(boxCentre − origin) > Σ|half_i·n_i|`.

**Per part** (only parts with flag 0x10000 at part +80):
1. **Skip** the part only if all three hold:
   - `V·s³·ρ·kDens ≤ 0.1·mass`;
   - an AABB area estimate × s² × resistance × kRes `≤ 0.0125·mass`;
   - damping ≤ 0.
2. **Build `wd`:**
   - `R = R(q_ent·q_part)`; `offset = pos + q_ent·partPos`; `scale = s`.
   - `wd.v = body.v(+64) − flow`; `wd.w = body.w(+52)`; `wd.com = body.com(+0)`. The body comes from entity vtable +512.
3. **Drag**, if resistance > 0:
   - `slot14 → dP, dL`. With `k = kRes·resistance`, apply impulse `dP·k·dt` and angular impulse `dL·k·dt`.
   - For water, `dP·k` is also summed for the splash logic.
   - If resistance == 0 but flow ≠ 0, use a crude push instead (no dt): `dP = (flow − v)·(hx·hy + hz·hy + hz·hx)·ρ·kDens`.
4. **Buoyancy:**
   - Fully submerged (`d ≤ −extent`): `Vs = V·s³`; `dP −= g·ρ·kDens·Vs·dt`. No torque.
   - Otherwise: `Vs = slot13(plane, wd, &c)`; `F = −g·ρ·kDens·Vs·dt`; `dP += F`; `dL += (c − com) × F`.
5. **Apply** via entity vtable +80 (Action, impulse type 2, point = NaN so it acts at the CoM).
6. **Accumulate** ΣVs and ΣV. ΣV uses the unscaled physGeom V.

**After all parts:**
- `f = min(ΣVs/ΣV, 1)`.
- `damp = max(entDamp, waterDamp)·f + (1−f)·defaultDamp`; `E+692 = max(E+692, damp)`.
- Water only: `+1136 = max(f, …)`. If `ρ·kDens·(+828) > (+820)`, set flag 0x4000000.
- If f < 0.9 and speed thresholds pass, queue a 160-B splash event.

**Air / wind:** 0x350c2790 queries areas with medium == 1 and calls slot 14 with `v − wind`; the result is scaled by `dt·entity+1140`.

### 6.3 Submerged volume and centre (slot 13)

**TriMesh 0x351da620** (also used by Heightfield, and by Box via a temporary 12-tri mesh):
1. Build `q = arc(n → (0,0,1))` (degenerate if `1 + n·z ≤ 1e−4`, then a perpendicular axis is used); `Rp = matrix(q)`.
2. Transform each vertex into the plane frame: `p = Rp·R·v·scale + Rp·(offset − origin)`. Here z is the height above the water.
3. Per triangle, weight `w = −sign(nz)` of the rotated normal (+1, 0 or −1, taken from the float's bit pattern).
4. On edges whose vertex signs differ, the crossing point is at `t = −zi/(zj − zi)`.
5. Let nA = number of vertices with z ≥ 0; top / low = indices of the highest / lowest vertex; index tables inc {1,2,0}, dec {2,0,1}.
   - **nA = 0** (all below): `zm = z[top]`; P' = vertices projected to height zm. Pieces: `Tet(v_top, P'_inc, v_inc, P'_dec) + Tet(v_top, P'_dec, v_dec, v_inc)`, plus a prism `|area_xy·zm|` with centroid `(cx, cy, zm/2)`. Each Tet is `|det|/6` with centroid = mean of its 4 points.
   - **nA = 1**: the same two tets, minus the prism, plus `Tet(v_top, (x_top, y_top, 0), i0, i1)`.
   - **nA = 2**: `Tet(v_low, (x_low, y_low, 0), i0, i1)`.
   - **nA = 3**: nothing.
6. Accumulate `V += w·Vk` and `M += w·Vk·ck`.
7. If V ≤ 0, return V with centre 0. Otherwise centre = `M/V + plane.origin`. **The centre is not rotated back by Rpᵀ** (same in x86), so it is exact only for n = (0,0,1).
- Constants: 1/6, 0.25, 1/3, 0.5.

**Sphere 0x351aec00:**
- `c = R·center + offset`. **Scale is not applied to the centre.** `r = scale·r`. `h = n·(origin − c)` is the water height above the centre.
- `h < −r`: return 0, centre = c.
- `h > r`: return `4/3·π·r³`, centre = c.
- Otherwise:
  - `V = π·((r² − h²/3)·h + 2/3·r³)`.
  - `off = π/2·((r² − h²/2)·h² − r⁴/2) − h`; centre = `c + n·off`.
  - **Quirk:** the binary does not divide by V. It literally subtracts h (verified at 0x351aedd5..0x351aede7; x86 the same).

**Cylinder 0x35143230** (capsule uses the same code via a thunk):
1. `r = s·r`, `L = 2·s·hh`. A capsule adds `4/3·r·s` to L (equal volume, modelled as a longer cylinder).
2. `a = R·axis`, flipped so that `a·n ≥ 0`. `c = R·center·s + offset`. Bottom `b = c − a·L/2`.
3. **Near-vertical** (`|n×a|² < 1e−4`): `d = clamp(n·(origin − b), 0, L)`; `V = π·r²·d`; centre = `b + a·d/2`.
4. **General case.** Let `e = normalize(n×a)`, `f = e×a`; x(t) is the waterline coordinate along f at axial height t, and `x0 = n·(origin − b)/(f·n)`.
   - If `x0 ≥ r`, return 0.
   - Segment area `A(x) = r²·(π/2 − asin(x/r)) − x·√(r² − x²)`.
   - **Piece 1**, fully submerged slab, if `x0 < −r`:
     - `t1 = n·(origin − (b − f·r))/(a·n)`. If `(a·n)² < 1e−4`, return πr²L.
     - `V1 = π·r²·t1`, moment `V1·(t1/2)·a`. Then set `x0 := −0.9999r`.
   - **Top:** `x1 = n·(origin − (b + a·L))/(f·n)`.
     - If `x1 < −r`, the cylinder is fully submerged: return πr²L.
     - If `x1 ≤ r`: **piece 2** is a prism `A(x1)·(L − t1)`, centroid at `(t1 + L)/2` along a, lateral moment `2/3·(r² − x1²)^{3/2}·(L − t1)` along f.
     - Else clamp `x1 = 0.9999r`, and t_end is where x = r.
   - **Piece 3**, the wedge, if `x1 > x0 + 0.01r`. With `k = (t_end − t1)/(x1 − x0)` and `q(x) = √(r² − x²)`:
     `V3 = k·[(q1³ − q0³)/3 + (x1 − x0)·(r²·asin(x1/r) + q1·x1) − r²·(asin(x1/r)·x1 − asin(x0/r)·x0 + q1 − q0)]`.
     The axial and lateral moments are closed-form polynomials (constants 0.375, 0.25, 2/3, 0.5); re-read 0x35143230 before porting.
   - Result: V = sum of pieces; centre = `b + ΣM/V`.
- Constants: 0.99989998, 1e−4, 0.01.

### 6.4 Medium resistance (slot 14): accumulates `dP −= …`, `dL −= …`; linear drag

**Core: PolygonDragIntegrate(pts, n, faceNormal, plane, v, w, com, dP, dL)** at 0x35133e50:
1. Clip the polygon to the underwater side: keep `plane.n·p < plane.n·origin`.
2. Make points relative to the CoM: `r = p − com`.
3. Clip again by the line where the face's normal speed changes sign (normal `w×n_f`, threshold `n_f·v`). This keeps the part with `n_f·(v + w×r) > 0`, i.e. faces moving into the fluid.
4. Rotate into the face frame: axis = normalize(n_f×z), cos = n_f.z, sin = |n_f×z| (fallback axis (1,0,0) when ≤ 0.001).
5. Integrate over the 2-D polygon with Green's theorem. The pressure is linear: `p(x,y) = v'z + w'x·y − w'y·x`.
6. `dP −= n_f·∫p dA`; `dL −= Rback(∫y·p dA, −∫x·p dA, 0)`, which is `∫ r × (p·n_f) dA`.
7. There is no density and no v² term; the caller multiplies by resistance·kRes·dt.

**Per shape:**
- **TriMesh 0x351cd0c0:** every world-space triangle with its rotated normal goes through the core.
- **Box 0x3513e7c0:**
  - `R' = R·Basis` if oriented; centre = `R·c·s + off`; half-sizes ×s.
  - Six quads: axis i = face/2, sign `2·(face&1) − 1`, each through the core.
- **Cylinder 0x35146f40:**
  - Side: 12 quads (30° steps; normals at 15° offsets, cos15 0.96592581, sin15 0.25881904) through the core.
  - Caps: analytic disk, clipped by the water line and the facing line (CircleClipByLine). Straight edges use polygon Green terms; arc edges use asin terms (clamp 0.9999).
- **Capsule 0x35140ea0:** 12 side quads plus a **full** sphere slot 14 at each end `c ± axis·hh` (temporary sphere object), so the end caps count twice as full disks.
- **Sphere 0x351afcb0:**
  - `c = R·center·s + off`; `u = v + w×(c − com)`; `h = n·(origin − c)`.
  - `|h| > r` gives zero, except `h > r` (fully submerged): `dP = −π·r²·u`, `dL = 0`.
  - Partial case:
    - `û = u/|u|`; `σ = |û × n|`.
    - `d1 = clamp(h/σ, ±0.999r)`, or `sign(h)·r` if σ < 0.01.
    - `A1 = ½·[(asin(d1/r) + π/2)·r² + d1·√(r² − d1²)]`.
    - Waterline disk `ρw = max(√(r² − h²), 0.001)`; `d2 = clamp((d1 − σh)/max(|u·n|, 0.001), ±0.999ρw)`.
    - `A2 = ½·[(sgn(u·n)·asin(d2/ρw) + π/2)·ρw² + q2·d2]·|u·n|`, with u unnormalised.
    - `dP = −u·(A1 + A2)`.
    - Centre of pressure: `t = ((σh − q2³/3·(u·n))·A2 − q1³/3·A1)/(A1 + A2)`; `p = c + t·b + √max(r² − t², 0)·û` with `b = a×û`; `dL = (p − com) × dP`.
  - The sphere **assigns** dP and dL rather than subtracting into them.

### 6.5 Slot 12: explosion / pressure impulse

Arguments: `(wd, epicenter, k, rmin, …, P&[, L&])`. The caller was not found.
- **Sphere:** `d = c_w − epi`; `P += d·k/(max(|d|², rmin²)·|d|)`. No area factor.
- **Cylinder:**
  - `sinθ = |axis_w × d̂|` (d̂ = (0,0,1) if `|d| ≤ 0.01·r`).
  - `area = (π·r²·(1 − sinθ) + 4·r·hh·sinθ)·s²`.
  - `P += d̂·area·k/max(|d|, rmin)²`.
- **TriMesh / Box:**
  - Takes a read lock and works in the local frame (k/s², rmin/s).
  - Triangles facing the epicenter are subdivided adaptively, 4-way, while vertex distance² ratios fall outside [0.49, 2.04].
  - Per leaf triangle: `mag = ½·|cross|·(n·(centroid − e))·k'/(max(dist², rmin'²)·dist)`, a solid-angle weighting.
  - `P += n·mag`; `L += (centroid − ref) × n·mag`.

### 6.6 Water-physics uncertainties and port decisions

1. **Sphere centre of buoyancy is not divided by V and ignores scale.** Decide whether to replicate this for client parity or use the physically correct value.
2. **TriMesh centre of buoyancy is not rotated back to world.** It is only exact for a horizontal water plane; ApplyBuoyancy has a special path for n = (0,0,1).
3. **Mesh volume can come back ≤ 0** for open or inverted meshes, and the caller does not clamp it.
4. **ΣV uses unscaled V.**
5. **Inferred fields:** the entity, body and medium-element field offsets are inferred from usage only.
6. **Transcribed in outline only:** the sphere slot 14 partial case and the cylinder wedge/cap polynomials. Re-read 0x351afcb0 and 0x35146f40 for exact arithmetic before porting.
7. **Not inspected:** box→trimesh conversion winding (sub_3513AC00).

## Corrections from the P1b port (bit-exact against the DLL)

- **AABB build split:** the AABB builder only considers the *larger* of the two allowed split axes,
  not both (the description above suggested both are tried).
- **OBB slot 13 bug:** `COBBTree` slot 13 writes the second child's orientation flag into the first
  child's record (mirrored in the port).
### 6.7 Resolved by the P1a port (bit-exact against the oracle; see `aaemu-physics/src/geometry/shapes/`)

- **Decision for 6.6 items 1–4:** all quirks are kept for client parity. The port returns the mesh volume even when it is ≤ 0 (centre zeroed) and the sphere's non-centroid "centre".
- **In/out semantics of slot 14:** base `CGeometry` (ray) **zeroes** dP/dL; box, cylinder and capsule zero them first and then accumulate (`-=`) their faces; the sphere **assigns** both. The caller (ApplyBuoyancy) does not reset dP between parts, so these differences matter.
- **Capsule slot 14** adds the sphere slot 14 of two temporary spheres (unscaled r) at `c ± axis·hh`, `+axis` first.
- **Cylinder slot 14 caps:** the water-line point is the projection of the **first** cap's centre on the water plane, reused for the second cap; parallel water/cap planes keep the whole disk when the centre is below the plane.
- **Side quads** (cylinder, capsule) step 30° in **double** (`(x·√3 ∓ y)·0.5`); everything else in slot 14 is single precision.
- **Box → temporary mesh** (sub_3513AC00): vertex `i = ((2(i&1)−1)·sx, ((i&2)−1)·sy, (((i>>1)&2)−1)·sz)` rotated by Basisᵀ, index table 0x35209A70 (faces −x, +x, −y, +y, −z, +z, two triangles each, outward winding), one normal per face.
- **asinf:** the cylinder buoyancy and slot-14 code call MSVCR100 `asinf`; `math::crt::asinf` is now a bit-exact port (100,000 oracle samples).
- **IsIdentity** (sub_3513A010) sums |m − I| and compares with 0 through `ucomiss`/`jne`: a NaN basis counts as the identity (`bOriented = 0`).
- **Mesh slot 12** (0x351CBE20): epicenter and centre of mass go to the mesh frame without the 1/scale, k and rmin are scaled; the refinement test compares squared distances with the raw `rmin`; the projection-split test has the wrong sign (dead for consistently wound meshes); at most 300 subdivision pops per triangle, the rest is dropped; P and L are rotated back without scale.
