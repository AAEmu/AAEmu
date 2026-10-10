# CryPhysics entities and the params/status/action interface (draft)

Source: x64 10.x `cryphysics_dedicate.dll` (base 0x35000000, RTTI) is the primary source. The x86 1.2 client (base 0x34FF0000) is used for cross-reference only. Everything here comes from the binaries and their decompiles. Quoted names are strings found in the binary. Any name chosen from behaviour carries "(inferred)" or "(inf)". All offsets are x64 byte offsets, written in decimal unless prefixed 0x. Sections 3-5 are per-class notes and keep their own local numbering.

**Entity types** (from `CPhysicalWorld::CreatePhysicalEntity` at 0x35082F00, and from vtable slot 1 GetType):

| type | class | size | ctor x64 | vtbl x64 / x86 | slots |
|---|---|---|---|---|---|
| 1 | CPhysicalEntity (static) | 0x1C0 | 0x35071D30 | 0x351EFE38 / 0x351FB7C0 | 77 |
| 2 | CRigidEntity | 0x4B0 | 0x350ACBC0 | 0x351F14C8 / 0x351FCD08 | 83 |
| 3 | CWheeledVehicleEntity | 0x2BF8 | 0x35104900 | 0x351F2388 / 0x351FD9A0 | 84 |
| 4 | CLivingEntity | 0x468 | 0x35025530 | 0x351EE1D8 / 0x351FAA60 | 77 (x86: 78) |
| 5 | CParticleEntity | 0x2A8 | 0x35039D40 | 0x351EEE88 / 0x351FB220 | 77 (x86: 78) |
| 6 | CArticulatedEntity | 0x618 | 0x35001380 | 0x351ED638 / 0x351FA2D8 | 83 |
| 7 | CRopeEntity | 0x400 | 0x350CE210 | 0x351F1A48 / 0x351FD2C0 | 77 |
| 8 | CSoftEntity | 0x360 | 0x350FC660 | 0x351F1DE8 / 0x351FD5B8 | 77 |
| 9 | CPhysArea (GetType returns 9) | — | — | 0x351EF598 / 0x351FB658 | 38 |

The placeholder (`CPhysicalPlaceholder`, 35 slots) forwards slots 7-27 and 34 to the real entity. It does this through slot 32, GetEntity, which creates the entity on demand. It handles three requests itself: GetStatus id 16 (returns the entity ptr at +0x38) and id 7 (returns 0); Action id 8 with bAwake=0, and Action id 9, both when no entity exists yet; and Action id 4, which forces a timeout.

## 0. Conventions used by the param/status/action interface

* Every `pe_params_*`, `pe_status_*`, `pe_action_*` and `pe_geomparams`-like struct starts with `int type` at +0; the handler dispatches with `if/switch (*(int*)p)`. Offsets below are **x64** (pointers are 8 bytes, 8-aligned); x86 1.2 layouts shift wherever a pointer precedes a field.
* **"Unused" sentinels** (optional fields): written by the struct ctors / internal callers and tested by every handler:
  * float / vector fields: unused bit pattern `0xFFBFFFFF` (= -4194305, a quiet NaN; Hex-Rays prints `NAN`). Test: `(bits & 0xFFA00000) == 0xFFA00000` -> unused. For a vec3 only the first component is tested; for a quaternion (x,y,z,w) the **w** component (last float) is tested.
  * int fields: `0x80000000` (INT_MIN).
  * pointer fields: `(void*)-1`; some also treat `nullptr` as "not set".
* Quaternions are stored `(x, y, z, w)` (w last; quat->matrix helper `sub_350247B0`).
* Float clamp constant used for "infinite": `9.9999999e14` (0x58635FA9 = 1482907561).
* **Deferred calls**: SetParams/Action/AddGeometry/RemoveGeometry first call a "lock-or-queue" helper (`sub_35023200` SetParams, `sub_35023650` Action, `sub_35022A50` AddGeometry, `sub_35022F00` RemoveGeometry). If the world is stepping on another thread (world+0x57C4 / +0x57DC / +0x57E0 locks, entity+0x40 "processed" counter >= 0x20000), the struct is **memcpy'd into the world command queue** (world+0x51A8.. ring of pages) and the call returns 1 (or the part id). The copy size and the embedded pointer arrays come from static tables built in `sub_351E5900` (x64):

| table (x64) | indexed by | content |
|---|---|---|
| `0x3537C640` int[27] | params type id | sizeof(params) |
| `0x3537C500` ptr[] | params type id | list of pointer-member descriptors `{int offs; int fixedCount; int countOffs; int elemSize; next*}` (count = fixedCount + *(int*)(p+countOffs) if countOffs!=0) |
| `0x3537C5F0` int[19] | action type id | sizeof(action) |
| `0x3537C460` ptr[] | action type id | pointer-member descriptors |
| `0x3537C5D8` int[3] | geomparams type id | sizeof(geomparams) |
| `0x3537B438..48` | geomparams type id | pointer-member descriptors (all 3 share `0x3537B3D8`) |

Params sizes (x64): 0:72 1:64 2:144 3:168 4:104 5:208 6:160 7:24 8:176 9:24 10:84 11:32 12:80 13:248 14:28 15:16 16:80 17:100 18:72 19:36 20:32 21:120 22:52 23:12 24:48 25:32 26:8.
Action sizes (x64): 0:- 1:24 2:80 3:36 4:8 5:128 6:48 7:88 8:12 9:4 10:40 11:48 12:48 13:12 14:- 15:12 16:72 17:104 18:88.
Geomparams sizes (x64): 0:112 (plain), 1:184 (car/wheel), 2:120 (articulated).

Pointer members declared in the tables (x64):

| struct | ptr @ | count | elem |
|---|---|---|---|
| params 0 (pos) | +40 | 1 | 48 (Matrix34) |
| params 0 (pos) | +48 | 1 | 36 (Matrix33) |
| params 2 | +88 | int@+84 | 4 |
| params 5 | +40 | 1 | 36 (Matrix33) |
| params 5 | +184 | int@+176 | 4 |
| params 6 (part) | +48 / +56 / +136 | 1 / 1 / int@+144 | 48 / 36 / 4 (Matrix34*, Matrix33*, int* matMapping) |
| params 7 | +8 and +16 | int@+4 | 12 (two vec3 arrays) |
| params 13 | +160 and +176 | 1+int@+124 | 12 (two vec3 arrays of nSegs+1 -> rope points/velocities) |
| action 11 | +24 (int*), +32 (vec3*) | int@+40 | 4 / 12 |
| action 12 | +8 (vec3*) | int@+4 | 12 |
| geomparams 0/1/2 | +48 / +56 / +96 | 1 / 1 / int@+104 | 48 / 36 / 4 |

## 1. Master id tables (which class handles which type id)

Every derived SetParams, GetParams and GetStatus calls the parent class **first**. The derived class handles an id itself only when the parent returns 0. Some classes post-process ids 0, 6 and 15 after the base has handled them. Derived Action handles its own ids first and then falls back to the parent. "B" marks the base (CPhysicalEntity) handler, which all classes inherit unless they override it. Sizes come from the x64 tables (§0).

### 1.1 params (SetParams / GetParams)

| id | size | name | handled by | layout |
|---|---|---|---|---|
| 0 | 72 | **pe_params_pos** (string) | B; rigid, living and soft post-process | §2.3 |
| 1 | 64 | **player_dimensions** (string "CLivingEntity:SetParams(player_dimensions)") | living | living §2.1 |
| 2 | 144 | car params (inferred) | wheeled | wheeled §5.1 |
| 3 | 168 | **pe_params_particle** (string) | particle | particle §2.1 |
| 4 | 104 | player dynamics (inferred) | living | living §2.2 |
| 5 | 208 | **params_joint** (string "CArticulatedEntity:SetParams(params_joint)") | artic | rigid/artic §2.4 |
| 6 | 160 | **pe_params_part** (string) | B; rigid, living and wheeled post-process | §2.3 |
| 7 | 24 | two vec3 arrays with count (inferred, sensor-like) | no handler found in the entity classes | +4 n, +8 vec3*, +16 vec3* |
| 8 | 176 | articulated body / host attach (inferred) | artic | rigid/artic §2.4 |
| 9 | 24 | outer entity (inferred) | B | §2.3 |
| 10 | 84 | simulation params (inferred) | rigid (wheeled copies 2 fields), rope, soft | rigid/artic §2.1, rope §2.2 |
| 11 | 32 | foreign data (inferred) | B | §2.3 |
| 12 | 80 | buoyancy (inferred) | rigid | rigid/artic §2.1 |
| 13 | 248 | rope params (inferred; its pointer members match points/velocities) | rope | rope §2.2 |
| 14 | 28 | **pe_params_bbox** (string) | B | §2.3 |
| 15 | 16 | flags (inferred) | B (+post-processing) | §2.3 |
| 16 | 80 | wheel params (inferred) | wheeled | wheeled §5.1 |
| 17 | 100 | soft-body params (inferred) | soft (base id 24 also builds one internally) | soft §2.3 |
| 18, 19, 22 | 72, 36, 52 | — | no handler found in the entity classes (probably area or world) | — |
| 20 | 32 | ground plane (inferred) | B | §2.3 |
| 21 | 120 | structural joint (inferred; field-name strings) | B | §2.3 |
| 23 | 12 | timeout (inferred) | B | §2.3 |
| 24 | 48 | skeleton (inferred) | B | §2.3 |
| 25 | 32 | per-part structural vectors (inferred) | B | §2.3 |
| 26 | 8 | byte flag (inferred) | B | §2.3 |
| 117 | — | ignore-part-id list (x64 10.x only, not present in 1.2) | soft | soft §2.3 |

### 1.2 status (GetStatus): there is no size table, so ids are taken from the handlers

| id | name (inferred unless noted) | handled by |
|---|---|---|
| 1 | pos | B; living wraps it (net smoothing); rigid interpolates when timeBack>0; wheeled handles wheel parts |
| 2 | living state | living |
| 4 | vehicle | wheeled |
| 5 | wheel | wheeled |
| 6 | joint | artic |
| 7 | awake | B (→ slot 56); rigid (lag); artic |
| 8 | dynamics | rigid, artic, living, particle |
| 9 | collisions / "collided" flag | rigid (bool), particle (bool, then clear), living (toggle), soft (buffer of 80-byte entries) |
| 10 | id (material id) | B |
| 11 | — (returns 0) | living |
| 12 | nparts | B |
| 13 | contains_point | B (→ slot 70) |
| 14 | rope | rope |
| 15 | vehicle abilities | wheeled |
| 16 | placeholder → entity ptr | placeholder |
| 17 | soft vertices | soft |
| 19 | contact patch fit | rigid |
| 20 | (sets +4=0; artic sets 1) | B, artic |
| 21 | check stance / unprojection | living |
| 24 | extent | B |
| 25 | random pos | B |
| 26 | constraint | rigid |
| 27 | (copy under lock, returns 0) | B |

### 1.3 actions (Action)

| id | size | name | handled by |
|---|---|---|---|
| 1 | 24 | **action_move** (string) | living |
| 2 | 80 | **action_impulse** (strings exist for rigid, living, artic and soft; field strings "impulse", "angImpulse", "point", "ipart") | B (structure/lattice), rigid, artic, living, particle, rope, soft |
| 3 | 36 | drive (inferred) | wheeled |
| 4 | 8 | reset (inferred) | rigid, artic, living, particle, rope, soft, wheeled; the placeholder uses it to force a timeout |
| 5 | 128 | add_constraint (inferred) | rigid (wheeled and artic pass it to rigid) |
| 6 | 48 | update_constraint (inferred) | rigid |
| 7 | 88 | register collision event (inferred) | rigid |
| 8 | 12 | awake | B, rigid (+min awake time) |
| 9 | 4 | remove_all_parts (inferred) | B |
| 10 | 40 | set_velocity (inferred) | rigid, artic, living, particle |
| 11 | 48 | attach points (inferred) | soft |
| 12 | 48 | target vertices (inferred) | rope, soft |
| 13 | 12 | reset part matrix (inferred) | B |
| 14 | — (not in size table) | rope re-lay / untie (inferred) | rope |
| 15 | 12 | auto part detachment (inferred) | B |
| 16 | 72 | move_parts (inferred) | B |
| 17 | 104 | set part poses (inferred) | artic |
| 18 | 88 | net sync ("CRigidEntity::Action() sync") | rigid |

geomparams (AddGeometry): type 0 = 112 B (plain, §2.7); type 1 = 184 B (car/wheel, see wheeled §5.2); type 2 = 120 B (articulated, see rigid/artic).

## 2b. Entity vtable roles (x64; 6-hex = 0x35xxxxxx; ret0/ret1/nop/ret-1/ret0.f/ret0b = shared stubs)

Roles marked (inferred) come from behaviour; plain names are x86 profiler strings or are obvious from the code.

|slot|role|PH|Static|Rigid|Wheeled|Living|Particle|Artic|Rope|Soft|
|---|---|---|---|---|---|---|---|---|---|---|
|0|scalar deleting dtor|0960a0|076090|110810|110810|039020|040840|024b20|0eaa10|0fded0|
|1|GetType (1 static,2 rigid,3 wheeled,4 living,5 particle,6 artic,7 rope,8 soft; area=9)|076410|ret1|0fe380|024cd0|037c40|038a00|020a60|0ea790|0fde20|
|2|AddRef (+0x58, per-thread step counter +0x80/+0x82)|ret0|052ae0|052ae0|052ae0|052ae0|052ae0|052ae0|052ae0|052ae0|
|3|Release|ret0|052b60|052b60|052b60|052b60|052b60|052b60|052b60|052b60|
|4|set +0x60 (interlocked xchg) (inferred lock/"processed" flag)|nop|052bf0|052bf0|052bf0|052bf0|052bf0|052bf0|052bf0|052bf0|
|5|add to +0x60|nop|052c00|052c00|052c00|052c00|052c00|052c00|052c00|052c00|
|6|GetId (low 23 bits of +0x44, sign-extended)|020990|020990|020990|020990|020990|020990|020990|020990|020990|
|7|SetParams(params*, bThreadSafe)|076920|06ef20|0c4cc0|106740|028bf0|03b800|01c850|0d8a80|0fca50|
|8|GetParams(params*)|076b90|054e60|0aabd0|104b50|025040|0398d0|0068a0|0cea00|0eb3d0|
|9|GetStatus(status*)|076450|0724c0|0b1b80|10a090|029a30|03a310|001b50|0cee50|0eb570|
|10|Action(action*, bThreadSafe)|0764b0|0583c0|0c5d30|1094a0|02a300|03a750|00f7c0|0dbb50|0eff10|
|11|AddGeometry(phys_geometry*, geomparams*, id, bThreadSafe)|076560|055ff0|0c41c0|10bbb0|024d30|055ff0|00cdf0|055ff0|0ed610|
|12|RemoveGeometry(id, bThreadSafe)|0765c0|056db0|0b0ca0|1072f0|024d80|056db0|0063d0|056db0|0efa50|
|13|GetForeignData(itype) (+0x20 if +0x28==itype)|020970|020970|020970|020970|020970|020970|020970|020970|020970|
|14|GetiForeignData (+0x28)|020980|020980|020980|020980|020980|020980|020980|020980|020980|
|15|GetStateSnapshot(CStream&, time_back, flags) (inferred)|076650|06c5e0|0c0050|105570|025270|03a000|005ea0|0d6bf0|0ecff0|
|16|GetStateSnapshot(TSerialize, flags) (inferred)|076600|ret0|0ae930|105260|02c780|039ad0|017510|0ce1a0|0ebb70|
|17|SetStateFromSnapshot(CStream&, flags) (inferred)|0766e0|06cd10|0c8820|105630|025bb0|03a140|0178e0|0d71f0|0f2970|
|18|SetStateFromSnapshot(TSerialize, flags) (inferred)|0766a0|ret0|0c0730|105290|025ba0|039b50|00b330|0d6a70|0ecfe0|
|19|PostSerialize fix-up: resolve saved entity ids -> ptrs (rigid) (inferred)|076770|ret1|0af110|0af110|ret1|ret1|0af110|ret1|ret1|
|20|GetStateChecksum: xor of round(x*1024) over pos,q,v,w (inferred)|076830|ret0|0ac750|0ac750|ret0|ret0|0ac750|ret0|ret0|
|21|typed-snapshot helper; builds static prototype entity of every type (inferred)|076720|0825a0|0825a0|0825a0|0825a0|0825a0|0825a0|0825a0|0825a0|
|22|GetStateSnapshotTxt (calls 16 + text serializer) (inferred)|0767a0|0537f0|0537f0|0537f0|0537f0|0537f0|0537f0|0537f0|0537f0|
|23|SetStateFromSnapshotTxt (text -> 18) (inferred)|0767f0|053830|053830|053830|053830|053830|053830|053830|053830|
|24|net/queued input: rigid queues an impulse (type 2) for next step; living = x86 "SyncFromNet"|ret0|ret0|0ae9b0|0ae9b0|02aa60|ret0|0ae9b0|ret0|ret0|
|25|particle-only big func (see particle section); others return 1|ret1|ret1|ret1|ret1|ret1|03c210|ret1|ret1|ret1|
|26|StartStep(time_interval) (inferred)|0768a0|nop|0ccb20|0ccb20|024dc0|039850|0ccb20|0ce050|0eb2e0|
|27|StepBack(dt): restore prev-step state (inferred)|0768e0|nop|0be4a0|0be4a0|nop|nop|00a740|nop|nop|
|28|GetWorld (+0x78)|076180|0209a0|0209a0|0209a0|0209a0|0209a0|0209a0|0209a0|0209a0|
|29|GetMemoryStatistics(ICrySizer*)|nop|053470|0aa4d0|1048a0|024e00|039870|0012f0|0cdd80|0eb310|
|30|GetNumParts? returns +0x120 (nParts) (inferred)|ret0|0209d0|0209d0|0209d0|0209d0|0209d0|0209d0|0209d0|0209d0|
|31|GetPartId(ipart) (part+0x60 low 10 bits?) (inferred)|ret0|0209e0|0209e0|0209e0|0209e0|0209e0|0209e0|0209e0|0209e0|
|32|GetEntity() (placeholder: create real entity)|0762a0|1a9340|1a9340|1a9340|1a9340|1a9340|1a9340|1a9340|1a9340|
|33|GetEntityFast() (placeholder: +0x38)|04fcc0|153c20|153c20|153c20|153c20|153c20|153c20|153c20|153c20|
|34|Step(time_interval) -> 1 when finished|076860|ret1|0c9df0|0c9df0|02ded0|0407e0|01a150|0ea720|0f8b10|
|35|GetExtent(form) - per-part extent cache (inferred)||072d40|072d40|072d40|072d40|072d40|072d40|0d8790|072d40|
|36|GetRandomPos(PosNorm*, ctx, form) (inferred)||0722c0|0722c0|0722c0|0722c0|0722c0|0722c0|0dcd90|0722c0|
|37|GetMaxTimeStep(time_interval)||037c10|0bcaa0|104790|025390|039ce0|001130|0ce120|0ebaf0|
|38|GetLastTimeStep(dt) (rigid +0x25C) (inferred)||037c10|020a40|020a40|037c10|037c10|020a40|0ea7f0|037c10|
|39|GetContactCount(nMax) (inferred)||ret0|0ac3e0|0ac3e0|ret0|ret0|0ac3e0|ret0|ret0|
|40|RegisterContacts(dt,nMaxPlaneContacts) (x86 profiler name)||ret0|0bdba0|105e50|027190|ret0|00bb00|0d2d30|ret0|
|41|Update(dt,damping): post-solver, damping, sleep logic -> "step finished" (inferred)||ret1|0beb40|1050a0|02c240|ret1|0182c0|0d4d60|ret1|
|42|CalcEnergy(dt) (inferred; base 0)||ret0.f|0ab8f0|0ab8f0|026fb0|ret0.f|002840|0ea7d0|ret0.f|
|43|GetDamping(dt) (base 1.0)||037c20|0ac490|104fa0|037c20|037c20|0018b0|0ea9d0|037c20|
|44|GetMaxFriction (base 100.0; rigid +0x2C0) (inferred)||037c30|020a50|020a50|037c30|037c30|020a50|037c30|037c30|
|45|HasConstraintWith(pent,bCheckColliders) (inferred)||ret0b|0ab5f0|0ab5f0|ret0b|ret0b|0ab5f0|ret0b|ret0b|
|46|Get v/w snapshot (ipart,&v,&w) (inferred)||037cb0|0ccdc0|0ccdc0|037cb0|037cb0|020a70|037cb0|037cb0|
|47|AddCollider(pent)||053890|0acf10|0acf10|053890|053890|0acf10|053890|053890|
|48|RemoveCollider(pent) (x86 name)||052980|0aadb0|104840|052980|052980|0aadb0|052980|052980|
|49|RemoveContactPoint(pent,pt,minDist) (inferred)||ret-1|0ab790|0ab790|ret-1|ret-1|0ab790|ret-1|ret-1|
|50|HasContactsWith(pent) (inferred)||ret0|0a9c20|1047f0|ret0|ret0|0a9c20|ret0|ret0|
|51|HasPartContactsWith(pent,ipart,bGreaterOrEqual) (inferred)||ret1|0aaf80|104ff0|ret1|ret1|0aaf80|ret1|ret1|
|52|HasCollisionContactsWith(pent) (inferred)||ret0|0a9c80|0a9c80|ret0|ret0|0a9c80|ret0|ret0|
|53|HasConstraintContactsWith(pent,flagsIgnore) (inferred)||ret0|0a9ce0|0a9ce0|ret0|ret0|0a9ce0|ret0|ret0|
|54|detach/removal hook: drop from colliders + wake them (inferred)||0530b0|0a9dd0|104700|037cd0|0406d0|001000|0ce670|0eb190|
|55|Awake(bAwake, iSource)||ret0|0ab170|0ab170|024f90|039b60|0ab170|0cde00|0eb280|
|56|IsAwake(ipart)||ret0|0ccd30|0ccd30|ret0|039bb0|0010f0|0fde30|0fde30|
|57|(particle/rope/soft) contact/matrix query (inferred)||1a9330|1a9330|1a9330|1a9330|03ab10|1a9330|0d5dd0|0ecb20|
|58|(rope/soft) attach-related (pent,float,float) (inferred)||nop|nop|nop|nop|nop|nop|0d6500|0ec240|
|59|OnContactResolved(contact,iop) (breakable constraint check; living: clip vel) (inferred)||05bf60|0add40|104760|0279b0|05bf60|001270|05bf60|05bf60|
|60|DelayedIntersect (x86 name, rigid)||nop|0bb8c0|0bb8c0|nop|nop|0bb8c0|nop|nop|
|61|restore raw state from buffer / OnDelayedStep (x86 soft name)||nop|nop|nop|nop|nop|002520|0d2b90|0ec810|
|62|OnEntitySplit(src,new) (rigid) (inferred)||nop|0c2f30|0c2f30|nop|nop|0c2f30|0d6a80|nop|
|63|unused (all empty)||nop|nop|nop|nop|nop|nop|nop|nop|
|64|GetRigidBody(ipart) (rigid +0x2E8) (inferred)||053030|0ccd40|0ccd40|027700|053030|0010b0|053030|053030|
|65|GetRigidBodyData(out,ipart) (inferred)||0406b0|0406b0|0406b0|037d40|0406b0|002490|0dd170|0406b0|
|66|GetMass(ipart) (part+0x4C)||0209b0|0209b0|0209b0|0209b0|0406c0|0209b0|0ea7a0|0fde40|
|67|GetVelocityAt(pt,ipart) (inferred)||nop|0ccd50|0ccd50|037db0|nop|0019c0|nop|nop|
|68|contact K-matrix add invMass (living only) (inferred)||nop|nop|nop|037c60|nop|nop|nop|nop|
|69|GetMassInv (rigid +0x338) (inferred)||ret0.f|020a10|020a10|037c50|ret0.f|020a10|0ea7c0|ret0.f|
|70|IsPointInside(pt) (pe_status_contains_point)||0576c0|0576c0|0576c0|0576c0|0576c0|0576c0|0576c0|0576c0|
|71|GetPartTransform(ipart,&pos,&q,&scale)||024970|024970|024970|024970|024970|024970|0dd480|024970|
|72|RemoveContacts(ipart,side,pent,...) (inferred)||nop|0aaae0|0aaae0|nop|nop|0aaae0|nop|nop|
|73|DrawHelperInformation(IPhysRenderer*,flags)||05a490|0aca60|106330|02bec0|03af40|0aca60|0d7f90|0ed140|
|74|RecomputeMassDistribution(ipart,bMassChanged) (inferred)||nop|0b1130|0b1130|nop|nop|00e690|nop|nop|
|75|ComputeBBox(BBox*, flags, ipart) (x86 name)||054080|054080|054080|026bc0|040f10|054080|054080|054080|
|76|execute queued (deferred) Action/params entry||052c10|0aad50|0aad50|052c10|052c10|0aad50|052c10|052c10|
|77|rigid/artic: returns 9 (inferred const)|||020a30|ret1|||020a30|||
|78|step hook 1 (rigid no-op; wheeled CheckAdditionalGeometry - x86 name)|||nop|10c940|||nop|||
|79|step hook 2 (rigid no-op; wheeled drivetrain/suspension) (inferred)|||nop|1074a0|||nop|||
|80|UpdateBreakableFlags (inferred)|||0aa050|0aa050|||001220|||
|81|GetPotentialColliders (x86 name)|||0b2b90|0b2b90|||001050|||
|82|IsSelfCollisionEnabled(ipart,ipart2) (artic) (inferred)|||ret0|ret0|||001070|||
|83|wheeled: ComputeBBox wrapper inflating by 2*world+0xF0 (inferred)||||104f10||||||

x86 1.2 vtable slot addresses (6-hex = 0x35xxxxxx; ? = similarity < 3):

|slot|PH|Static|Rigid|Wheeled|Living|Particle|Artic|Rope|Soft|
|---|---|---|---|---|---|---|---|---|---|
|0|084650|05f850|0b9fc0|116ca0|02c3f0|03fba0|004c70|0db7d0|10b720|
|1|080680|0597b0|0b6e20|116c80|02a200|03f5e0|0028f0|0db590|10b500|
|2|048c80|059820|059820|059820|059820|059820|059820|059820|059820|
|3|048c90|0598a0|0598a0|0598a0|0598a0|0598a0|0598a0|0598a0|0598a0|
|4|048ca0|059930|059930|059930|059930|059930|059930|059930|059930|
|5|048cb0|059950|059950|059950|059950|059950|059950|059950|059950|
|6|0027a0|0027a0|0027a0|0027a0|0027a0|0027a0|0027a0|0027a0|0027a0|
|7|080a70|07b720|0d4920|116cd0|02f5d0|041f80|024c70|0e5590|10b950|
|8|0806c0|05c460|0b7550|113120|02a4a0|03fbd0|00e930|0dc4c0|0f86c0|
|9|080740|07f330|0bdd20|1184d0|030500|0408d0|004ca0|0dc940|0f8880|
|10|080790|067250|0c6950|117820|030e90|041af0|00f140|0e87f0|0fd7a0|
|11|080830|061170|0c61a0|119fc0|0291f0|061170|00ad50|061170|0fb060|
|12|080850|061de0|0bcd70|11ae00|029240|061de0|00cdd0|061de0|0fd5c0|
|13|002770|002770|002770|002770|002770|002770|002770|002770|002770|
|14|002790|002790|002790|002790|002790|002790|002790|002790|002790|
|15|0808a0|07e720|0cfd40|113b50|02aa00|040630|0082d0|0e3960|0fa7d0|
|16|080870|059800|0bb5b0?|1137f0?|02a680?|03fdc0|0202c0|0dbb70|0f8d90|
|17|0808f0|07ee00|0d0350|116500|02c420|040720|0207d0|0e3e10|0fa8c0|
|18|0808d0|059810|0c48e0|1138f0|02ab50|03fe80|0207c0|0e3840|0fa7c0|
|19|080950|02a1d0|0b8cf0|0b8cf0|02a1d0|02a1d0|0b8cf0|02a1d0|02a1d0|
|20|0809c0|02a1e0|0b9100|0b9100|02a1e0|02a1e0|0b9100|02a1e0|02a1e0|
|21|080920|08cb50|08cb50|08cb50|08cb50|08cb50|08cb50|08cb50|08cb50|
|22|080970|05a460|05a460|05a460|05a460|05a460|05a460|05a460|05a460|
|23|0809a0|05a4a0|05a4a0|05a4a0|05a4a0|05a4a0|05a4a0|05a4a0|05a4a0|
|24|03f550|03f550|0bb6c0|0bb6c0|031560|03f550|0bb6c0|03f550|03f550|
|25|048cc0|0027d0|0027d0|0027d0|0027d0||0027d0|0027d0|0027d0|
|26|080a10|0597c0|0b64f0?|0b64f0?|029270|03f630|0b64f0?|0dba30|0f8600|
|27|080a40|03f560|0c2b70|0c2b70|02a210|03f560|01f400|03f560|03f560|
|28|0804f0|0027b0|0027b0|0027b0|0027b0|0027b0|0027b0|0027b0|0027b0|
|29|048cd0|05a100|0b66e0|1130e0|0292f0|03f650|0012a0|0db610|0f8620|
|30|048ce0|002830|002830|002830|002830|002830|002830|002830|002830|
|31|048cf0|002840|002840|002840|002840|002840|002840|002840|002840|
|32|080590|1bfee0|1bfee0|1bfee0|1bfee0|1bfee0|1bfee0|1bfee0|1bfee0|
|33|048c70|0027c0|0027c0|0027c0|0027c0|0027c0|0027c0|0027c0|0027c0|
|34|0809e0|0597e0||||03f830||0f8420|1072b0|
|35||080370?|080370?|080370?|080370?|080370?|080370?|0e50d0?|080370?|
|36||07fff0|07fff0|07fff0|07fff0|07fff0|07fff0|0e51c0|07fff0|
|37||0597d0|0c19a0|112fc0|02ab60|040030|001140?|0dbaf0|0f8d10|
|38||02a0e0|0028b0|0028b0|02a0e0|02a0e0|0028b0|0db600|02a0e0|
|39||02a0f0|0b89b0|0b89b0|02a0f0|02a0f0|0b89b0|02a0f0|02a0f0|
|40||03f570|0cf490|11ffc0|02d1a0|03f570|0085a0|0e13b0|03f570|
|41||03f580|0c3460||03f030|03f580|020eb0|0f7080|03f580|
|42||03f590|0b83c0|0b83c0|02d040|03f590|005870|0db5d0|03f590|
|43||02a100|0b8a40|113550|02a100|02a100|003040|0db7a0|02a100|
|44||02a110|0028c0|0028c0|02a110|02a110|0028c0|02a110|02a110|
|45||02a120|0b7e40|0b7e40|02a120|02a120|0b7e40|02a120|02a120|
|46||02a2e0|0b6e50|0b6e50|02a2e0|02a2e0|002900|02a2e0|02a2e0|
|47||05d350|0ba290|0ba290|05d350|05d350|0ba290|05d350|05d350|
|48||059370|0b9ff0|113080|059370|059370|0b9ff0|059370|059370|
|49||02a130|0b8020|0b8020|02a130|02a130|0b8020|02a130|02a130|
|50||02a140|0b60e0|113030|02a140|02a140|0b60e0|02a140|02a140|
|51||02a150|0b7750|1135c0|02a150|02a150|0b7750|02a150|02a150|
|52||02a160|0b6140|0b6140|02a160|02a160|0b6140|02a160|02a160|
|53||02a170|0b6190|0b6190|02a170|02a170|0b6190|02a170|02a170|
|54||059df0|0b6270|112f30|02a310|03f600|001020|0dc180|0f84f0|
|55||0597f0|0b79b0|0b79b0|02a3f0|03fe90|0b79b0|0db800|0f85b0|
|56||02a180|0b6e30|0b6e30|02a180|03fef0|001100|0db5a0|10b510|
|57||1bfed0|1bfed0|1bfed0|1bfed0|040c50|1bfed0|0e3150|0fa380|
|58||0027e0|0027e0|0027e0|0027e0|0027e0|0027e0|0f8060|0f9930|
|59||05dbf0|0ce750|112f90|02da50|05dbf0|0030c0|05dbf0|05dbf0|
|60||02a190|0d7590|0d7590|02a190|02a190|0d7590|02a190|02a190|
|61||02a1a0|02a1a0|02a1a0|02a1a0|02a1a0|005610|0e1210|0f9e70|
|62||02a1b0|0d3ad0|0d3ad0|02a1b0|02a1b0|0d3ad0|0e3850|02a1b0|
|63||0027f0|0027f0|0027f0|0027f0|0027f0|0027f0|0027f0|0027f0|
|64||059d80|0b6e40|0b6e40|02d6f0|059d80|0010c0|059d80|059d80|
|65||03f5a0|03f5a0|03f5a0|02a220|03f5a0|005590|0e9d50|03f5a0|
|66||002800|002800|002800|002800|03f5f0|002800|0db5b0|10b520|
|67||03f5c0|0b6ea0|0b6ea0|02a3b0|03f5c0|003140|03f5c0|03f5c0|
|68||002820|002820|002820|02a290|002820|002820|002820|002820|
|69||03f5d0|002870|002870|02a280|03f5d0|002870|0db5c0|03f5d0|
|70||05cfd0|05cfd0|05cfd0|05cfd0|05cfd0|05cfd0|05cfd0|05cfd0|
|71||00aa80?|00aa80?|00aa80?|00aa80?|00aa80?|00aa80?|0ea220?|00aa80?|
|72||02a1c0|0b74c0|0b74c0|02a1c0|02a1c0|0b74c0|02a1c0|02a1c0|
|73||0624f0|0b9390|1165f0|02dc90|0410b0|0b9390|0e4960|0fab80|
|74||02a1f0|0bd290|0bd290|02a1f0|02a1f0|00d400|02a1f0|02a1f0|
|75||05f880|05f880|05f880|05f880?|05f880?|05f880|05f880|05f880|
|76||059970|0b76c0|0b76c0|059970|059970|0b76c0|059970|059970|
|77|||0028a0|116c90|||0028a0|||
|78|||0028d0|11afb0|||0028d0|||
|79|||0028e0|1142f0|||0028e0|||
|80|||0b8160|0b8160|||001260|||
|81|||0cad80|0cad80|||001060|||
|82|||0b6e90|0b6e90|||001080|||
|83||||1134c0||||||

## 2. CPhysicalEntity (base, type 1 "static") — x64 vt 0x351EFE38, x86 vt 0x351FB7C0

Key functions (x64 -> x86 1.2):

| role | x64 | x86 |
|---|---|---|
| ctor (`sub_35071D30`, size 0x1C0) | 0x35071D30 | 0x35059590 (callgraph, sim 3.9) |
| dtor body | 0x35071F80 | 0x3505C210 (callgraph, sim 3.7) |
| SetParams (vf7) | 0x3506EF20 | 0x3507B720 `CPhysicalEntity__SetParams` |
| GetParams (vf8) | 0x35054E60 | 0x3505C460 |
| GetStatus (vf9) | 0x350724C0 | 0x3507F330 |
| Action (vf10) | 0x350583C0 | 0x35067250 |
| AddGeometry (vf11) | 0x35055FF0 | 0x35061170 `CPhysicalEntity__AddGeometry` |
| RemoveGeometry (vf12) | 0x35056DB0 | 0x35061DE0 |
| ComputeBBox (vf75) | 0x35054080 | 0x3505F880 `CPhysicalEntity__ComputeBBox` |
| queued-action executor (vf76) | 0x35052C10 | 0x35059970 |
| Step (vf34) | 0x35076030 (`return 1`) | 0x350597E0 |
| SetParams lock-or-queue helper | 0x35023200 | — |
| Action lock-or-queue helper | 0x35023650 | — |
| AddGeometry lock-or-queue helper | 0x35022A50 | — |
| RemoveGeometry lock-or-queue helper | 0x35022F00 | — |
| validator for params_pos / bbox / part / geomparams (logs "Validation Error: %s is invalid") | 0x350741C0 / 0x35074520 / 0x350747C0 / 0x35074BC0 | — |
| matrix -> (pos,q,scale) decomposition (used when pMtx3x4/pMtx3x3 given) | 0x35135AB0 | — |
| world: re-grid entity (RepositionEntity-like, (world, ent, mode, bbox*, ...)) (inferred) | 0x35080A00 | — |
| world: GetEntitiesInBox-like (world, min, max, out**, flags, ...) | 0x35084680 | — |
| update OBB/grid cache after move (`+0x140` buffer, 0x140 bytes) (inferred) | 0x35053DC0 | — |

### 2.1 CPhysicalEntity member layout (x64; size 0x1C0)

| off | type | meaning (source) |
|---|---|---|
| 0x00 | vtbl* | |
| 0x08 | Vec3 | BBox[0] (world AABB min) (bbox params/status) |
| 0x14 | Vec3 | BBox[1] (max) |
| 0x20 | void* | pForeignData (params 11) |
| 0x28 | int16 | iForeignData |
| 0x2A | int16 | iForeignFlags |
| 0x2C..0x33 | int16[4] | entity-grid cell coords (inferred); -2 = not gridded, -3 (0xFFFD) = hidden / out-of-grid (flags 0x400 path) |
| 0x34 | int | |
| 0x38 | ptr | buddy: placeholder<->entity link (bbox mirrored into it) |
| 0x40 | int | "Processed" counter (X2Log), +0x20000 marks "has queued commands" |
| 0x44 | uint32 | bits 0..22 entity id (sign-extended 23-bit); byte 0x47 = **iSimClass** (signed char; ctor 0; tests `<=6`, `>=7` = deleted/hidden, `0xFF`) |
| 0x48 | int | RW spin-lock (readers +1, writer 0x10000; byte 0x4A = writer flag) |
| 0x50 | int | "Deletion time" (X2Log) |
| 0x58 | int | refcount (AddRef/Release) |
| 0x5C | uint32 | **flags** (default 0x8640). Bits seen: 0x2 auto-part-detachment active; 0x10 -> large-object grid/OBB mode, converted to 0x400 (hidden from grid) when cell count small; 0x80 (sign of low byte) blocks lattice-break wake; 0x8000 skip "entities in box" wake; 0x100000 alert; 0x200000 impulse scaling by world var (+0x1AC); 0x80000000 set by AlertNeighbourhood |
| 0x60 | int | vf4/vf5 counter |
| 0x68,0x70 | ptr | list links (inferred) |
| 0x78 | CPhysicalWorld* | pWorld |
| 0x80/0x82 | int16 | per-thread step stamp / nested refcount |
| 0x84 | bitfield | (|=0xFFFF, bits 24..27 = 1 in ctor) |
| 0x88 | int | -1 |
| 0xA8 | Vec3 + Quat | 0xA8 pos, 0xB4 quat(x,y,z,w=1) — secondary coords (prev/next frame) (inferred) |
| 0xC4 | float | **scale** (1.0) |
| 0xC8 | coord* | pNewCoords -> {Vec3 pos; Quat q} (default = this+0x198) |
| 0xD0 | CPhysicalEntity** | colliders array |
| 0xD8 | int | nColliders; 0xDC nCollidersAlloc; 0xE0 colliders lock |
| 0xE8 | IPhysicalEntity* | pOuterEntity (params 9) |
| 0xF0 | IGeometry* | pBoundingGeometry (params 9) |
| 0xF8 | int | |
| 0xFC | float | timeIdle (params 23) |
| 0x100 | float | maxTimeIdle (params 23); placeholder action 4 forces `timeIdle = maxTimeIdle + 1` |
| 0x104 | int | |
| 0x108 | int | 1 (cleared by lattice break) |
| 0x110 | ptr | |
| 0x118 | geom_part* | **parts** (stride 0x88); empty = static dummy `0x3520C8A0` |
| 0x120 | int | nParts |
| 0x124 | int | nPartsAlloc (grows by 4) |
| 0x128 | int | next free part id |
| 0x12C | int | lock for 0x128 |
| 0x130 | GroundPlane* | array, stride 24 {Vec3 origin; Vec3 n} (params 20) |
| 0x138 | int | nGroundPlanes |
| 0x140 | ptr | OBB/grid cache (0x140 bytes) |
| 0x148 | int | |
| 0x14C | byte | params 26 value |
| 0x150 | int | lock; 0x158.. data copied out by status 27 |
| 0x178 | | sub-object init by `sub_35075F90` (extent cache?) |
| 0x198 | Vec3 | **pos** |
| 0x1A4 | Quat | **q** (x,y,z,w) |
| 0x1B8 | Structure* | pStructure (breakable joints, see params 21) |

### 2.2 geom_part (stride 0x88 = 136, array at entity+0x118)

| off | type | meaning |
|---|---|---|
| 0x00 | Vec3 | pos (entity frame, already multiplied by entity scale) |
| 0x0C | Quat | q (x,y,z,w) |
| 0x1C | float | scale |
| 0x20 | Vec3 | local BBox[0] |
| 0x2C | Vec3 | local BBox[1] |
| 0x38 | phys_geometry* | pPhysGeom |
| 0x40 | phys_geometry* | pPhysGeomProxy (== pPhysGeom unless proxy set) |
| 0x48 | int | id |
| 0x4C | float | mass |
| 0x50 | uint32 | flags (bit 0x400000 = has lattice/structure; 0x80000 = breakable geom; 0x20000 stripped on add = "proxy only"; 0x10000000 = removed by queued remove_all_parts; 0x4 cleared on parent link) |
| 0x54 | uint32 | flagsCollider |
| 0x58 | float | max dimension of geom bbox (auto computed) |
| 0x5C | float | minContactDist (auto = mid(0.03*maxDim, k*minDim) k=0.1 or 0.4) |
| 0x60 | bitfield | bits 0..9 surface_idx (from phys_geometry+0x38 if unset); bits 10..23 idmatBreakable (14 bit, 0x3FFF=-1) |
| 0x64 | uint32 | bits 0..30 nMats, bit 31 flag |
| 0x68 | int* | pMatMapping (shared with phys_geometry+0x40 or own copy) |
| 0x70 | ptr | pLattice (tetra lattice) |
| 0x78 | coords* | pNewCoords -> {pos,q,scale,BBox[2]} (normally points to the part itself) |
| 0x80 | ptr | cleared on add |

phys_geometry (refcounted through world geometry manager vtbl +0x50 AddRef/+0x58 Release): +0x00 IGeometry*, +0x30 float V (volume; density*V*scale^3 = mass), +0x38 surface_idx, +0x40 int* pMatMapping, +0x48 nMats.

Structure (entity+0x1B8): +0x00 joints array (stride 0x80), +0x10 nJoints, +0x14 nJointsAlloc, +0x08 per-part info (stride 84: accumulated lin/ang impulse +0/+12, part pos/vel +48/+60, parent idx +72, saved flags +76/+80), +0x1C counter, +0x20 int, +0x24 nPartsAlloc, +0x3C autoDetachmentDist, +0x50/+0x58 per-part Vec3 temp arrays, +0x60 per-part skeleton slots (stride 72: +0 ptr, +8 IPhysicalEntity* skeleton, +16 idSkeleton, +20/+24/+28 params), +0x6C flags OR.
Structural joint (stride 0x80): +0 id, +4/+8 ipart[2], +12 pt, +24 n, +36 axisx, +48 maxForcePush, +52 maxForcePull, +56 maxForceShift, +60 maxTorqueBend, +64 maxTorqueTwist, +68 limitConstr Vec3, +80 dampingConstr, +84 bits (1 breakable, 2 !constraintIgnoresCollisions..., 4<< directBreaksOnly), +88 bBroken, +92 szHelper (default 5*world+0xF0), +96/+100 zeroed. (Names `maxForcePush`, `limitConstr`, `dampingConstr`, `szHelper`, `bBreakable`, `axisx`, `ipart0/1`, `hasJoints`, `nJoints` are binary serialization strings.)

### 2.3 Base SetParams ids (x64 0x3506EF20)

| id | name (inferred) | layout (x64) | effect |
|---|---|---|---|
| 0 | **pe_params_pos** (string "CPhysicalEntity:SetParams(pe_params_pos)") | +4 Vec3 pos (f), +16 Quat q (f, tested on w @+28), +32 float scale (f), +40 Matrix34* pMtx3x4 (ptr), +48 Matrix33* pMtx3x3 (ptr), +56 int iSimClass (i), +60 int bRecalcBounds, +64 byte bEntGridUseOBB(inferred) | matrices decomposed into pos/q/scale first; validator; new simclass only if current <=6 (world re-sort `sub_35080A00(w,e,2)`); scale change rescales all part pos/scale/mass(?) by ratio; bbox recomputed (vf75) and entity re-gridded if bRecalcBounds |
| 6 | **pe_params_part** ("CPhysicalEntity:SetParams(pe_params_part)") | +4 partid (i), +8 ipart (i), +12 bRecalcBBox (bit1: wake entities in box), +16 pos (f), +28 q (f, test +40), +44 scale (f), +48 pMtx3x4*, +56 pMtx3x3*, +64 flagsCond (i; if set only parts with flags&cond), +68 flagsOR, +72 flagsAND, +76 flagsColliderOR, +80 flagsColliderAND, +84 mass (f), +88 density (f; mass = scale^3*V*density), +92 minContactDist (f), +96 pPhysGeom (ptr), +104 pPhysGeomProxy (ptr), +112 idmatBreakable (i), +120 pLattice (ptr), +128 idSkeleton (i), +136 int* pMatMapping (ptr), +144 nMats, +152 bAddrefGeoms (GetParams), +156 idParent (i) | if partid & ipart both unused -> applies flags/mass/matmapping to **all** parts; returns ipart+1 |
| 9 | pe_params_outer_entity | +8 IPhysicalEntity* pOuterEntity (AddRef'd), +16 IGeometry* pBoundingGeometry | stored at +0xE8/+0xF0 |
| 11 | pe_params_foreign_data | +8 pForeignData (ptr, -1 unused), +16 iForeignData (i), +20 iForeignFlags (i), +24 iForeignFlagsAND, +28 iForeignFlagsOR | also mirrored to placeholder |
| 14 | **pe_params_bbox** ("CPhysicalEntity::SetParams(pe_params_bbox)") | +4 BBox[0] (f), +16 BBox[1] (f) | regrid + store |
| 15 | pe_params_flags | +4 flags (i), +8 flagsOR (i), +12 flagsAND (i) | order: set, AND, OR; handles 0x10/0x400 grid-hiding |
| 20 | ground-plane params (inferred) | +4 iPlane (<0 = clear all), +8 Vec3 origin (f), +20 Vec3 n (f) | array at +0x130 |
| 21 | pe_params_structural_joint (inferred; field strings exist) | +4 id, +8 idx (-1 = only update +116; -2 = rebuild/`sub_3506D3C0`), +12 bReplaceExisting, +16 partid[2], +24 pt, +36 n, +48 axisx, +60 maxForcePush, +64 maxForcePull, +68 maxForceShift, +72 maxTorqueBend, +76 maxTorqueTwist (each clamped to 1e15), +80 limitConstr Vec3, +92 bBreakable, +96 bConstraintWillIgnoreCollisions, +100 bDirectBreaksOnly, +104 dampingConstr, +108 szSensor, +112 bBroken, +116 nextJointId/flag | joint array in pStructure |
| 23 | pe_params_timeout | +4 timeIdle (f), +8 maxTimeIdle (f) | +0xFC/+0x100 |
| 24 | skeleton params (inferred) | +4 partid, +8 ipart, +12 stiffness, +16 thickness, +20 maxStretch, +24 maxImpulse, +28 timeStep, +32 nSteps (i), +36 hardness, +40 explosionScale (f; stored *200 and *0.005), +44 bReset | builds params type 17 (soft-body params) and calls skeleton entity's SetParams; bReset -> Action type 4 on skeleton |
| 25 | part velocity/offset (inferred) | +4 partid, +8 Vec3, +20 Vec3 | writes structure per-part info +48/+60 |
| 26 | byte flag (inferred) | +4 byte | +0x14C |

### 2.4 Base GetParams ids (x64 0x35054E60)
14 bbox, 9 outer_entity, 11 foreign_data, 6 part (fills pos/q/scale, optional out Matrix34 @+48 / Matrix33 @+56, flagsOR=flagsAND=flags, mass, density=mass/(scale^3*V_proxy), minContactDist, geoms, idmatBreakable, pLattice, pMatMapping/nMats, idSkeleton; AddRefs geoms if +152), 15 flags, 20 ground plane, 21 structural joint, 23 timeout, 24 skeleton (reads back type-17 params from the skeleton entity).

### 2.5 Base GetStatus ids (x64 0x350724C0)

| id | name (inferred) | layout |
|---|---|---|
| 1 | pe_status_pos | +4 partid, +8 ipart (-1 = whole entity), +12 flags (1 = local/part frame, 2 = skip lock, 4 = AddRef geoms), out: +16 flagsOR, +20 flagsAND, +24 pos, +36 BBox[0] (relative to pos), +48 BBox[1], +60 q, +76 scale, +80 iSimClass, +88 Matrix34* (out, optional), +96 Matrix33* (out), +104 IGeometry* pGeom, +112 IGeometry* pGeomProxy |
| 7 | pe_status_awake | returns vf56 IsAwake(-1) |
| 10 | pe_status_id | +4 ipart, +8 partid, +12 iPrim, +16 iFeature, +20 bUseProxy; out +24 id (material id from geometry vf16, else part surface idx) |
| 12 | pe_status_nparts | returns nParts |
| 13 | pe_status_contains_point | +4 Vec3 pt -> vf70 |
| 20 | (inferred, collisions-like) | sets +4 = 0, returns 1 (overridden by dynamic classes) |
| 24 | pe_status_extent (inferred) | +4 form, +8 ctx* -> `sub_35075F10` (calls vf35) |
| 25 | pe_status_random (inferred) | +4 form, +8 ctx*, +16 out PosNorm -> vf36 |
| 27 | (inferred) | copies block at +0x158 under lock +0x150 into +8; returns 0 |

### 2.6 Base Action ids (x64 0x350583C0)

| id | name | layout (x64) | effect |
|---|---|---|---|
| 2 | **pe_action_impulse** (strings "...:Action(action_impulse)" in derived classes) | +4 impulse Vec3 (f), +16 angImpulse Vec3 (f), +28..+51 (used by rigid), +52 point Vec3 (f), +64 partid (i), +68 ipart (i), +72 iApplyTime (inferred), +76 iSource (0/1/2) | static: accumulates into structure per-part impulse (breakable joints, threshold 0.01*mass), feeds skeleton (action type on skeleton), and lattice (`sub_351B1420`) -> wake/re-structure |
| 4 | (placeholder only) | — | `timeIdle = maxTimeIdle+1` then forwards (forces timeout) |
| 8 | pe_action_awake | +4 bAwake | vf55 Awake(bAwake,1) when simclass<=6 |
| 9 | pe_action_remove_all_parts | — | RemoveGeometry for every part |
| 13 | reset part matrix (inferred) | +4 ipart / +8 partid | moves entity to part world pose (params 0), then part pos=0,q=identity (params 6) |
| 15 | auto part detachment (inferred) | +4 threshold (f), +8 autoDetachmentDist (f) | creates structural joint (params 21) per part with maxForce = |g|*mass*threshold; flags|=2 |
| 16 | pe_action_move_parts (inferred) | +4 idStart, +8 idEnd, +12 idOffset, +16 IPhysicalEntity* pTarget, +24 Matrix34 mtxRel | re-adds matching parts to target (AddGeometry with copied part params) and removes them here; also remaps structural joints |

### 2.7 pe_geomparams (type 0, 112 B), as read by base AddGeometry (0x35055FF0)

| off | type | meaning |
|---|---|---|
| 0 | int | type (0) |
| 4 | float | density (used if mass ≤ 0: mass = density·V·(scale·entScale)^3) |
| 8 | float | mass |
| 12 | Vec3 | pos (multiplied by entity scale) |
| 24 | Quat | q (x,y,z,w) |
| 40 | float | scale (multiplied by entity scale) |
| 48 | Matrix34* | pMtx3x4 (decomposed into pos/q/scale) |
| 56 | Matrix33* | pMtx3x3 |
| 64 | int | surface_idx (0x80000000 = take it from phys_geometry) |
| 68 | uint | flags (bit 0x20000 = only replace the proxy geometry of an existing part id; the bit is stripped before storing) |
| 72 | uint | flagsCollider |
| 76 | float | minContactDist (unused → auto) |
| 80 | int | idmatBreakable (unused: if flags&0xFFB then auto from the surface flags table via 0x35052cc0 — max((flags>>16)−1) over the mapped/primitive materials, reset to -1 for non-meshes, part flag 0x800000 when some flags have 0x400 — and part 0 is redone when the second part is added; else -1) |
| 88 | ptr | pLattice (only for geometry type 1) |
| 96 | int* | pMatMapping |
| 104 | int | nMats |
| 108 | int | bRecalcBBox |

AddGeometry returns the new part id. id < 0 means auto-assign (counter at +0x128). An existing id with flag 0x20000 swaps the proxy geometry. Parts grow in steps of 4, and a single part uses the world's small-block allocator. For geometry type 1 (mesh) with a volume above 0.05, a mesh-prepare helper runs.

### 2.8 x86 1.2 differences noticed
* x86 `pe_params_pos`: +4 pos, +16 q, +32 scale, +36 pMtx3x4, +40 pMtx3x3, +44 iSimClass, +48 bRecalcBounds. This is the x64 layout minus 4 or 8 bytes after the first pointer.
* x86 CPhysicalEntity: pos +120, q +132, scale +176, simclass byte +59, world +92, nParts +240.
* The same sentinel tests (`0xFFA00000` mask, `0x80000000`) appear in x86 (451 occurrences, against 466 in x64).
* The x86 Living and Particle vtables have 78 slots, against 77 in x64. Positional slot matching for slots ≥ 75 of those two classes is therefore unreliable.


---

## 3. CRigidEntity / CArticulatedEntity

Everything here comes from the decompiles. Names marked **(inferred)** are based on observed behaviour. Quoted names are serialization or validation strings that appear in the binary. Addresses are x64 unless they are in an x86 column.

### 0. Conventions and sentinels

| kind | "unused" test | value written by ctors / defaults |
|---|---|---|
| float field | `(bits & 0xFFA00000) == 0xFFA00000` | `0xFFBFFFFF` (= -4194305; Hex-Rays prints NAN) |
| int field | `== 0x80000000` (INT_MIN) | `0x80000000` |
| pointer field | `== -1` (`0xFFFFFFFFFFFFFFFF`) | `-1` |
| quaternion | only `.w` (4th float, +12) is tested | |

- Quaternions are stored as `(x,y,z,w)`, w last.
- Entity base fields used here:
  - pos +0x198 (408), q +0x1A4 (420)
  - world bbox +8..+31
  - world ptr +0x78 (120)
  - colliders ptr +0xD0 (208), nColliders +0xD8 (216), collider capacity +0xDC (220), collider rw-lock +0xE0 (224)
  - parts +0x118 (280), nParts +0x120 (288), parts capacity +0x124 (292)
  - simclass byte +0x47 (71), flags +0x5C (92), base idle timers +252/+256
- Part (136 B) fields used:
  - +0 pos, +12 q, +28 scale
  - +56 phys-geom (geom+48 = volume, +36 = centre), +64 proxy geom
  - +72 part id, +76 mass, +80 flags (0x400000 = carries a breakable constraint), +84 collider flags
  - +120 pointer to the new-pose slot (56 B)
- Every SetParams/Action starts with the world deferred-queue check `sub_35023200` / `sub_35023650`. If the call is queued, the function returns early. The queued copy is later replayed through slot 76.
- World vars used:
  - world+364: max linear velocity
  - world+240: `fixedStep`-like dt used to scale debug drawing and CCD
  - world+22516: current step id
  - world+21576: free-contact pool (spin-locked by +22540)
  - world+21928 + 80·thread: per-thread "bSubstep" flag (inferred)

### 1. VTables: overridden slots

Base = CPhysicalEntity (Static column, 77 slots). Slots 77..82 are new in CRigidEntity. The x86 address is the same vtable slot in 1.2 (`compare/vtable_slots.txt`). "=R" means CArticulatedEntity inherits the CRigidEntity function.

| slot | Rigid x64 | x86 1.2 | Artic x64 | x86 1.2 | role (inferred unless string-named) |
|---|---|---|---|---|---|
| 0 | 0x35110810 | 0x350b9fc0 | 0x35024b20 | 0x35004c70 | scalar deleting dtor (rigid dtor body sub_350AA5C0) |
| 1 | 0x350fe380 | 0x350b6e20 | 0x35020a60 | 0x350028f0 | **GetType**: returns 2 (rigid) / 6 (articulated). Confirmed because slot 29 tests `vt[1]()==2/6`. |
| 7 | 0x350c4cc0 | 0x350d4920 | 0x3501c850 | 0x35024c70 "CArticulatedEntity__SetParams" | SetParams (§2) |
| 8 | 0x350aabd0 | 0x350b7550 | 0x350068a0 | 0x3500e930 | GetParams |
| 9 | 0x350b1b80 | 0x350bdd20 | 0x35001b50 | 0x35004ca0 | GetStatus |
| 10 | 0x350c5d30 | 0x350c6950 CRigidEntity__Action | 0x3500f7c0 | 0x3500f140 CArticulatedEntity__Action | Action |
| 11 | 0x350c41c0 | 0x350c61a0 CRigidEntity__AddGeometry | 0x3500cdf0 | 0x3500ad50 CArticulatedEntity__AddGeometry | AddGeometry: base add, then mass/body recompute; artic also attaches the part to a joint |
| 12 | 0x350b0ca0 | 0x350bcd70 | 0x350063d0 | 0x3500cdd0 | RemoveGeometry(id): drops contacts (sub_350AA9A0), calls base, recomputes mass, fixes the constraint part indices |
| 15 | 0x350c0050 | 0x350cfd40 | 0x35005ea0 | 0x350082d0 | GetStateSnapshot(TSerialize): rigid writes pos/rot/vel/angvel/simclass and "constraints"; artic writes awake/pos/vel/"numjoints"/"joint"{q,dq,qext,q0changed,quat0}/"offsPivot" |
| 16 | 0x350ae930 | 0x350bb5b0 | 0x35017510 | 0x350202c0 | GetStateSnapshot(binary stream) (rigid helper sub_350AC5D0) |
| 17 | 0x350c8820 | 0x350d0350 | 0x350178e0 | 0x350207d0 | SetStateFromSnapshot(TSerialize) |
| 18 | 0x350c0730 | 0x350c48e0 | 0x3500b330 (returns 0) | 0x350207c0 | SetStateFromSnapshot(binary). The rigid version calls UpdatePosition/ComputeBBoxRE. |
| 19 | 0x350af110 | 0x350b8cf0 | =R | | PostSerialize fix-up: resolves collider/contact entity **ids → pointers** (world vt+216 = GetEntityById), drops dead contacts, rebuilds 520/528 arrays from the saved collider count in flags504 bits1..8 |
| 20 | 0x350ac750 | 0x350b9100 | =R | | GetStateChecksum: XOR of round(x·1024) over pos, q.xyz/w, v(808), w(796) |
| 24 | 0x350ae9b0 | 0x350bb6c0 | =R | | Queue an Action of type 2 (impulse) for the next step. It copies the 84-byte struct into the heap buffer at +1176 and sets +1184=1 under lock +1188. Step applies it (§4). |
| 26 | 0x350ccb20 | 0x350b64f0 | =R | | StartStep(dt): timeStepFull(+596)=dt, timeStepPerformed(+600)=0 |
| 27 | 0x350be4a0 | 0x350c2b70 | 0x3500a740 | 0x3501f400 | StepBack(dt): restores the prev-step state (rigid 980/1016/992/1004; artic 1468/1480 + per-joint snapshot +100..+172), rebuilds Iinv/P/L and repositions |
| 29 | 0x350aa4d0 | 0x350b66e0 | 0x350012f0 | 0x350012a0 | GetMemoryStatistics (sizeof 1200 = 0x4B0 / 1560 = 0x618) |
| 34 | 0x350c9df0 | 0x350d84a0 | 0x3501a150 | 0x35022c50 | **Step(dt)** (§4) |
| 37 | 0x350bcaa0 | 0x350c19a0 | 0x35001130 (thunk →R) | 0x35001140 | GetMaxTimeStep(dt): CCD limiter. Takes the fastest part's box extent along its motion and swept-tests when `vel·min(dt,maxTimeStep) > 0.7·size`. Logs "(RE TimeStep Error)" for parts without geometry. Sets +588/+592. |
| 38 | 0x35020a40 | 0x350028b0 | =R | | GetLastTimeStep: returns +604 |
| 39 | 0x350ac3e0 | 0x350b89b0 | =R | | GetContactCount(nMax): per-collider contact count, clamped |
| 40 | 0x350bdba0 | **0x350cf490 CRigidEntity__RegisterContacts** | 0x3500bb00 | 0x350085a0 | RegisterContacts(dt, nMaxPlaneContacts): turns the contact list into solver contacts (sub_350994B0 / sub_35099570 / sub_3509A9C0 = solver-side rigid-body helpers) |
| 41 | 0x350beb40 | 0x350c3460 | 0x350182c0 | 0x35020eb0 | Update(dt, damping): post-solver step. Applies damping, removes broken constraints, applies ground-velocity transfer, runs sleep logic. Returns "step finished". |
| 42 | 0x350ab8f0 | 0x350b83c0 | 0x35002840 | 0x35005870 | CalcEnergy(dt): m·v²+w·L plus constraint/contact correction terms, clamped to 2·m·maxVel² (dt≤0 gives the plain KE) |
| 43 | 0x350ac490 | 0x350b8a40 | 0x350018b0 | 0x35003040 | GetDamping(dt): `max(0, 1 − dt·max(damping or dampingFreefall, +692))`. Artic also takes the min with `1−dt·dampingLyingMode` when nContacts(1532) ≥ nCollLyingMode(1508). |
| 44 | 0x35020a50 | 0x350028c0 | =R | | returns +704 (sim-params field @+80, default 100) |
| 45 | 0x350ab5f0 | 0x350b7e40 | =R | | HasConstraintWith(pent, bCheckColliders): constraint flag 0x200 and buddy == pent, or recurses into colliders (vt+424, +360) |
| 46 | 0x350ccdc0 | 0x350b6e50 | 0x35020a70 | 0x35002900 | Get{v,w} snapshot (ipart,&v,&w). Rigid returns +708/+720 (values saved when the body is put to sleep, zeroed at step start). Artic returns joint+212/+224. |
| 47 | 0x350acf10 | 0x350ba290 | =R | | AddCollider: grows the 520/528 per-collider arrays |
| 48 | 0x350aadb0 | 0x350b9ff0 CRigidEntity__RemoveCollider | =R | | RemoveCollider(pent, bAlways): frees that collider's contacts back to the world pool |
| 49 | 0x350ab790 | 0x350b8020 | =R | | RemoveContactPoint(pent, pt, mindist²) |
| 50 | 0x350a9c20 | 0x350b60e0 | =R | | HasContactsWith(pent) (contacts or constraints) |
| 51 | 0x350aaf80 | 0x350b7750 | =R | | HasPartContactsWith(pent, ipart, bGreater) |
| 52 | 0x350a9c80 | 0x350b6140 | =R | | HasCollisionContactsWith(pent) (contacts only) |
| 53 | 0x350a9ce0 | 0x350b6190 | =R | | HasConstraintContactsWith(pent, flagsIgnore) |
| 54 | 0x350a9dd0 | 0x350b6270 | 0x35001000 | 0x35001020 | Entity detach/removal hook (base 0x350530b0 detaches from colliders and wakes them). Rigid first wakes all constraint buddies. Artic first releases pHost(+1360). |
| 55 | 0x350ab170 | 0x350b79b0 | =R | | **Awake(bAwake, iSource)**: sets byte508 and simclass (1 sleeping / 2 active), world reposition (sub_35080A00(world,ent,2,0)), minAwakeTime=0.1. A massless body wakes its colliders. Returns simclass. |
| 56 | 0x350ccd30 | 0x350b6e30 | 0x350010f0 | 0x35001100 | IsAwake(ipart): byte +508 (artic: joint+416 per part) |
| 59 | 0x350add40 | 0x350ce750 | 0x35001270 | 0x350030c0 | OnContactResolved(contact, iop): if the contact is a constraint (flags160 bit 0x40000000, index in bits16..29) and its impulse(+148) > maxForce·max(dt,0.01), it marks the constraint broken (flags \|= 0x10100) and posts **event type 8** (joint-broken, 128 B) to the world queue (+21088/+21096). Artic counts contacts (1532, 1496) and sets joint+424=1. |
| 60 | 0x350bb8c0 | **0x350d7590 CRigidEntity__DelayedIntersect** | =R | | DelayedIntersect: deferred narrow-phase; calls RegisterContactPoint and CheckContactConflicts |
| 61 | base 0x35037700 | | 0x35002520 | 0x35005610 | artic: restore raw state from a binary buffer (memcpy of the joint array 816·n and part-aux 92·nParts, v/w/awake), re-attach to host via an action_impulse-type struct, reposition |
| 62 | 0x350c2f30 | 0x350d3ad0 | =R | | OnEntitySplit(pentSrc, pentNew): copies buoyancy params (id 12) to the new entity. Moves constraints whose anchor point lies in the new piece (sub_35057E90 point-in-part). Posts events. |
| 64 | 0x350ccd40 | 0x350b6e40 | 0x350010b0 | 0x350010c0 | GetRigidBody(ipart): &body(+744); artic returns joint body (joint+432) |
| 65 | base | | 0x35002490 | 0x35005590 | GetRigidBodyData(out, ipart): artic adds host velocity (+1396) |
| 67 | 0x350ccd50 | 0x350b6ea0 | 0x350019c0 | 0x35003140 | GetVelocityAt(pt, ipart): body.v + w×(pt−com) (sub_35098FA0) |
| 69 | 0x35020a10 | 0x35002870 | =R | | GetMassInv: +824, or 0 if flags92 & 2 |
| 72 | 0x350aaae0 | 0x350b74c0 | =R | | RemoveContacts(ipart, side, pent, iEnt, bNotify) |
| 73 | 0x350aca60 | 0x350b9390 | =R | | DrawHelperInformation: base, plus contact points and normals (n·fixedStep·30) |
| 74 | 0x350b1130 | 0x350bd290 | 0x3500e690 | 0x3500d400 | RecomputeMassDistribution(ipart, bMassChanged): zero the body, accumulate each part (sub_3509D230 first / sub_3509D460 add), keep momentum, snapshot prev |
| 76 | 0x350aad50 | 0x350b76c0 | =R | | Deferred-request handler (base 0x35052c10). For a queued action 5 (add_constraint) it assigns the constraint id from counter +1144 when the id is unused. |
| 77 | 0x35020a30 | 0x350028a0 | =R | | returns constant 9 (unknown) |
| 78/79 | no-op | | =R | | step hooks (wheeled overrides: CheckAdditionalGeometry / post-step); called by Step at vt+624 / vt+632 |
| 80 | 0x350aa050 | 0x350b8160 | 0x35001220 | 0x35001260 | UpdateBreakableFlags: part flags80 0x400000 for parts holding breakable constraints (maxForce>0). Artic sets it on all parts. |
| 81 | 0x350b2b90 | **0x350cad80 CRigidEntity__GetPotentialColliders** | 0x35001050 | 0x35001060 | GetPotentialColliders(&list, dt) → count. Rigid queries GetEntitiesAround (sub_35084680). Artic returns the cached list +1544 / count +1552 built in Step. |
| 82 | stub 0 | | 0x35001070 | 0x35001080 | IsSelfCollisionEnabled(ipart, ipartOther): joint+392 mask bit |

Slots 2-6, 13-14, 21-23, 25, 28, 30-33, 35-36, 57-58, 63, 65 (rigid), 66, 68, 70, 71 and 75 (ComputeBBox, x86 0x3505f880) come from the base and are not overridden.

Vtable byte offsets seen in calls:

| byte offset | slot | role |
|---|---|---|
| 56 | 7 | SetParams |
| 72 | 9 | GetStatus |
| 80 | 10 | Action |
| 384 | 48 | RemoveCollider |
| 400 | 50 | |
| 416 | 52 | |
| 424 | 53 | |
| 440 | 55 | Awake |
| 448 | 56 | IsAwake |
| 512 | 64 | GetRigidBody |
| 552 | 69 | GetMassInv |
| 592 | 74 | RecomputeMass |
| 600 | 75 | ComputeBBox |
| 608 | 76 | |
| 624/632 | 78/79 | step hooks |
| 640 | 80 | UpdateBreakableFlags |
| 648 | 81 | GetPotentialColliders |

### 2. Params / Status / Action

Type-id meanings that the other agent decoded (struct sizes from the tables at 0x3537C640 / 0x3537C5F0) are reused here.

#### 2.1 CRigidEntity::SetParams (0x350c4cc0)

Flow:
1. Save the old bbox and simclass.
2. Call `CPhysicalEntity::SetParams` (0x3506ef20).
3. If the base handled the id, run the rigid post-processing below and return.
4. Otherwise handle ids **10** and **12** here. Anything else returns 0.

Post-processing after the base:

- **id 0 (pos)**
  - Recompute com(744) = q·offsfb(872) + pos when pos (+4) or pMtx (+40) is set.
  - When q (+28), pMtx3x3 (+48) or pMtx (+40) is set, set body q(756) = q·conj(qfb(856)).
  - +56 iSimClass ≤ 2 → byte508 = (simclass > 1).
  - If com moved more than 0.05 m or rotated more than 0.003 (squared distances), and flags (+60) bit 0x20 is not set:
    - wake the colliders (only when world+420 > 0),
    - drop contacts (sub_350AA9A0),
    - unless flags bit 2 is set, zero P/L/w/v when mass > 0,
    - when world+420 > 0, call sub_350B0940 (moves constraint anchors by the delta).
  - +32 scale changed: RecomputeMass(-1,1).
  - If the part flags include bit 1 and Minv == 0: wake entities around the bbox (GetEntitiesAround, mask 90). If +1104 < 0 and the body moved: wake inside an inflated box (mask 26).
  - Store prev com/q (980/1016).
  - Flags (+60) bit 0x40 → sub_350C4880 (re-query buoyancy areas).
- **id 6 (part)**
  - If ipart and partid are both unused, and the mass/density fields (+84, +88) are set: RecomputeMass(-1,1).
  - Else if mass, density, pMtx (+96) and pMtx (+104) are all unused and +12 is nonzero: RecomputeMass(last part, 0) when that part's mass > 0.
  - Else: drop contacts, then RecomputeMass(last, 1) if mass > 0 or mass/density were set.
- **id 15 (flags)**
  - flags (+4) or flagsOR (+8) contains 0x100 → flags504 \|= 0x10000000.
  - Flag 0x10000 was just cleared, the body was idle, submerged (+1136 > 0) and asleep → Awake(1).

**id 10: pe_simulation_params-like (84 B) (inferred names)**

| off | type | meaning (inferred) | stored |
|---|---|---|---|
| 4 | int | iSimClass (only for GET; SET handled at the end via simclass bits) | +71 byte; SET: `(v<<24)` into +68 hi-byte, byte508 = simclass>1, world reposition; refused (log via world vt+160) if current simclass ≥ 7 |
| 8 | float | maxTimeStep | +656 (def 0.02) |
| 12 | float | minEnergy | +652 (def 0.0049) |
| 16 | float | damping | +684 (def 0.1) |
| 20 | vec3 | gravity | +616 (def = world gravity, else (0,0,-9.81)). Also copied to gravityFreefall when +36 is unused. |
| 32 | float | dampingFreefall | +688 (def 0.1) |
| 36 | vec3 | gravityFreefall | +628 |
| 48 | float | maxRotVel | +696 (def 20) |
| 52 | float | mass | Rescales part masses: if body mass 0, distribute ∝ scale³·V·density; else scale the existing masses. Then RecomputeMass(-1,1). |
| 56 | float | density (≥0) | part.mass = scale³·geom.V·density |
| 60 | int | maxLoggedCollisions | flags508 bits22..26. (Re)allocates 8·n at +1088, resets the counter bits17..21, +1100 = 1e10. |
| 64 | int | disablePreCG | flags504 bit27 |
| 68 | vec3 | constant angular accel (applied as L += I·a·dt in Step) | +640 (def 0) |
| 80 | float | ? (def 100) returned by slot 44 | +704 |

GET (0x350aabd0) returns the same fields. density = mass/V(+828).

**id 12: buoyancy-like (80 B)**

World default medium at world+20928 (fields +392 density, +404 resistance; +440 tested for unused; defaults 1000/1000).

| off | meaning (inferred) | stored |
|---|---|---|
| 4 | waterDensity (absolute) | +1124 = v / worldDensity |
| 8 | kwaterDensity (ratio) | +1124 |
| 12 | waterDamping | +1120 |
| 16 | waterResistance | +1128 = v / worldResistance |
| 20 | kwaterResistance | +1128 |
| 64 | minEnergy while submerged | +1132 |
| 68 | ? | +1140 (def 0.05) |
| 76 | ? | +1192 |

#### 2.2 CRigidEntity::GetStatus (0x350b1b80)

Ids handled before the base:

- **7 awake.** +4 is the lag.
  - lag == 0: returns byte508.
  - lag != 0: returns 1 if +1104 < 0 or awake or `+1104 ≥ worldStep − lag`, else 0.

The base is called next. If the base handled the request and it was **1 (pos)** with timeBack(+120) > 0, the result is overridden with interpolated q = prevq(1016)·qfb and pos = prevcom(980) − q·offsfb.

Ids handled after the base returns 0:

- **26 constraint.**
  - +4 id, +8 idx (int; INT_MIN means search by id).
  - Out: +16 pt0, +28 pt1, +40 n (from contact +24/+36/+48), +12 flags (constraint+68).
- **8 dynamics.**
  - +12 v, +24 w.
  - +36 a = gravity + Minv·Fcoll(920).
  - +48 wa = Iinv·(Tcoll(932) − w×(R·Ibody·Rᵀ·w)).
  - +60 com.
  - +72..+104 Iinv (3×3 world).
  - +108 submerged fraction (+1136), +112 mass.
  - When asleep, v/w/a/wa are zero.
- **9:** returns (contact list non-empty).
- **19:** sub_350AF550 fits a contact patch through all contacts (stop flags 0, iCaller 0, tolerance 0.1·((dy+dx)+dz) of the bbox). +4 and +16 are **inputs** (the centre of mass and the gravity of the inside test); the fitted normal goes to a stack temporary, so nothing is written; the result is the inside test (P7 port: `entities::interface::status`).

Other ids return 0.

#### 2.3 CRigidEntity::Action (0x350c5d30)

**id 2 impulse (80 B; validator sub_35021B10 uses the strings "impulse", "angImpulse", "point", "ipart")**

Fields:

| off | type | meaning |
|---|---|---|
| +4 | vec3 | impulse |
| +16 | vec3 | angImpulse (opt) |
| +28 | vec3 | dv: added as m·dv (inferred) |
| +40 | vec3 | dw: added as Ibody_diag·dw (inferred) |
| +52 | vec3 | point (opt): adds (point−com)×impulse to angImpulse |
| +64 | int | partid |
| +68 | int | ipart |
| +72 | int | iApplyTime (base naming; P0 `ActionImpulse::i_apply_time`): ≠0 → accumulate into deferred P/L (+956/+968), applied at the next Step |
| +76 | int | iSource (P0 `ActionImpulse::i_source`): ==1 → clamp \|Minv·P\| ≤ 5 and \|Iinv·L\| ≤ 5, no wake; else wake (byte508=1; simclass 1→2 + reposition) and zero idle timer +252 unless +76>0 |

Only acts if Minv > 0. Immediate mode: P += …, L += …; v = Minv·P; w = Iinv·L; CapVelocities (sub_350B9440). Always also calls the base Action.

**id 4 reset.**
- Zero P, L, w, v and deferred P/L.
- +4 bClearContacts: drop contacts, then Action(6 update_constraint {id=unused, bRemove=1}) (removes all constraints).
- Zero Fext/Text (1048/1060), flags508 bits17..21, +1096 = +1104 = −2, +580 = 0.

**id 5 add_constraint (128 B).** Struct layout from slot 15's serializer:

| off | type | name | notes |
|---|---|---|---|
| 4 | int | id | INT_MIN → auto id from +1144 (spin-lock +1148) |
| 8 | ptr | pBuddy | -10 → static world dummy (0x3520C6E0). Buddy simclass > 4 is refused. |
| 16 | vec3 | "pt0" | must be set |
| 28 | vec3 | "pt1" | unused → pt0 |
| 40 | int | "partid0" | INT_MIN → part 0 |
| 44 | int | "partid1" | |
| 48 | quat | "qframe0" | w @60 tested |
| 64 | quat | "qframe1" | w @76 tested |
| 80 | float[2] | "xlim0","xlim1" | rotation limits about the frame x axis |
| 88 | float[2] | "yzlim0","yzlim1" | cone limits (stored as sin) |
| 96 | int | "flags" | see below |
| 100 | float | "damping" | → cinfo+72 |
| 104 | float | "radius" | sensor radius → cinfo+76 |
| 108 | float | "maxPullForce" | → cinfo+124 (breakableLin) |
| 112 | float | "maxBendTorque" | → limit sub-constraint +124 (breakableAng) |
| 120 | ptr | pConstraintEntity | e.g. a rope. If its GetType()==7: contact+128=0, cinfo flags=4. |

Flags at +96 (inferred):

| bit | effect |
|---|---|
| 1 | points/frames given in local frames (transformed by pos/q) |
| 4 | points relative to the parts |
| 0x400 | line constraint (internal type 2048) |
| 0x800 | plane constraint (1024); otherwise point (512) |
| 0x1000 | ignore buddy (clear the collider mask bit) |
| 0x2000 | extra full-rotation lock entry (768) |
| 0xC300 | kept in cinfo flags |

Building the entries:
1. Each constraint produces 1..3 consecutive "constraint slots" through sub_350B71E0(ent, pt0, pt1, ipart0, buddy, ipart1, internalType, flags) → slot index or −1.
2. The limit entries are internal types 256 / 1280 / 2304:
   - xlim → cinfo+60/+64 = lo/hi;
   - yzlim → +60 = lo, +64 = sin(hi).
3. Returns the id.

**id 6 update_constraint (48 B).**

| off | type | meaning |
|---|---|---|
| 4 | int | idConstraint (INT_MIN = all) |
| 8 | int | flagsOR |
| 12 | int | flagsAND |
| 16 | int | bRemove |
| 20 | vec3 | pt[0] (opt) |
| 32 | vec3 | pt[1] (opt) |
| 44 | int | flags: bit2 means points are in world coordinates |

- Removing: sub_350A9F70 per slot, then slot 80. Returns the count.
- With id = INT_MIN and bRemove: clears all masks and removes colliders that are left without contacts.

**id 7 register collision event (88 B).** Builds a world collision event (type 1) from: +4 pt, +16 n, +28/+40 v of each side (v unused → computed from the body at the point), +52, +56 pCollider, +64 partid, +68, +72/+76/+80/+82 int16 mat/part ids. Posts it with sub_35037EF0.

**id 8 awake (12 B).** Awake(+4, iSource=1). +8 float (opt) → minAwakeTime +608. Rejected when simclass > 6.

**id 10 set_velocity (40 B).**

| off | type | meaning |
|---|---|---|
| +12 | vec3 | v (opt) → P = m·v |
| +24 | vec3 | w (opt) → L = R·Ibody·Rᵀ·w |
| +36 | int | bRotationAroundPivot: v += w×(com−pos); v treated as 0 if unset |

Then CapVelocities. Wakes the body if v or w is nonzero. A zero velocity with Minv == 0 puts it to sleep.

**id 18 net sync (88 B)** ("CRigidEntity::Action() sync"). Validated by sub_350CD030 relative to base +4:

| off | name |
|---|---|
| +8 | "pos" |
| +20 | "vel" |
| +44 | "rot" (quat, w @56) |
| +60 | "angVel" |
| +76 | "minPosDiff" |

+74 byte flag set = take the values as given; clear = smoothing (positions blended by distance, velocities lerped by 0.1, slerp `sub_350CD980` for the rotation). Re-issues SetParams(pos) via vt+56 and Action(10) via vt+80.

Every other id goes to the base Action. Base ids used: 9, 13, 15, 16 per the other agent.

#### 2.4 CArticulatedEntity::SetParams (0x3501c850, x86 0x35024c70)

Pre-checks:
- id 8 with +4 == 100 clears +1504 ("params locked" flag) and returns.
- If +1504 != 0, all SetParams are ignored.

Then it calls **CRigidEntity::SetParams**. Post-processing for ids the rigid/base handled:

- **0 pos**
  - If q changed and the root joint is free (joint.iParent < 0): rotate offsPivot and the root joint's quat0 (+240), set q0changed (+420) = 1, and rotate pivot (+340) when joint flag 0x200000 is set.
  - If +1452 == 0, force entity q and q_new to identity: an articulated entity carries its rotation in the joints.
  - If moving with +1424 set and mass > 0, translate all joint body positions (+432).
  - +60 (bRecalc) → sub_35011BC0(joint,1) for all, ComputeBBox, sub_35001550 (sync part poses).
- **6 part.** May set +1452. Wakes neighbours when hosted / massless. Re-anchors the part to its joint (pivot +72 in part-aux).

**id 8 articulated body (176 B) (inferred names)**

| off | type | meaning | stored |
|---|---|---|---|
| 4 | int | bGrounded | +1344. On change, re-roots joint 0: pivot (+352) / offsPivot fix-up, or clears joint 0 limits (±1e15 at +256/+268) and dq. Sets awake. |
| 8 | int | ? | +1420 |
| 12 | int | bCheckCollisions-like flag | +1424 |
| 16 | vec3 | pivot | offsPivot +1236 (local), world +1224 |
| 28 | vec3 | a (host accel) | +1248 |
| 40 | vec3 | wa | +1260 |
| 52 | vec3 | w | +796 |
| 64 | vec3 | v | +808 |
| 76 | float | scaleBounceResponse | +1340 (def 1) |
| 80 | int | bApply_dqext | qext += dq for all joints |
| 84 | int | bAwake | byte508. 0 → +1332 = 10. |
| 88 | ptr | pHost | +1360 (AddRef via vt+16, Release vt+24, obtained through vt+256) |
| 96 | vec3 | posHostPivot | +1368 |
| 108 | quat | qHostPivot | +1380 (w @120) |
| 124 | int | ? | +1352 |
| 128 | int | nCollLyingMode | +1508 (def 5) |
| 132 | vec3 | gravityLyingMode | +1512 (def = gravity) |
| 144 | float | dampingLyingMode | +1524 (def 0.2) |
| 148 | float | minEnergyLyingMode | +1528 (def 0.0144) |
| 152 | int | iSimType | +1432 (def 1) |
| 156 | int | iSimTypeLyingMode | +1436 (def 1) |
| 160 | int | (GET only) | +1348 |
| 164 | int | nJointsAlloc | grows joints (+1208, cap +1220), parts (cap +292) and part-aux (+1200) |
| 168 | int | ? → sub_35019660(ent, v, 0) | joint/pose resync |

**id 5 params_joint (208 B; error string "CArticulatedEntity:SetParams(params_joint)")**

| off | type | meaning | joint field |
|---|---|---|---|
| 4 | int | flags | +236. Bits 0..2 lock axis i (zero dq[i]). Bit (64<<i): kd is given as a ratio (kd·2√ks). Bit 0x10000 rejects impulses. Bit 0x200000 = root follows entity rotation. |
| 8 | int | flagsPivot | bit 8 → pivot in the parent part frame; bit 4 → ... |
| 12 | vec3 | pivot | pivot[0] +340 (parent body frame), pivot[1] +352 (child) |
| 24 | quat | q0 | quat0 +240 ("quat0") |
| 40 | float* | pMtx0 (3×3) | converted to q0 |
| 48 | vec3 | limits[0] | +256 |
| 60 | vec3 | limits[1] | +268 |
| 72 | vec3 | bounciness | +280 |
| 84 | vec3 | ks | +292 |
| 96 | vec3 | kd | +304 |
| 108 | vec3 | qdashpot | +316 |
| 120 | vec3 | kdashpot | +328 |
| 132 | vec3 | q | +0 and snapshot +100 ("q") |
| 144 | vec3 | qext | +12 ("qext"). dqext(+36) = wrap(qext_new−qext_old)·rAnimDt; limits auto-widened. |
| 156 | vec3 | ? (target) | +176 |
| 168 | int | op[0] parent part id | (−1 = root) |
| 172 | int | op[1] child part id | joint id +428 |
| 176 | int | nSelfCollidingParts | |
| 184 | int* | pSelfCollidingParts | mask +392 |
| 192 | int | bNoUpdate | |
| 196 | float | animTimeStep | |
| 200 | float | ranimTimeStep | |

Creating or reparenting a joint inserts it into the depth-first joint array (memmove of 816-B records), fixes iParent/nChildren and calls sub_35001140.

GET id 5 is found by +172 (joint id) and returns world pivot, q0, limits, ks/kd, q, qext, op[0]. GET id 8 returns the table above.

#### 2.5 CArticulatedEntity::GetStatus (0x35001b50)

- **6 joint.**
  - In: +4 idChildBody or +8 partid.
  - Out: +12 flags, +16 q (snap +100), +28 qext, +40 dq (snap +112), +52 quat0.
  - Read under spin-lock +1536.
- **8 dynamics.**
  - Per part: v (+136), w (+148), a = gravity + Minv·F(+188), wa from +200 / +160, com (+432), mass (+508), energy, +120 = joint+424 (has contacts).
  - Whole entity: v/w of the root, mass-weighted com, total mass, energy, +120 = nContacts (+1532).
- Everything else goes to rigid. id 7 returns 0 if mass == 0. id 20 sets +4 = 1.

#### 2.6 CArticulatedEntity::Action (0x3500f7c0)

- **5** goes to rigid.
- **17 set part poses (104 B):**

  | off | meaning |
  |---|---|
  | +8 | int* partIds |
  | +16/+24 | quat array ptr / stride |
  | +32/+40 | pos array ptr / stride |
  | +48 | n |
  | +56 | int* nOut |
  | +64 | q offset |
  | +80 | pos offset |
  | +96 | object with vt (+8 validate, +16 release) |

  Writes part-aux q/pos (+12/+0), then ComputeBBox, reposition, sync parts.
- **2 impulse.**
  - Ignored when +1424 is set with a small impulse, iApplyTime == 4, or there are no joints.
  - If ipart is unset, finds the part whose box contains the point.
  - Applies to the joint body; rejected if joint flag 0x10000.
  - With iApplyTime 0, impulses below a threshold are ignored (0.04² scaled).
- **4 reset:** zero v/w/P/L for the entity and all joints, re-pose.
- **10 set_velocity:** per part (+4 ipart / +8 partid) sets the joint body v (+496), w (+484) with clamping. Without a part it sets the whole entity.
- Others go to CRigidEntity::Action.

### 3. Member layouts

#### 3.1 CRigidEntity (0x4B0 = 1200 B; ctor 0x350acbc0)

| off | type | meaning (inferred) / default |
|---|---|---|
| 200 | ptr | → +448 (pending pose used by the base) |
| 448 | vec3 | pos_new |
| 460 | quat | q_new (def identity) |
| 476 | vec3[2] | bbox_new |
| 500 | int | part-count threshold for per-part bbox (1000000; artic 0) |
| 504 | bits | 0x1 swept-CCD active; 1..8 saved nColliders; 0x200/0x400 contact-solver state; 0x1000 stepped-back; 0x1E000 counters; 0x20000 CCD enabled (artic clears); 0x3FC0000 "need GetPotentialColliders"; 0x4000000 submerged; 0x8000000 disablePreCG; 0x10000000 flags-0x100 latch |
| 508 | bits | byte0 **bAwake** (1); 8..12 sleep-frame counter; 13..16 sub-step counter; 17..21 nLoggedColl; 22..26 maxLoggedColl |
| 512 | bits | low 10 bits |
| 520 | ptr[] | per-collider first-contact ptr (contacts are list-linked, flag 0x80000 = last for this collider) |
| 528 | u64[] | per-collider constraint-slot mask (64 slots) |
| 536/544 | ptr | contact circular list head (next/prev); 552 nContacts |
| 560 | Contact[176 B] | constraint contacts (see 3.3) |
| 568 | CInfo[128 B] | constraint infos (see 3.3); 576 capacity |
| 580 | u32 | awake-history shift register |
| 584 | int | n of extra (constraint) entities |
| 588/592 | float | CCD: min extent / size along fastest axis |
| 596 | float | timeStepFull (def 0.01); 600 timeStepPerformed |
| 604 | float | last dt |
| 608 | float | minAwakeTime (0.1 on wake) |
| 612 | float | forced dt for next step (0 → normal; 0.001 after StepBack(0)) |
| 616 | vec3 | gravity |
| 628 | vec3 | gravityFreefall |
| 640 | vec3 | const angular accel (sim +68) |
| 652 | float | minEnergy 0.0049 (artic 0.000625) |
| 656 | float | maxTimeStep 0.02 |
| 660 | vec3 | v accum (sleep test) |
| 672 | vec3 | w accum |
| 684 | float | damping 0.1 |
| 688 | float | dampingFreefall 0.1 |
| 692 | float | per-step min damping (set by buoyancy) |
| 696 | float | maxRotVel 20 |
| 700 | float | 0.1, reset each step (penetration / unprojection tolerance) |
| 704 | float | 100 (sim +80) |
| 708 | vec3 | v at sleep |
| 720 | vec3 | w at sleep |
| 736 | ptr | ground contact used for velocity transfer |
| **744** | RigidBody (212 B, ctor 0x35098ea0) | body; fields +0 pos(com) … +208 (see 3.2) |
| 956 | vec3 | deferred P |
| 968 | vec3 | deferred L |
| 980 | vec3 | prev com |
| 992 | vec3 | prev v |
| 1004 | vec3 | prev w |
| 1016 | quat | prev body q |
| 1032 | float | cached energy |
| 1036 | float | energy clamp |
| 1040/1044 | | timer / small state bits |
| 1048 | vec3 | Fext accum |
| 1060 | vec3 | Text accum (zeroed per step) |
| 1072 | vec3 | pending position correction (added in Step) |
| 1088 | ptr[] | logged-collision ptrs |
| 1096 | int | stamp (−2) |
| 1100 | float | 1e10 |
| 1104 | int | last step id with external contact (−2) |
| 1112 | ptr | |
| 1120 | float | waterDamping 0 |
| 1124 | float | waterDensity ratio 0.001 |
| 1128 | float | waterResistance ratio 0.001 |
| 1132 | float | minEnergy submerged 1e-4 |
| 1136 | float | submerged fraction |
| 1140 | float | 0.05 |
| 1144 | int | next constraint id (1); 1148 lock |
| 1152 | | contact read-lock |
| 1176 | ptr | pending impulse (slot 24); 1184 flag; 1188 lock |
| 1192 | float | buoyancy +76 |

#### 3.2 RigidBody (212 B; body = entity+744; joint body = joint+432)

| off | field (inferred) |
|---|---|
| 0 | pos (com) |
| 12 | q |
| 28 | P |
| 40 | L |
| 52 | w |
| 64 | v |
| 76 | M |
| 80 | Minv |
| 84 | V |
| 88 | Ibody diag |
| 100 | Ibody_inv diag |
| 112 | qfb (body-frame quat) |
| 128 | offsfb |
| 140 | Iinv 3×3 world |
| 176 | Fcollision |
| 188 | Tcollision |
| 200 | ptr |
| 208 | float (Eaux) |

Helpers:

| addr | role |
|---|---|
| 0x35098f60 | zero |
| 0x3509ce40 | UpdateState: Iinv = R·diag(Ibody_inv)·Rᵀ; v = Minv·P; w = Iinv·L |
| 0x3509e180 (x86 0x350a5af0) | Step(dt), see §4 |
| 0x35098fa0 | velocity at r |
| 0x3509d230 | create from part |
| 0x3509d460 | add part: new part's inertia `R·diag((m/V)·I)·Rᵀ` in the old principal frame (`R` from `q_body*⊗q`), parallel-axis shift of both to the combined CoM, sum, Jacobi 0x3511eaa0 (threshold 0, eigenvectors = rows) → Ibody; `q ← q⊗q_rot*`, `qfb ← q_rot⊗qfb`, `offsfb += q_ent⁻¹·Δcom`; `m == 0`/NaN → no-op; `!(|M+m| ≥ M·1e-4)` → zero M/Minv/Ibody/Ibody_inv/P/L/w/v (port: `entities::base::geometry::body_add`) |
| 0x3511eaa0 | `matrixT<float>::jacobi` cyclic-max Jacobi (port: `math::eigen::jacobi_sym`) |

#### 3.3 Constraint storage (per slot i, 0..63, bit i of the collider masks at +528)

**Contact (+560, 176 B):**

| off | field |
|---|---|
| 0/8 | list next/prev |
| 24 | pt[0] |
| 36 | pt[1] |
| 48 | n |
| 64 | pent[0] |
| 72 | pent[1] |
| 80 | ipart[0] |
| 84 | ipart[1] |
| 96/104 | pbody[0..1] |
| 112 | n_local |
| 124 | friction |
| 128 | flags (0x200 constraint, 0x80000 last-of-collider) |
| 136 | vreq |
| 148 | Pspare (impulse) |
| 152 | penetration |
| 156 | material ids (bits 2..10 and 17..25 into world friction table +6504) |
| 160 | constraint link (0x40000000 \| slot+1 in bits 16..29) |
| 272/280 | part-body ptrs |
| 304 | type flags (0x400 / 0x800) |

The same layout is used by the normal contact list at +536.

**CInfo (+568, 128 B):**

| off | field |
|---|---|
| 0 | id |
| 4 | qframe_rel[0] |
| 20 | qframe_rel[1] |
| 60/64 | limit lo/hi |
| 68 | flags: bit0 x-limit, bit1 yz-limit, bit2 hidden / no-serialize, 0x200 active, 0x8000 unbreakable, 0x10000 broken, \|0xC300 user |
| 72 | damping |
| 76 | radius |
| 80 | pConstraintEnt |
| 92 | buddy-part q |
| 108 | own-part q |
| 124 | maxForce |

The serializer (slot 15) rebuilds an action_add_constraint from slots via sub_350B0500 (x86 0x350c5d40), merging the 1..3 slots that share the id.

#### 3.4 CArticulatedEntity additions (0x618 = 1560 B; ctor 0x35001380)

The ctor:
- calls the rigid ctor,
- sets simclass byte 4,
- clears byte508,
- sets minEnergy 0.000625, damping 0.1,
- clears flags504 0x20000 and flags92 0x40.

| off | meaning (inferred) / default |
|---|---|
| 1200 | PartAux[92 B] per part: +0 pos (in joint body), +12 q, +56 q (part→body), +72 pivot offset, +84 iJoint |
| 1208 | Joint[816 B]; 1216 nJoints; 1220 capacity |
| 1224 | pivot (world) |
| 1236 | "offsPivot" |
| 1248 | a (host) |
| 1260 | wa (host) |
| 1272..1316 | 3×3 + vec3 root/host spatial terms read by the Featherstone driver |
| 1332 | timer (10 when forced asleep) |
| 1336 | 10.0 |
| 1340 | scaleBounceResponse 1 |
| 1344 | bGrounded |
| 1348 | ? |
| 1352 | ? (params8 +124) |
| 1360 | pHost |
| 1368 | posHostPivot |
| 1380 | qHostPivot (identity) |
| 1396 | host velocity (added in slot 65) |
| 1408 | ? (host-velocity related, read in slot 61) |
| 1420 | params8 +8 |
| 1424 | params8 +12 |
| 1428 | 0 |
| 1432 | iSimType 1 |
| 1436 | iSimTypeLyingMode 1 |
| 1440 | 1 |
| 1448 | |
| 1452 | "re-pose needed" |
| 1456..1464 | |
| 1468 | prev pos |
| 1480 | prev v |
| 1492 | 1 |
| 1496 | n non-static contacts |
| 1504 | params-locked |
| 1508 | nCollLyingMode 5 |
| 1512 | gravityLyingMode |
| 1524 | dampingLyingMode 0.2 |
| 1528 | minEnergyLyingMode 0.0144 |
| 1532 | nContacts this step |
| 1536 | joint read spin-lock |
| 1544 | potential-collider list |
| 1552 | count |

**Joint (816 B):**

| off | field |
|---|---|
| 0 | "q" (3 Euler-like angles) |
| 12 | "qext" |
| 24 | "dq" |
| 36 | dqext |
| 84 | quat (part-frame) |
| 100 | snapshot: q +100, dq +112, body pos +124, v +136, w +148, body q +160 (used by status / StepBack) |
| 176 | vec3 (params +156) |
| 188 | a |
| 200 | wa |
| 212 | v-snap |
| 224 | w-snap |
| 236 | flags |
| 240 | "quat0" |
| 256 | limits[2] |
| 280 | bounciness |
| 292 | ks |
| 304 | kd |
| 316 | qdashpot |
| 328 | kdashpot |
| 340 | pivot[2] |
| 364 | iStartPart |
| 368 | nParts |
| 372 | iParent (−1 root) |
| 376 | nChildren / subtree size |
| 392 | self-collision mask (u64) |
| 416 | bAwake |
| 420 | "q0changed" |
| 424 | bHasContacts |
| 428 | id |
| **432** | RigidBody (212 B) |
| 800 | ptr to 6×6 articulated inertia (scratch pool) |
| 808 | ptr |

### 4. Step functions

#### CRigidEntity::Step(dt): 0x350c9df0 (x86 0x350d84a0)

1. Lock the colliders. Detach constraint-entity colliders (simclass 7) via RemoveCollider. OR together HasCollisionContacts.
2. Snapshot prev com/q.
3. Zero the sleep-snapshot v/w (708/720) and +692.
4. If timeStepPerformed > full−0.001 or there are no parts, return 1 (done).
5. timeStepPerformed += dt. dt may be overridden by +612.
6. **Asleep** (byte508 = 0):
   - optionally refresh GetPotentialColliders (vt+648),
   - hook vt+624,
   - bbox_new = bbox,
   - skip to 9.
7. **Awake:**
   - zero Fext/Text;
   - **body.Step(dt)**;
   - pos += correction(+1072);
   - CapVelocities;
   - q_new = qbody·conj(qfb), pos_new = com − q_new·offsfb, normalise q_new.
8. **CCD and contacts:**
   - If the displacement exceeds 0.71·size (+588/+592), test a sweep: revert pos_new to the old pose, ComputeBBox, **CheckForNewContacts** (sub_350B3440 = x86 0x350cb650; sweeps the part geometries against GetPotentialColliders results; world-geometry intersection via geometry vt+488).
   - On a hit, the body is moved to the time of impact (inferred).
   - Otherwise ComputeBBox, hook, and CheckForNewContacts at the new pose.
   - Then **CheckContactConflicts** (sub_350B9880 = x86 0x350c0a00) and ComputeBBoxRE (sub_350ABF20).
   - Each contact goes through **RegisterContactPoint** (sub_350B5E10 = x86 0x350d5a40; flags e.g. 524352). It allocates from the world pool, links into +536 and the per-collider list, and fills the 176-B contact.
   - sub_350BADE0 (x86 0x350d6b10) handles the unprojection / penetration response. It writes deferred P/L and logs collision events (world event alloc sub_3507D400).
9. sub_350ADB90 verifies existing contacts (drops contacts with removed or asleep partners; possibly x86 VerifyExistingContacts 0x350ba720).
10. A pending queued impulse (+1184) is applied.
11. World reposition (sub_35080A00(world,ent,1,bbox_new)) and **UpdatePosition** (sub_350AC080; x86 UpdatePosition 0x350bab60): commits pos/q and the per-part new poses under lock +72.
12. sub_350C4880 queries up to 4 buoyancy/medium areas (params id 12). **ApplyBuoyancy** (sub_350C0E50 = x86 0x350d1d80) uses gravity (+616 with contacts, +628 freefall). If flags92 & 8, sub_350C2790 applies an area effect (wind/air, inferred).
13. **Integrate external forces (momentum form):**
    - P += deferredP; L += deferredL;
    - P += m·g·dt;
    - P += Fext·dt; L += Text·dt;
    - L += (R·Ibody·Rᵀ)·angAccel(+640)·dt.
14. body.UpdateState. Hook vt+632. Zero Fcoll/Tcoll. UpdateState again. CapVelocities. Store prev v/w.
15. Small-mass and resting checks. Update flags504 bit 11 and the sub-step counter in 508 bits 13..16.
16. Return "finished" = (bit11 clear) \| (counter ≥ 4).
17. Profiling via sub_3507D040 when world+336 is set.

**body.Step (0x3509e180).**
- UpdateState.
- x += v·dt.
- If \|w·dt\|² ≥ 9e-6: q = quat(axis = w/\|w\|, angle = \|w\|·dt)·q (exact). Otherwise first-order q += ½·(w,0)·q·dt.
- If Minv > 0: Iinv = R·diag(Ibody_inv)·Rᵀ, w = Iinv·L.

Angular momentum is the primary state. This is semi-implicit Euler on (P, L).

**After the world solver: Update (slot 41).**
- Velocity scale by damping.
- Broken-constraint removal (CInfo flag 0x10000 → Action 6 remove).
- Ground-velocity transfer from the first contact with a moving collider (vt+368 GetVelocityAt).
- **Sleep:**
  - E = ½·(Minv·w·L + \|v\|²) + Eaux.
  - Threshold = minEnergy (or +1132 when submerged and without colliders), ×0.1 when resting on ≥3 contacts with a single collider.
  - If E < threshold for 2 consecutive frames (508 bits 8..12 ≥ 2) and minAwakeTime ≤ 0: P, L, v, w = 0 (saved to 708/720), simclass 2→1, world reposition.
  - Idle timer: base +252/+256. Bodies below z = −200 count down faster.
- Awake history goes into +580.

**GetMaxTimeStep (slot 37):** returns dt clipped so that the fastest part moves less than about 0.7 of its box size. Swept-box test against neighbours when flag 0x20000 is set.

#### CArticulatedEntity::Step(dt): 0x3501a150 (x86 0x35022c50)

1. Detach constraint colliders. Resync joints (sub_35011BC0(j,3) = recompute the joint body pose from q, pivot and parent; x86 0x350122d0). sub_35019660 (x86 0x350220a0) syncs with the host / pivot.
2. When "simple" or lying mode applies, per joint: body.Step(dt) on joint+432, then sub_35012D10(j,3) (derive q/dq from the body pose).
3. Build potential colliders into +1544 / +1552 with GetEntitiesAround (mask 131423). Same-type articulated neighbours are passed to sub_350B2220. Others are woken; static ones go to sub_3507A140.
4. **StepJoint** sub_35013590 (x86 0x3501af00) runs recursively over the joint tree:
   - integrates each joint body (sub_3509E180),
   - CheckForNewContacts and RegisterContactPoint per joint body,
   - uses q/dq limits, the pivot (1224..1244), pos_new / q_new (448..472) and +1460.
5. **Featherstone solver** driver sub_350161D0 (x86 0x3501dfd0); the world profiler slot is named "featherstone":
   - sub_350089D0 (x86 0x35017030): recursive articulated-body inertia Ia per joint. Uses 6×6 matrices from a global scratch pool (36 floats each; counter 0x35239074, capacity 0x35209990), pointer stored at joint+800. 6-vectors come from pool 0x35239078/0x35209994. 6×6 multiply is sub_35020120.
   - sub_35004AE0 (x86 0x350074d0): recursive joint pass (Za / bias accumulation).
   - sub_350070D0 (x86 0x35018310): velocity / acceleration propagation using the pivot and q_new.
   - sub_3501FCD0: matrix inversion with a success flag, used for the root 6×6 and +1272.
   - sub_35003280: contact-impulse application through the tree (dt, contact array).
   - sub_350096D0 (x86 0x350144a0): impulse propagation, controlled by flag +1424.
   - sub_35005500: final per-joint update.
   - sub_35022630: contact-frame helper. It reads CInfo+48 and Contact+36, so constraints are included.
   - Joints are then resynced (sub_35011BC0(j,2/3)) and Ia is recomputed after impulses (repeated sub_350089D0/sub_35004AE0 calls).
   - Joint springs (ks/kd), dashpots and limits with bounciness come from the joint fields at +256..+328.
6. ComputeBBox, reposition, sub_35001550 (x86 0x35002d60: copy joint-body poses into the parts' new poses under lock +72), sub_350016F0 (x86 0x35002ed0: refresh the per-joint snapshot +100..+172 under lock +1536).
7. Buoyancy (sub_350C4880 + sub_350C0E50 with gravityFreefall +628), verify contacts (sub_350ADB90), profiling.

**Artic Update (slot 41, 0x350182c0):** sub_35012D10 / sub_35011BC0 per joint, lying-mode switching (nContacts ≥ nCollLyingMode switches to gravity/damping/minEnergy/iSimType LyingMode), sleep, sub_350016F0.

### 5. Main uncertainties

- The type-id ↔ name mapping is behavioural. Only action_impulse and params_joint are string-confirmed. Status ids 9/19/26 and action 7/17/18 meanings are inferred.
- Unknown fields:
  - sim-params +68 vec3 / +80 float (+640 / +704),
  - buoyancy +68/+76,
  - artic params8 +8/+124/+160/+168,
  - joint +176, +188/+200 semantics,
  - slot 77 (returns 9),
  - flags504 bits 13..16.
- The internal constraint type codes (256 / 512 / 768 / 1024 / 1280 / 2048 / 2304) are only partly understood. 512 / 1024 / 2048 are tied to point / plane / line through flags 0x400 / 0x800.
- The Featherstone sub-function roles (which pass computes Ia, Za or the accelerations) are inferred from recursion shape and 6×6 pool usage. They were not traced instruction by instruction.
- The x86 matches for sub_350ADB90 (VerifyExistingContacts?) and sub_350AC080 / sub_350ABF20 (UpdatePosition / ComputeBBoxRE) are inferred from caller sets (x86 callers 0x350c2b70 / 0x350c3460 / 0x350c48e0 equal slots 27 / 41 / 18). They are not in func_match.csv.


---

## 4. CLivingEntity / CWheeledVehicleEntity

Clean-room: everything below is derived from the decompiles in `D:\aa-re\cryphysics`. Names in quotes are strings found in the binary. Anything named from behaviour is marked (inferred). Offsets are decimal unless prefixed 0x. x64 addresses omit nothing (full 0x35xxxxxx). x86 = 1.2 client DLL match from `compare/vtable_slots.txt` / `func_match.csv` ("–" = no match).

### 0. Conventions shared by both classes

| item | value |
|---|---|
| "unused" float test | `(bits & 0xFFA00000) == 0xFFA00000`; value written by the code to mark unused = `0xFFBFFFFF` (-4194305, shows as NAN) |
| "unused" int | `0x80000000` |
| "unused" pointer/qword | `-1` |
| deferred-command wrapper | SetParams/Action first call `sub_35023200` / `sub_35023650` (AddGeometry: `sub_35022A50`, RemoveGeometry `sub_35022F00`); if it queued the request (world busy) the function returns 1 immediately. Base-agent territory. |
| entity locks | `+72` main state spinlock (0x10000 = writer, +1 reader, byte +74 writer flag). Living: `+1112` params lock (byte +1114), `+1116` step lock. Wheeled: `+11220` params lock (byte +11222). |
| quaternion layout | (x,y,z,w); living q at +420..+432 (w at +432) |
| base members used | pos +408 (0x198), q +420, BBox +8..+28, flags +92 (0x5C), simclass byte +71, parts ptr +280, nParts +288, world +120, colliders +208/+216, foreign data +32/+40 |

Struct-size table (from another agent, sub_351E5900): params id1=64 B, id2=144, id4=104, id16=80; action id1=24, id2=80, id3=36, id4=8, id10=40; geomparams type1=184.

---

### 1. Overridden vtable slots

#### CLivingEntity (vtable 0x351EE1D8, 77 slots; compared with the CPhysicalEntity/"Static" column)

| slot | x64 | x86 1.2 | role (inferred unless string-named) |
|---|---|---|---|
| 0 | 0x35039020 | 0x3502C3F0 | scalar deleting dtor → `sub_35024E60` (real dtor: clears the part geometry pointers, frees contact array +1088 and collider geometry +656) |
| 1 | 0x35037C40 | – | GetType → returns **4** |
| 7 | 0x35028BF0 | 0x3502F5D0 CLivingEntity__SetParams | SetParams |
| 8 | 0x35025040 | 0x3502A4A0 | GetParams |
| 9 | 0x35029A30 | 0x35030500 | GetStatus |
| 10 | 0x3502A300 | 0x35030E90 CLivingEntity__Action | Action |
| 11 | 0x35024D30 | 0x350291F0 | AddGeometry: clears part flags bits 0xA000 on part 0, calls base, then sets 0x400000 on the new last part |
| 12 | 0x35024D80 | 0x35029240 | RemoveGeometry: base, then if 1 part left set 0x8000 on part 0 flags |
| 15 | 0x35025270 | 0x3502AA00 | GetStateSnapshot(TSerialize): writes "pos"(+408), "vel"(+448), "velRequested"(+460) |
| 16 | 0x3502C780 | 0x3502A680 | snapshot-variant: re-attaches ground collider while preserving pos/vel/q/flying/timeFlying/ground normal (calls `sub_3502C700`); bumps +932 when world frame id changed |
| 17 | 0x35025BB0 | 0x3502C420 | SetStateFromSnapshot(TSerialize): reads pos/vel/velRequested; optionally extrapolates pos by vel·(frame delta·world dt); applies pos via SetParams(type 0, flags 0x31); sets vel/velRequested |
| 18 | 0x35025BA0 | 0x3502AB50 | SetStateFromSnapshot (stream variant) → returns 0 (unsupported) |
| 24 | 0x3502AA60 | 0x35031560 CLivingEntity__SyncFromNet | network sync (see §4.4) |
| 26 | 0x35024DC0 | 0x35029270 | StartStep(dt): `dt = min(dt, +512)` if +512>0 then +512=0; +924=dt, +928=0, +1096 (contacts)=0 |
| 29 | 0x35024E00 | 0x350292F0 | GetMemoryStatistics: adds sizeof 1128 (0x468) |
| 34 | 0x3502DED0 | 0x35035630 (unnamed) | **Step** (§4) |
| 37 | 0x35025390 | 0x3502AB60 | GetMaxTimeStep(dt): returns `+924-+928` if ≥0.001 and < dt, else dt |
| 40 | 0x35027190 | 0x3502D1A0 | RegisterContacts (inferred): turns each stored living contact (+1088, stride 80) into 2 solver contacts (176-byte, via `sub_3509A9C0` + `sub_350994B0`), plus a ground contact with the ground collider if a rigid proxy (+1104) exists and not flying |
| 41 | 0x3502C240 | 0x3503F030 | Update after solver (inferred): posts collision events for contacts (`sub_35026300`), prunes colliders list, pulls vel and yaw rotation back from the rigid proxy +1104 (scaled by a3 damping) |
| 42 | 0x35026FB0 | 0x3502D040 | GetEnergy: m·|v|² (blended with proxy), plus contact terms |
| 54 | 0x35037CD0 | 0x3502A310 | (base role; "OnNeighbour removed"-like) releases ground collider +960 then base |
| 55 | 0x35024F90 | 0x3502A3F0 | Awake(bAwake): drops ground collider if it is dead/sim-class 7; flag 0x80 of +952 = awake; if !awake zero vel(+448), velRequested(+460) and +472 vec |
| 59 | 0x350279B0 | 0x3502DA50 | on-constraint/contact (inferred): clips velRequested and vel against a contact normal (removes the into-normal component) |
| 64 | 0x35027700 | 0x3502D6F0 | GetRigidBody(ipart): for a3≠0 creates/refreshes a rigid-body proxy at +1104 (0xD4 bytes, `sub_35098EA0`, mass +580, inertia from size.x·size.y·size.z·8) at pos + up·heightCollider |
| 65 | 0x35037D40 | 0x3502A220 | fills a body-info struct: pos, vel(+448), mass(+580), invMass(+584) |
| 67 | 0x35037DB0 | 0x3502A3B0 | add invMass to diag of a 3x3 K matrix (inferred contact matrix) |
| 68 | 0x35037C60 | 0x3502A290 | same, for a 7-wide layout (elements 18/25/32) |
| 69 | 0x35037C50 | 0x3502A280 | GetMassInv → +584 |
| 73 | 0x3502BEC0 | 0x3502DC90 | DrawHelperInformation (uses +656 collider, +672 head sphere) |
| 75 | 0x35026BC0 | 0x3505F880 (low sim) | ComputeBBox: base then extends by pivot offset and by head sphere (if +732>0) |

Not overridden (inherit CPhysicalEntity): 2–6, 13, 14, 19–23, 25, 27, 28, 30–33, 35, 36, 38, 39, 43–53, 56–58, 60–63, 66, 70–72, 74, 76.

#### CWheeledVehicleEntity (vtable 0x351F2388, 84 slots; compared with CRigidEntity column)

| slot | x64 | x86 1.2 | role (inferred unless string-named) |
|---|---|---|---|
| 0 | 0x35110810 (=CRigidEntity dtor) | – | not overridden (wheel array is inline) |
| 1 | 0x35024CD0 | – | GetType → **3** |
| 7 | 0x35106740 | 0x35116CD0 | SetParams |
| 8 | 0x35104B50 | 0x35113120 | GetParams |
| 9 | 0x3510A090 | 0x351184D0 | GetStatus |
| 10 | 0x351094A0 | 0x35117820 | Action |
| 11 | 0x3510BBB0 | 0x35119FC0 CWheeledVehicleEntity__AddGeometry | AddGeometry (wheels) |
| 12 | 0x351072F0 | 0x3511AE00 | RemoveGeometry: removes wheel slot (memmove 544-byte records) and re-links part→wheel pointers |
| 15 | 0x35105570 | 0x35113B50 | GetStateSnapshot(TSerialize): `sub_351054B0` writes "pedal"(+11064) "steer"(+11068) "wengine"(+11076) "handbrake"(+11148) "curGear"(+11132), then rigid |
| 16 | 0x35105260 | 0x351137F0 | rigid's snapshot(stream) then return 1 |
| 17 | 0x35105630 | 0x35116500 | SetStateFromSnapshot(TSerialize): reads the 5 values; re-issues action_drive (id 3) with pedal/steer, sets curGear, wengine, clutch=1; then rigid |
| 18 | 0x35105290 | 0x351138F0 | SetStateFromSnapshot(stream): rigid, then unpacks per-wheel ω (13-bit ×0.024417) and clutch (11-bit ×0.000488) from a packed int, issues action_drive, sets +11216 |
| 29 | 0x351048A0 | 0x351130E0 | GetMemoryStatistics: sizeof 11256 (0x2BF8) |
| 37 | 0x35104790 | 0x35112FC0 | GetMaxTimeStep: sets rigid's maxTimeStep (+656) = (+11216 ? +11176 : +11172) ×(0.75 if entity flag 0x10000) then rigid |
| 40 | 0x35105E50 | 0x3511FFC0 | RegisterContacts: registers each wheel's inline contact (wheel+336) then rigid |
| 41 | 0x351050A0 | 0x35113650 (unnamed) | Update: tracks hull-contact timer +11208 (reset if +11212 hull contacts), +11216 = timer<0.5; picks minEnergy +11164/+11168; then rigid |
| 43 | 0x35104FA0 | 0x35113550 | GetDamping(dt): if no hull contact → `max(0, 1 - dt·+11180)` else rigid |
| 48 | 0x35104840 | 0x35113080 | RemoveCollider: clears wheel contacts referencing the entity |
| 50 | 0x351047F0 | 0x35113030 | HasCollider-like: true if any wheel contact entity (+1520) matches |
| 51 | 0x35104FF0 | 0x351135C0 | HasPartContactsWith-like (wheel contact ent +1520 / part +1528) |
| 54 | 0x35104700 | 0x35112F30 | rigid then clear all wheel contacts (flag 0x40, ptr +1520) |
| 59 | 0x35104760 | 0x35112F90 | OnContact: counts hull contacts in +11212 |
| 73 | 0x35106330 | 0x351165F0 | DrawHelperInformation: wheels + "%.3fG/%.3fG" from +11252/+11248 ×(1/9.8) |
| 77 | 0x35076030 (generic) | – | rigid has 0x35020A30 here; wheeled uses the generic stub |
| 78 | 0x3510C940 | 0x3511AFB0 CWheeledVehicleEntity__CheckAdditionalGeometry | wheel/suspension contact detection (§5.2) |
| 79 | 0x351074A0 | 0x351142F0 | drivetrain + suspension + tyre impulses (§5.3) |
| 83 | 0x35104F10 | 0x351134C0 | new virtual: ComputeBBox expanded by 2·world[+240] on all sides |

Wheeled does **not** override Step (slot 34 = CRigidEntity 0x350C9DF0); it hooks the rigid step through slots 78/79 (rigid stubs 0x35037700 there).

---

### 2. CLivingEntity params / status / action

Unhandled ids fall to CPhysicalEntity (SetParams calls base first with the same struct; if base returns non-zero and id==0 (pos), living additionally drops ground collider and zeroes vel when the position jumped > (size.z·0.01)², and renormalizes q).

#### 2.1 SetParams / GetParams id 1 — "player_dimensions" (string "CLivingEntity:SetParams(player_dimensions)"), 64 B

Validation `sub_35038380` (x86 0x3502AF90): heightPivot, heightEye finite; sizeCollider finite and |size|² ≤ 10000. Validation is skipped if world+312==0.

| off | type | meaning | entity field | notes |
|---|---|---|---|---|
| 0 | int | type = 1 | | |
| 4 | float | "heightPivot" | +540 | unused → filled from entity |
| 8 | float | "heightEye" | +536 | if set also zeroes +916 |
| 12 | vec3 | "sizeCollider" (x=radius, z=half-height) | +544 (x), +548 (=x), +552 (z) | unused tested on +12 only; stored z = max(z,0.001) |
| 24 | float | heightCollider (inferred) | +532 | part offset z = +532 − +540 |
| 28 | float | head sphere radius (inferred) | +732 (CSphereGeom at +672) | GetParams returns +732 |
| 32 | float | heightHead (inferred) | +664 | |
| 36 | vec3 | unprojection direction (inferred) | – | used when size changes |
| 48 | float | max unprojection distance (inferred) | – | |
| 52 | int | bUseCapsule (inferred) | flag 0x8 of +952 | 1 → CCapsuleGeom, 0 → CCylinderGeom (0xB0-byte geom at +656, also part geom +824) |
| 56 | int | apply-anyway-on-failure (inferred) | – | if unproject fails and ≠0: clears +60 and re-calls SetParams |
| 60 | int | require collision check (inferred) | – | checked on the deferred path |

Change-of-size path: if size/height/capsule changed and entity active (0x40): up to 8 iterations of `sub_35027E10` (unprojection test, x86 0x3502E370) moving along dir by `dist·1.01` until total ≥ max; fail → return 0. Then rebuild collider geometry and part offsets.

#### 2.2 SetParams / GetParams id 4 — player dynamics (inferred; no string), 104 B

| off | type | meaning (inferred) | entity | default (ctor) |
|---|---|---|---|---|
| 4 | float | inertia (ground accel coefficient) | +516 | 8.0 |
| 8 | float | inertia when accelerating | +520 | 0 |
| 12 | float | air control | +524 | 0.1 |
| 16 | float | air resistance | +528 | 0 |
| 20 | vec3 | gravity | +472..+480 | world vars a2[71..73] or (0,0,−9.81) |
| 28 | float | (alone) if +20 unused but +28 set → gravity = (0,0,−value) | | |
| 32 | float | landing-nod speed | +576 | 60 |
| 36 | int | bSwimming | flag 0x4 | 0 |
| 40 | float | mass | +580, +584 = 1/mass (0 if mass≤0) | 80 / 0.025 |
| 44 | int | surface index | part0 flags(+96) low 10 bits, mirrored +588 | 0 |
| 48 | float | min slide angle (deg) | +628, cos→+612 | 36° |
| 52 | float | max climb angle | +632, cos→+616 | 54° |
| 56 | float | max jump angle | +636, cos→+620 | 54° |
| 60 | float | min fall angle | +640, cos→+624 | 70.2° |
| 64 | float | max ground velocity | +644 | 10 |
| 68 | float | impulse recover time | +648 | 5 |
| 72 | int | collision types for queries | +940 | 271 (0x10F) |
| 80 | ptr | entity to ignore | +944 (−1 = unused) | 0 |
| 92 | int | bActive | flag 0x40 (turning on sets flying bit 0x1) | 1 |

#### 2.3 GetStatus

| id | handled | layout written (float idx ×4 = byte off) |
|---|---|---|
| 1 (pos) | wraps base | temporarily adds net position-error smoothing `+1028·(+1080·3.333)` to pos while base fills; sets status flag bit 2 |
| 2 | status living (inferred) | +4 bFlying = flag1 & bActive; +8 timeFlying (+604); +12 eye offset = R(q)·(0,0,−+556); +24 vel (+448) (+ ground vel +1004 if ground collider); +36 raw vel (+448); +48 velRequested (+460); +60 ground vel (+1004); +72 actual displacement vel (+496..+504); +84 +508 (int, ground material id, inferred); +88 ground height (+572); +92 ground normal (+484); +104 +592, +108 +596 (ground surface idxs); +112 ptr ground collider (+960); +120 +968, +124 +972 (ground part idx/id); +128 +976..+984 (pos in ground frame); +140 +916; +144 bStuck = flag 0x100; +152 ptr to step lock (+1116); +164 +956 (collision counter) |
| 8 | dynamics (inferred) | +12 v; +24 ω derived from delta-quat +1064 / +1080; +36 a = +1052/+1080; +60 c.o.m = bbox centre; +112 mass |
| 9 | (inferred) query collision-event flag 0x200: returns old bit and toggles it |
| 11 | returns 0 |
| 21 | check stance (inferred) | in: +4 pos, +16 q (unused test on +28), +32 size, +44 heightCollider, +48 dir, +64 bCapsule (int sentinel); out +60 = unprojection from `sub_35027E10`; returns 1 if 0 |
| else | base |

#### 2.4 Action

| id | name | layout / effect |
|---|---|---|
| 1 | "action_move" ("CLivingEntity:Action(action_move)"), 24 B | Ignored if flag 0x10. Validation `sub_35038670`: dir finite, |dir|² ≤ 1e6; +20 in [0,2]. +4 vec3 dir (unused test); +16 int iJump (inferred): 0 → velRequested=dir; 3 → velRequested=dir and byte +1120=1 (use dir as raw vel next step); 1 → vel=dir, flag 0x2 (jump), timeFlying=0; 2 → vel+=dir, flag 0x2 only if dir.z>1. +20 float → +512 (cap for next StartStep dt). If entity flag 0x4 (+92): quantize velRequested (yaw int16 ×2π/65535, pitch int8 ×π/127, magnitude uint8 ×20/255, `sub_35037A90`) and vel (int16 ×100/32767, clamp ±100, `sub_350379E0`). |
| 2 | "action_impulse" ("CLivingEntity:Action(action_impulse)"), 80 B | J = +4 impulse; ×0.3 if int +76 == 2; += (+28 vec3)·mass if set; if int +72 ≠0 re-issue as action_move{dir=J/m, iJump=2}; else vel += J/m (quantized if flag 0x4) and if J.z/m>1 set flying, timeFlying=0. If inertia(+516)==0: +608 = recover time (+648). |
| 4 | reset (inferred), 8 B | vel=velRequested=0, flying, +556/+560=0, +568=1 |
| 10 | set velocity (inferred), 40 B | +12 vec3 → vel and velRequested, flag 0x2 |
| else | base Action |

---

### 3. CLivingEntity member map (size 0x468)

| off | type | meaning (inferred unless string) | default |
|---|---|---|---|
| 280 / 288 | ptr/int | parts (1 part; part geom = +824 wrapper, part.pos.z = height−pivot, mass +580, flags 0x41FDFF, collflags 2, minContactDist = size.z·0.1) | |
| 408 / 420 | vec3/quat | pos / orientation (base) | |
| 448 | vec3 | "vel" | 0 |
| 460 | vec3 | "velRequested" | 0 |
| 472 | vec3 | gravity | world / (0,0,−9.81) |
| 484 | vec3 | ground normal | (0,0,1) |
| 496 | vec3 | displacement velocity (Δpos/dt of last step) | 0 |
| 508 | int | ground material id, reset each step | 0 |
| 512 | float | dt cap from action_move | 0 |
| 516..528 | float×4 | inertia, inertiaAccel, airControl, airResistance | 8,0,0.1,0 |
| 532 | float | heightCollider | 1.2 |
| 536 | float | "heightEye" | 1.7 |
| 540 | float | "heightPivot" | 0 |
| 544/548/552 | float | "sizeCollider" r, r, half-height | 0.4,0.4,0.2 |
| 556 | float | eye/step smoothing offset | 0 |
| 560 / 564 / 568 | float | eye offset velocity / accel / timer | 0,0,1 |
| 572 | float | ground height under pivot | −1e−10 |
| 576 | float | landing-nod speed | 60 |
| 580 / 584 | float | mass / invMass | 80 / 0.025 |
| 588 | int | surface idx | 0 |
| 592/596/600 | int | ground surface idx(s) | −1 |
| 604 | float | timeFlying | 0 |
| 608 | float | impulse-recover countdown | 0 |
| 612..624 | float | cos(minSlide, maxClimb, maxJump, minFall) | |
| 628..640 | float | same in degrees | 36,54,54,70.2 |
| 644 | float | max ground velocity | 10 |
| 648 | float | impulse recover time | 5 |
| 656 | ptr | collider geometry (CCapsuleGeom / CCylinderGeom, 0xB0) | |
| 664 | float | heightHead | 1.9 |
| 672..823 | CSphereGeom inline (+736 CSingleBoxTree); radius at +732 | head sphere | |
| 824.. | phys-geometry wrapper for collider | | |
| 912 | float | lock timer (−=dt each step, set 1.0 after unproject) | −1 |
| 916 | float | time since eye-height change (+=dt) | 0 |
| 920 | float | time since pushing another entity (+=dt) | 10 |
| 924 / 928 | float | step dt requested / performed | |
| 932 / 936 | int | world-frame change counter / last frame id | 0 / −1 |
| 940 | int | collision type mask | 271 |
| 944 | ptr | entity to ignore | 0 |
| 952 | uint | flags: 0x1 flying, 0x2 jump requested, 0x4 swimming, 0x8 capsule, 0x10 ignore move, 0x20 no-lock/replay mode, 0x40 active, 0x80 awake, 0x100 stuck, 0x200 collision-event pending | 0x49 |
| 956 | int | recent collision counter (0..5) | 0 |
| 960 | ptr | ground collider entity (ref-counted) | 0 |
| 968 / 972 | int | ground part index / part id | |
| 976 / 988 | vec3 | pos in ground-part frame (current / anchor); 1000 = z offset | |
| 1004 | vec3 | ground velocity at feet (clamped to max ground vel) | |
| 1016 | vec3 | ground angular velocity | |
| 1028 | vec3 | net position error for smoothing | |
| 1040 | vec3 | net target pos (when world+1112) | |
| 1052 | vec3 | Δvel last step | |
| 1064 | quat | Δorientation last step | (0,0,0,1) |
| 1080 | float | last dt | 0.16 |
| 1088 / 1096 / 1100 | ptr/int/int | contact array (stride 80) / count / capacity | |
| 1104 | ptr | rigid-body proxy (slot 64) | 0 |
| 1112 / 1116 | lock | params lock / step lock | |
| 1120 | byte | use velRequested as raw vel next step | 0 |

#### 3.1 x64 helper functions (living)

| x64 | x86 | role (inferred) |
|---|---|---|
| 0x35025530 | 0x3502BCC0 | ctor (base ctor `sub_35071D30`) |
| 0x35027E10 | 0x3502E370 | unprojection test: collider at (pos,q,h,pivot,size,capsule) along dir → distance (0 = free) |
| 0x3502B3F0 | – | follow moving ground: pos from ground part frame and local anchor, ground vel/ω, reaction impulse to ground, clamp to max ground vel |
| 0x3502CA20 | – | ground probe: downward CRayGeom from capsule bottom, length 1.5·(h−size.z)+2 over the cached nearby-part list; returns ground z (init −1e10), normal (tilted by ≤15°), surface idx; with commit flag sets +960/+968/+972/+976..+1024 |
| 0x35027BB0 | – | airborne vel integration: `+= groundVel` (if not flying & no collider), g (+ swim drag toward velRequested), air resistance `−v·k·dt` (zero if overshoot), kill <1e−10 |
| 0x35025DF0 | – | push ground body with a reaction impulse (action_impulse id 2), if ground is light rigid |
| 0x35026300 | 0x35034F20 | post collision event (needs entity flag 0x2002000 and not 0x20); sets flag 0x200 |
| 0x35026A30 | – | append living contact (80 B) + AddCollider on both |
| 0x35026EB0 / 0x350253C0 | – | compute new BBox for pos / commit pos+bbox+part transform |
| 0x3502C700 / 0x35024CF0 | – / 0x350291D0 | set / release ground collider |
| 0x35038380 / 0x35038670 | 0x3502AF90 / 0x3502B250 | validators for player_dimensions / action_move |
| 0x35037A90 / 0x350379E0 | 0x35029D90 / 0x35029C50 | velRequested polar packing / vel int16 packing |
| 0x35084680 | 0x350942C0 CPhysicalWorld__GetEntitiesAround | world box query |

---

### 4. CLivingEntity::Step (slot 34, 0x3502DED0, ~38 KB; x86 0x35035630)

Signature `int Step(dt)`. Outline:

1. dt ≤ 0 → return 1. `dt = clamp(+924−+928, ≥0.001)` (if world+1744==2, `min(remaining, dt)`). Take step lock (+1116) unless flag 0x20. Local cylinder geom on stack used for sweeps.
2. Inertia selection: `k = 6` if impulse-recover timer (+608) > 1e−4; else `inertiaAccel` (+520) if non-zero and |velRequested|²>0.1; else inertia (+516).
3. Moving platform: pos0 = `sub_3502B3F0` (unless world+1744==2). Timers: +912 −= dt, +916/+920/+928 += dt, +608 −= dt (≥0).
4. Early-out (sleep): if inactive (no 0x40) or (vel=0, velRequested=0, (not flying or g=0), pivot above ground, no eye smoothing, not awake bit, ground normal ≥ cos(minSlide), no ground collider): if inactive and velRequested≠0, just translates pos/bbox by velRequested·dt (no collision). Go to finish.
5. Desired velocity `v`:
   * on ground & no jump: `v = velRequested − n·(velRequested·n)` (tangent to ground normal +484);
   * flying with airControl>0: if inertia>0, move v toward velRequested per axis by `inertia·dt·airControl` (clamped, no overshoot; full if airControl≥1); if inertia==0 add the component of velRequested along/against gravity;
   * byte +1120 → v = velRequested; clamp |v| to world+368 (max velocity); NaN → abort.
6. Neighbour gathering: box = swept capsule (expanded by `(|v·dt|+world[+244]+size.x)·2.5` and ≥ 10·dt), `GetEntitiesAround(box, collTypes|0x20050, ignore self)`. Rigid entities light enough (mass·0.4 < 1/invMass) may be pre-checked by sweeping the collider against their parts (vtable+80 intersect); living/sleeping entities are woken (vtable+440). Results cached in world per-thread arrays (88-byte entity records, 96-byte part records at world+21904/+21912).
7. **Collision sweep loop** (while move length > ε and part 0 collides): for each cached part, swept intersection of collider geom (+656) with part geom (intersection params: time-interval = 2·dt, sweep dir = v̂, max = |Δ|); living-vs-living handled analytically as vertical cylinders. Take earliest hit; contacts whose normal·up ≥ cos(maxClimb) treated as floor; ≥0.95 alignment used to detect creases. On hit: move to contact, post event (`sub_35026300`), store contact (`sub_35026A30`), apply action_impulse to the hit entity (scaled 0.2 for non-rigid sim classes; +920=0), set impulse-recover timer if inertia==0. Remaining motion is projected onto the contact plane (two-plane crease handled; scale 0.9998 + 0.02 nudging). Stop when remaining ≤ world[+244]·0.1.
8. **Step up / snap down** (`sub_3502CA20` with commit=0): if target is below ground by < (heightCollider − size.z)·1.01 → lift to ground; if slightly above ground (< |v|·dt) and not flying/jumping/swimming → snap down. If ground normal < cos(minSlide) at both points and velRequested=0 → cancel horizontal move. Jump flag → reaction impulse into ground (`sub_35025DF0`).
9. Final overlap/unproject against parts (box = size.z·10); if still penetrating sets bStuck (0x100), zeroes v, +912 = 1.
10. Ground commit (`sub_3502CA20` commit=1): +572 ground height; ground normal stored if n·up ≥ 0.087; ground collision event; drops static ground collider; timeFlying += dt if flying.
11. **Flying decision**: flying if g·up > 0, swimming, stuck, or pivot is above ground by > max(0.004, size.z·0.01) and normal steep (< cos(minFall)).
12. Velocity update:
    * flying → `sub_35027BB0` (gravity, air resistance, swim drag);
    * landing (was flying, v·up < −4, nod>0): +560 = v·up, +564 = max(v²/(2·size.z), nod speed);
    * ground: remove normal component of v; `Δ = (velReqTangent − v)·min(k, 1/dt)·dt` (overshoot-limited); if n·up < cos(minSlide) add tangential gravity (slide); if n·up < cos(maxClimb) remove uphill component; if n·up < cos(minFall) → flying; tiny |v| zeroed (<1e−2 with no request, else <1e−4).
    * entity flag 0x4 → quantize v (int16 ×100/32767).
13. Eye/step smoothing (+556..+568): step-ups > size.z·0.01 add to +556 (clamped to size.z) and decay with +560/+564; head sphere (+732>0) sweep upward limits +556 to stay under ceilings (heightHead +664).
14. Commit: new bbox (`sub_35026EB0`), world grid update (`sub_35080A00`), pos (`sub_350253C0`).
15. Finish: +1052 = Δvel; vel(+448) += (v − v0); +496 = Δpos/dt; flying bit; +1064 = q0⁻¹·q; +1080 = dt; net error +1028 = +1040 − pos (zeroed if |e|² <1e−4 or >4) when world+1112; refresh rigid proxy +1104 (or delete when no colliders); if moved notify world+20848 object (`sub_351008D0`) and post a PostStep event (`sub_35037FE0`) when pos/q changed more than world+624 (entity flags 0x8080000). Release step lock.

#### 4.4 SyncFromNet (slot 24, 0x3502AA60; x86 0x35031560)

Struct id 4 (net state, inferred): +4 pos, +16 vel, +28 velRequested, byte +68 relative-to-ground-collider, byte +69 drop ground collider, byte +70 snap (no smoothing), int +76 ground part idx, int +80 ground part id.
Correction limit `c = clamp(min(|vel|·0.1, size.x·0.5), ≥ size.x·0.1)`; if |target−cur| ≤ c: `cur += (target−cur)·(d/c)·0.033`, else `cur = target + dir(cur−target)·c`. Relative mode works in ground-part frame (+976..+1000) and converts to world. Applies pos via SetParams(pos, flags 0x31), then sets vel.xy (needs both set), vel.z, velRequested.

---

### 5. CWheeledVehicleEntity

#### 5.1 Params / status / action

Unhandled ids → CRigidEntity (called first; wheeled handles id 2/16 only when rigid returned 0; after rigid handles id 10 it copies +8→+11176, +12→+11164; after id 6 (part) it re-syncs the wheel's pivot/orientation/flags from the part).

**Params id 2 — car params (inferred), 144 B.** RPM fields are converted by ×π/30 (0.10472) on Set and ×30/π on Get.

| off | type | meaning (inferred) | entity | default |
|---|---|---|---|---|
| 4 | float | axle friction | +11032 | 0 |
| 8 | float | engine power / max torque | +10992 | 10000 |
| 12 | float | (unknown) | +10996 | 0 |
| 16 | float | max steer angle (rad) | +11000 | π/4 |
| 20 | rpm | engine max RPM (Get rounds) | +11004 | 600 rad/s |
| 24 | float | brake torque | +11036 | 4000 |
| 28 | int | integration type | +11160 | 1 |
| 32 | float | max time step | +11172 | 0.02 |
| 36 | float | min energy | +11168 | 0.0025 |
| 40 | float | damping | +11180 | 0.01 |
| 44 | float | min braking friction | +11044 | 0 |
| 48 | float | max braking friction | +11048 | 1 |
| 52 | float | stabilizer (anti-roll) | +11060 | 0 |
| 56 | int | Get only: nWheels = nParts − +11156 | | |
| 60 | rpm | engine min RPM (stall) | +11008 | 0.05·max |
| 64 | rpm | shift-up RPM | +11016 | 0.5·max |
| 68 | rpm | shift-down RPM | +11020 | 0.2·max |
| 72 | rpm | idle RPM (also resets current engine ω +11076) | +11012 | 0.1·max |
| 76 | rpm | start RPM | +11028 | 40 rad/s |
| 80 | float | clutch speed | +11040 | 1 |
| 84 | int | nGears | +11128 | 3 |
| 88 | float* | gear ratios (−1 = unused) | +11080[nGears] (index 0 reverse, 1 neutral, ≥2 forward) | {−1,1,1} |
| 96 / 100 | int | max gear / min gear | +11136 / +11140 | 2 / 0 |
| 104 | float | slip threshold | +11056 | 0.2 |
| 108 | rpm | gear-direction-switch RPM | +11024 | 1 rad/s |
| 112 | float | dynamic friction ratio | +11052 | 1 |
| 116 | float | tank-steer turn width (stored as 2/x; 0 = car steering) | +11144 | 0 |
| 120 | float | (unknown) | +11224 | 0 |
| 124 | float | (unknown) | +11232 | 0.866 |
| 128 | int | (unknown) | +11236 | 0 |
| 132 | float | engine-braking scale | +11240 | 1 |
| 136 | int | rear-steer flag (1 = reverse steering side) | +11244 | 0 |

**Params id 16 — wheel params (inferred), 80 B.** +4 int wheel index (clamped). Wheel record = +1200 + 544·i.

| off | type | meaning (inferred) | wheel field (abs off / wheel-rel) |
|---|---|---|---|
| 8 | int | bDriving | flags +1380 bit 0x1 |
| 12 | int | axle index (<0 = not on an axle) | +1260 / +60 |
| 16 | int | bCanBrake | bit 0x2 |
| 20 | int | bBlocked | bit 0x4 |
| 24 | float | suspension length max (also clamps +1296, +1400) | +1276 / +76 |
| 28 | float | suspension rest length | +1296 / +96 |
| 32 | float | (unknown, set only) | +1280 / +80 |
| 36 / 40 | float | min / max friction | +1348 / +1352 |
| 44 / 48 | float | lateral friction terms | +1364 / +1368 |
| 52 | int | surface idx | part flags (+96) low 10 bits |
| 56 | int | bRayCast | bit 0x8 |
| 60 | float | spring stiffness | +1284 / +84 |
| 64 | float | damping; ≤0 → `−sqrt(4·k·m_share)·x` (fraction of critical) | +1288 / +88 |
| 68 | float | (unknown) | +1356 |
| 72 | float | drive-torque scale | +1256 / +56 |
| 76 | float | wheel ω (rad/s) | +1412 / +212 |

**Status:** id 1 (pos of a wheel part, includes suspension compression), **id 4 vehicle status (inferred)**: +4 steer (+11068), +8 pedal (+11064), +12 handbrake (+11148), +16 footbrake (+11152), +20 drive torque/clutch value, +24 rigid vel (+808..+816), +36 number of wheels in contact, +40 curGear (+11132), +44 engine RPM (+11076 ×30/π), +48 clutch (+11072), +52 drive torque (+11228); **id 5 wheel status (inferred)**: +4 wheel by part id, +12 bContact (bit 0x40), +16..+40 wheel pos/q (wheel+0..+24), +44 contact pt (+1460), +56 normal (+1448), +68 ω, +72 bSlip (bit 0x10), +76 slip vel (+1480), +88 contact surface (+1476), +92 +1660, +96 +1432, +100 current susp len (+1400), +104 len max, +108 rest len, +112 radius (+1388), +116 brake torque (+1420), +120 steer (+1404), +124 rotation angle (+1408), +128 contact entity (+1520), +136 mass share (+1300); **id 15 (inferred abilities)**: from steer input → turning centre (+8) and max speed estimate (+20). Others → rigid.

**Action:** id 3 **drive (inferred; strings "pedal","steer","handbrake","curGear")**, 36 B: +4 pedal (abs), +8 dpedal (added, clamped ±1), +12 steer (abs), +16 dsteer (added), steer clamped to ±maxSteer; +20 clutch (+11072); +24 int handbrake (+11148); +28 int footbrake (+11152); +32 int gear (+11132). Ackermann: steered wheels on the turn side get the full angle; the other wheels on the same axle get `atan(d·tanθ /(|x|·tanθ + d))`. Wheel steer stored at wheel+204, wheel part q rebuilt (rotation about −z). Non-zero pedal (or releasing handbrake) wakes the vehicle (+508=1, +608≥1, sim class 2). id 4 reset (after rigid): clears wheel ω/+1416, pedal=0, +11208=10. id 5 → rigid only.

#### 5.2 Wheel record (544 bytes at +1200, max 18; first wheel part index = +11156)

Built in AddGeometry (slot 11) from car geomparams (type 1, 184 B; otherwise converted by `sub_35110840` and forwarded to rigid). If geom +112 (bDriving) is the unused int → hull part (inserted before the wheels, +11156++). Mass = +8 or density(+4)·volume; mass ≤ 0 → logs "CWheeledVehicleEntity::AddGeometry: trying to add a weightless wheel", returns −1. Wheels are added as zero-mass parts (flag 0x40000000).

| wheel-rel | abs | meaning (inferred) |
|---|---|---|
| 0 / 12 | 1200 / 1212 | wheel pos / q (copied from part) |
| 28..52 | 1228.. | part bbox copy |
| 56 | 1256 | drive-torque scale (1) |
| 60 | 1260 | axle index (geom +116) |
| 64 | 1264 | suspension pivot (geom +128) |
| 76 | 1276 | len max (geom +140) |
| 80 | 1280 | (geom +148) |
| 84 / 88 | 1284 / 1288 | stiffness / damping |
| 92 | 1292 | (geom +156) |
| 96 | 1296 | rest length (geom +144) |
| 100 | 1300 | mass share (from body inertia) |
| 104 / 120 / 132 | 1304 / 1320 / 1332 | initial part q / pos / centre |
| 144 | 1344 | 1/(mass·r) (inferred) |
| 148..168 | 1348..1368 | friction terms (geom +160..+176; defaults 0,1,1,1,0); 1360 = geom +180 (default 0.1745 rad, ≥0.001) |
| 172 / 176 / 180 | 1372 / 1376 / 1380 | part flags / collision flags / wheel flags (0x1 driving, 0x2 can brake, 0x4 blocked, 0x8 raycast, 0x10 slipping, 0x20 temp, 0x40 in contact) |
| 188 / 192 / 196 | 1388 / 1392 / 1396 | radius / 1/radius / width (from geometry bbox) |
| 200 | 1400 | current suspension length |
| 204 / 208 / 212 | 1404 / 1408 / 1412 | steer / rotation angle / ω |
| 216 | 1416 | (qword, cleared on reset) |
| 220 / 224 / 228 | 1420 / 1424 / 1428 | brake torque / brake impulse / ω copy |
| 232 | 1432 | |
| 248 / 260 | 1448 / 1460 | contact normal / contact point |
| 272 / 276 | 1472 / 1476 | own / other surface idx |
| 280 | 1480 | slip velocity |
| 312 / 320 / 328 | 1512 / 1520 / 1528 | contact body / contact entity / contact part |
| 336 | 1536 | inline solver contact |
| 460 | 1660 | |
| 472 | 1672 | friction direction |

Other members: +10992..+11060 car params (above); +11064 pedal; +11068 steer; +11072 clutch; +11076 "wengine" (engine ω); +11080 gear ratios; +11128 nGears; +11132 "curGear"; +11148 "handbrake"; +11152 footbrake; +11156 first-wheel part index; +11164/+11168 minEnergy (hull contact / normal); +11172/+11176 maxTimeStep (normal / hull contact); +11208 time since hull contact; +11212 hull contact count; +11216 hull contact flag; +11220 lock; +11228 drive torque; +11248/+11252 accelerations (G display).

#### 5.3 Step hooks

**Slot 78 0x3510C940 (x86 "CheckAdditionalGeometry" 0x3511AFB0)** — per-step wheel contact detection: query neighbours over the vehicle box (`GetEntitiesAround`), wake light entities; per wheel cast a suspension ray (CRayGeom) or swept wheel shape from the pivot downward over len max, against neighbour parts matching the wheel collision mask (+1376); keep nearest hit: set flag 0x40, store point, normal, entity/part, surfaces (heightfield/terrain ids 0x7FFFFF handled separately), AddCollider on both; compute current length +1400 and move the wheel part; compute slip velocity +1480; register contacts via vtable+576; if the hull penetrates the ground (> 1.01·tolerance) lift pos/com and vel.

**Slot 79 0x351074A0 (x86 0x351142F0)** — drivetrain and tyre/suspension impulses:
1. collider housekeeping (adds wheel contact entities as colliders, removes stale ones);
2. engine `sub_351057C0` (x86 –): steering-scaled wheel ω average × gear ratio → engine ω target; clutch engages at clutch speed when ω > min RPM (stalls → neutral, ω = idle); auto-shift: up when ω > shift-up, down when < shift-down (clutch > 0.99, ≥2 wheels in contact), clamp to [min,max] gear; torque `T = sin(π·min(ω,1.5·max)/max)·P/Σscale` (floor −0.1·), divided by ω, minus engine braking (+11240), × |pedal| × gear ratio × clutch → +11228;
3. per wheel: brake torque = axle friction ± |pedal|·brake torque when braking against motion; tank steering via +11144; handbrake/blocked zero ω; integrate rotation angle (wrap ±2π);
4. suspension: spring `k·(rest − len) − c·v_rel` (+ stabilizer term between wheels of the same axle), clamped ≥0, ×dt → normal impulses, summed into body linear/angular impulse;
5. tyre friction from surface table (world+6504 by surface idx), min/max braking friction, slip detection (flag 0x10), lateral friction, impulses applied via rigid helpers.

---

### 6. Main uncertainties

* Field names are behavioural guesses; living type ids 2 (status), 4 (params), 8, 9, 21 and wheeled ids 2, 3, 4, 5, 15, 16 have no binary strings. Unknown car-params fields: +12, +120, +124, +128.
* Living flags 0x10 and 0x20 of +952: no writer was found in the living code (probably set by world/replay code).
* Step (§4) is an outline: the sweep loop's crease/iteration rules and the living-vs-living cylinder test were summarised, not transcribed. Slot 78/79 internals (friction model, stabilizer) are summarised; `sub_351057C0` formulas need a check before porting.
* Slot role labels for 40/41/54/59/64–69 are inferred; they need to agree with the CPhysicalEntity/CRigidEntity agents' names.
* The vtable slot 16/18 "snapshot variant" roles are uncertain; living slot 16 behaves like a ground re-attach.


---

## 5. CParticleEntity / CRopeEntity / CSoftEntity

Sources: x64 10.x `cryphysics.c` and the x86 1.2 decompile. Everything here comes from the decompiles. Any name not taken from a binary string is marked **(inf)**.
Addresses are full VAs. x64 is listed first and x86 second (taken from `compare/vtable_slots.txt` / `func_match.csv`).
Notation: `E+0xNN` is an entity member, `P+N` is an offset in the params/status/action struct (decimal, as in the decompile), `float` means 4 bytes, `double3` means 3×8 bytes.

### 0. Common conventions (confirmed for all three classes)

| Item | Value |
|---|---|
| Unused float field | stored as `0xFFBFFFFF` (-4194305). Tested with `(bits & 0xFFA00000) == 0xFFA00000` → unused (vec3: only x is tested). |
| Unused int field | `0x80000000` |
| Unused pointer field | `-1` (`0xFFFFFFFFFFFFFFFF`). Rope/soft `pEntity == -10` means the static "world/terrain" entity singleton `&unk_3520C6E0` (inf: WORLD_ENTITY). |
| Command guard | Every SetParams/Action begins with `sub_35023200` (SetParams) or `sub_35023650` (Action): (world, this, struct, bThreadSafe). If the guard defers the call (queued), the function returns 1 immediately. Exit releases `world+22468/+22492` spin counters. (inf: deferred-command / write-lock helper; shared with the base class) |
| Validation | Particle SetParams(3) and Soft Action(2) run a validator (`sub_350409C0` / `sub_35021B10`). It logs `"%s: (%.50s @ %.1f,%.1f,%.1f) Validation Error: %s is invalid"` per field (`mass`,`size`,`thickness`,`heading` – heading must have |len-1|<0.01, …). The call proceeds if the struct is valid **or** `world+312 == 0` (inf: strict-validation cvar). |
| Base fall-through | Particle and rope SetParams call `CPhysicalEntity::SetParams(this,p,1)` (`0x3506ef20`) **first**. If the base returns nonzero, the derived class only post-processes. Soft does the same. GetParams/GetStatus also call the base first, **except** Soft GetParams, which handles 10/17 itself and falls back to the base after that. Rope and Soft GetStatus also call the base first; if the base handled id **20**, they force `P+4 = 1`. Action: the derived id switch runs first, then `CPhysicalEntity::Action(this,p,1)` (`0x350583c0`). |
| Locks | `E+0x48` (72) entity RW spinlock: `+0x10000` = writer, `+1` = reader, with a wait on byte `+74`. Particle uses its own lock `E+0x2A0` (672). Soft uses `E+0x358` (856). Rope uses `E+516` / `E+512` for the vertex list. |
| Physics-data RW | `+1` reader with spin while `byte[lock+2] != 0`. `0x10000` writer via `InterlockedCompareExchange`. |
| Base members used here | pos `E+0x198` (408) float3, q `E+0x1A4` (420) xyzw, BBox min `E+8`/max `E+20`, flags `E+0x5C` (92), simclass byte `E+0x47` (71), awake bits `E+0x84` (132) bits 24..27, parts `E+0x118` (280) (stride 0x88: +72 partid, +76 mass, +80 flags, +84 flagsCollider, +56 pPhysGeom, +64 pPhysGeomProxy, +3/+7 pos/q, +28 scale), nParts `E+0x120` (288), world `E+0x78` (120), idle timer `E+252` / maxIdle `E+256`, collider list `E+208`/count `E+216`. |
| Helpers | `sub_35080A00` = world RepositionEntity (inf; error string `"is too large"`). `sub_35084680` = GetEntitiesAround (x86 `CPhysicalWorld__GetEntitiesAround`). `sub_35037FE0` = world OnEvent/post event. World vfunc `+832` = RayWorldIntersection (string tag `"RayWorldIntersection(PhysParticles)"`). World vfunc `+696` = CheckAreas (fills up to 4 `pe_params_buoyancy`-like structs, id 12). World vfunc `+208` = GetPhysicalEntityId (inf). World vfunc `+784` = id→entity for serialization (inf). `sub_35040F80` = quaternion rotating vector a→b (inf). `sub_35022240` = q*v. `sub_35023C00` = q⁻¹*v (inf). `sub_350210A0` = normalize. |

Vtable role names used below (cross-agent consensus): 1 GetType · 7 SetParams · 8 GetParams · 9 GetStatus · 10 Action · 11 AddGeometry · 12 RemoveGeometry · 15/16 GetStateSnapshot (TSerialize / alt) · 17/18 SetStateFromSnapshot · 25 DoStep(dt,iCaller) · 26 StartStep(dt) · 29 GetMemoryStatistics · 34 Step(dt) · 37 GetMaxTimeStep(dt) · 54 (release refs before delete) · 55 Awake(int) · 56 IsAwake(ipart) · 57 RayTrace(SRayTraceRes&) · 64 GetRigidBody(ipart,bWillModify) (inf) · 65 GetRigidBodyData (inf) · 66 GetMass(ipart) · 69 GetMassInv (inf; base returns 0, rope returns 1e26) · 71 GetPartTransform(ipart,pos(double3),q,scale) · 73 DrawHelperInformation · 75 ComputeBBox.

Note: slot **1** returns the type id (5/7/8). Slot 6 is not overridden by any class.

---

### 1. Overridden vtable slots

Base (CPhysicalEntity) column = `Static` column of `vtable_table.md`.

#### CParticleEntity (type 5, size 0x2A8, ctor `0x35039d40` / x86 `0x3503f880`, vtable `0x351eee88` / x86 `0x351fb220`)

| slot | x64 | x86 | role (inf unless string) |
|---|---|---|---|
| 0 | 0x35040840 | 0x3503fba0 | scalar-deleting dtor → `0x350397c0` (unregisters from world list if `E+288>0`, releases collider-to-ignore `E+608` unless the world is shutting down (`world+4452`), then base dtor `0x35071f80`) |
| 1 | 0x35038a00 | 0x3503f5e0 | GetType → **5** |
| 7 | 0x3503b800 | 0x35041f80 `CParticleEntity__SetParams` | SetParams |
| 8 | 0x350398d0 | 0x3503fbd0 | GetParams |
| 9 | 0x3503a310 | 0x350408d0 | GetStatus |
| 10 | 0x3503a750 | 0x35041af0 | Action |
| 15 | 0x3503a000 | 0x35040630 | GetStateSnapshot (TSerialize). Saving: `"ifawake"` group → `"vel"` (E+552), then `"pos"`. Text form `"ParticleEntity"` + base + `"vel"`. |
| 16 | 0x35039ad0 | 0x3503fdc0 | alt snapshot: only touches IsAwake when `world+328` is set; returns 1 |
| 17 | 0x3503a140 | 0x35040720 | SetStateFromSnapshot (same keys; heading = normalize(vel)) |
| 18 | 0x35039b50 | 0x3503fe80 | returns 0 |
| **25** | **0x3503c210** | 0x35042c60 | **DoStep(float dt, int iCaller)**. The whole particle simulation (~17 KB). See §4.1. |
| 26 | 0x35039850 | 0x3503f630 | StartStep(dt): `E+656=dt` (full step), `E+652=0` (performed) |
| 29 | 0x35039870 | 0x3503f650 | GetMemoryStatistics (adds sizeof 680) |
| 34 | 0x350407e0 | 0x3503f830 | Step(dt) → `DoStep(dt, iCaller)`. iCaller = thread index read from TLS. |
| 37 | 0x35039ce0 | 0x35040030 | GetMaxTimeStep(dt) = `min(dt, full-performed)` if performed < full-0.001, else dt |
| 54 | 0x350406d0 | 0x3503f600 | release `pColliderToIgnore` (E+608) |
| 55 | 0x35039b60 | 0x3503fe90 | Awake(b): b≠0 → state bits=1 (`|=0x100000`, `&=~0x200000`). b=0 → state=0, vel=0. Returns simclass. |
| 56 | 0x35039bb0 | 0x3503fef0 | IsAwake. Asleep if pos.z ≤ -1000, or state==0, or (state≠1 && \|v\|² ≤ minVel² && (bounce-cnt≠0 \|\| E+668&0x4000000 \|\| g==0)) |
| 57 | 0x3503ab10 | 0x35040c50 | RayTrace: ray vs. oriented box (half extents r, r, thickness/2; axes from q and `normal`). Skips the check if this particle and the currently-stepped entity (TLS slot) both have flag 0x100. Result: dist, pt, n, surface idx `E+600`. |
| 66 | 0x350406c0 | 0x3503f5f0 | GetMass → E+448 |
| 73 | 0x3503af40 | 0x350410b0 | DrawHelperInformation: draws 12 box edges (flag&2), plus the contact normal line `E+620` if `E+668&0x4000000` (flag&1) |
| 75 | 0x35040f10 | (base in x86) | ComputeBBox: pos ± size/2 (E+452) |

#### CRopeEntity (type 7, size 0x400, ctor `0x350ce210` / x86 `0x350dbca0`, vtable `0x351f1a48`)

| slot | x64 | x86 | role |
|---|---|---|---|
| 0 | 0x350eaa10 | 0x350db7d0 | dtor → `0x350cdc90` |
| 1 | 0x350ea790 | 0x350db590 | GetType → **7** |
| 7 | 0x350d8a80 | 0x350e5590 | SetParams (ids 10, 13; post-hook on base id 0) |
| 8 | 0x350cea00 | 0x350dc4c0 | GetParams (10, 13) |
| 9 | 0x350cee50 | 0x350dc940 | GetStatus (14; base 20 → P+4=1) |
| 10 | 0x350dbb50 | 0x350e87f0 `CRopeEntity__Action` | Action (14 handled pre-guard; 2, 12, 4) |
| 15 | 0x350d6bf0 | 0x350e3960 | GetStateSnapshot. Keys: `"nsegs"`, `"strained"`, `"awake"`, group `"Segs"` (pos, vel if awake), `"nvtx"`, group `"Verts"`, `"pt_tied0"`, `"pt_tied1"`, `"pos_start"`, `"pos_end"`, `"saveties"`, group `"link"`×2 {`"tied"`, entity id, `"ipart"`} |
| 16 | 0x350ce1a0 | 0x350dbb70 | alt snapshot: writes nSegs (`sub_3512C800`) |
| 17 | 0x350d71f0 | 0x350e3e10 | SetStateFromSnapshot (same keys) |
| 18 | 0x350d6a70 | 0x350e3840 | returns 0 |
| 26 | 0x350ce050 | 0x350dba30 | StartStep(dt): `E+488=dt`, `E+484=0`. Copies host frames current→prev (`E+804→792`, `E+856→840`, `E+828→816`, `E+888→872`). |
| 29 | 0x350cdd80 | 0x350db610 | GetMemoryStatistics (1024 + 184·nSegs) |
| 34 | 0x350ea720 | 0x350f8420 | Step(dt): `n = int(dt / world[+596] + 1)`, then calls `Step2(dt/n)` (`0x350e5020`) n times |
| 35 | 0x350d8790 | 0x350e50d0 | GetLength (inf): `E+520` if >0, else sum of segment lengths |
| 36 | 0x350dcd90 | 0x350e51c0 | random point (+dir) on rope (inf; `CryRandom`): pos→P+0, dir→P+12 |
| 37 | 0x350ce120 | 0x350dbaf0 | GetMaxTimeStep = min(dt, full-performed, maxTimeStep `E+476`) |
| 38 | 0x350ea7f0 | 0x350db600 | returns `E+496` (last dt) (inf: GetLastTimeStep) |
| 40 | 0x350d2d30 | 0x350e13b0 | RegisterContacts(dt) (inf). Builds 176-byte contacts (`sub_3509A9C0(176)`) against dynamic contact entities and registers them with the world LCP solver (`sub_350994B0`). Accumulates `E+1008`. |
| 41 | 0x350d4d60 | 0x350f7080 | Update after solver (inf). Reads back contact impulses, tie strain/break → posts 128-byte events, untie via SetParams. |
| 42 | 0x350ea7d0 | 0x350db5d0 | dt>0 ? `E+1008` : 0 (inf: CalcEnergy) |
| 43 | 0x350ea9d0 | 0x350db7a0 | `max(0, 1 - dt*damping(E+472))` (inf: GetDamping) |
| 54 | 0x350ce670 | 0x350dc180 | detach everything: SetParams(rope, pEntTiedTo=0,0), releases segment contacts and the collider list |
| 55 | 0x350cde00 | 0x350db800 | Awake(n): sets `E+132` bits 24..27, `E+492=0`, repositions. Drops contacts and ties to entities being deleted. |
| 56 | 0x350fde30 (shared w/ Soft) | 0x350db5a0 | IsAwake = `(E+132>>24)&0xF` |
| 57 | 0x350d5dd0 | 0x350e3150 | RayTrace vs. segment (or subdivided vertex) capsules with radius `collDist E+604`. Surface = `E+608`. |
| 58 | 0x350d6500 | 0x350f8060 | explosion/pressure (center, kr, r) (inf). If tied at both ends and the impulse exceeds `E+696`: queue a 128-byte event and untie the nearer end. |
| 61 | 0x350d2b90 | 0x350e1210 | OnDelayedStep (name from the x86 Soft slot-61 profiler string). Restores the segment array and `E+940` from a recorded stream buffer, then calls Step. |
| 62 | 0x350d6a80 | 0x350e3850 | entity replaced/split (old,new) (inf). Re-ties the ends to the part of the new entity within `E+692` of the end point (`sub_35057E90`). |
| 65 | 0x350dd170 | 0x350e9d50 | GetRigidBodyData(seg i) (inf). Body = {pos=midpoint, q=arc((0,0,-1)→dir), w=Δv×dir/len², v=avg vel, M=mass/nSegs, Minv}. Layout: +0 pos, +12 q, +52 w, +64 v, +76 M, +80 Minv. |
| 66 | 0x350ea7a0 | 0x350db5b0 | GetMass = mass/nSegs |
| 69 | 0x350ea7c0 | 0x350db5c0 | returns **1e26** |
| 71 | 0x350dd480 | 0x350ea220 | GetPartTransform(seg i): pos = seg[i].pos (double3), q = arc((0,0,-1)→seg dir), scale 1 |
| 73 | 0x350d7f90 | 0x350e4960 | DrawHelperInformation |

#### CSoftEntity (type 8, size 0x360, ctor `0x350fc660` / x86 `0x3510b230`, vtable `0x351f1de8`)

| slot | x64 | x86 | role |
|---|---|---|---|
| 0 | 0x350fded0 | 0x3510b720 | dtor |
| 1 | 0x350fde20 | 0x3510b500 | GetType → **8** |
| 7 | 0x350fca50 | 0x3510b950 | SetParams (10, 17, **117** (x64 only), post-hook on base id 0) |
| 8 | 0x350eb3d0 | 0x350f86c0 | GetParams (10, 17, else base) |
| 9 | 0x350eb570 | 0x350f8880 | GetStatus (17, 9; base 20 → P+4=1) |
| 10 | 0x350eff10 | 0x350fd7a0 `CSoftEntity__Action` | Action (2 `"CSoftEntity:Action(action_impulse)"`, 11, 12, 4) |
| 11 | 0x350ed610 | 0x350fb060 | AddGeometry: builds vertex/edge arrays from the tri-mesh (§3.3) |
| 12 | 0x350efa50 | 0x350fd5c0 | RemoveGeometry |
| 15 | 0x350ecff0 | 0x350fa7d0 | GetStateSnapshot (only if flag 0x4): `"updated"`, `"pos"`, `"q"`, `"q0"`, per-vertex group `"pos"` |
| 16 | 0x350ebb70 | 0x350f8d90 | returns 1 |
| 17 | 0x350f2970 | 0x350fa8c0 | SetStateFromSnapshot (same keys) |
| 18 | 0x350ecfe0 | 0x350fa7c0 | returns 0 |
| 26 | 0x350eb2e0 | 0x350f8600 | StartStep: `E+580=dt`, `E+584=0` |
| 29 | 0x350eb310 | 0x350f8620 | GetMemoryStatistics (864 + 144·nVtx + 36·nEdges + 8·nEdges) |
| 34 | 0x350f8b10 | 0x351072b0 `CSoftEntity__Step` | Step (§4.3) |
| 37 | 0x350ebaf0 | 0x350f8d10 | GetMaxTimeStep = min(dt, full-performed, maxTimeStep `E+604`) |
| 54 | 0x350eb190 | 0x350f84f0 | release vertex contact entities (and attached ones if arg=0). Frees `E+792`. Destroys helper entity `E+768` via world vfunc +192. |
| 55 | 0x350eb280 | 0x350f85b0 | Awake(n): bits 24..27 of E+132. If awake and no `E+768`, sleep counter `E+608=0`. |
| 56 | 0x350fde30 | 0x3510b510 | IsAwake (shared with rope) |
| 57 | 0x350ecb20 | 0x350fa380 | RayTrace via mesh geometry intersect (`sub_3515AB20`) |
| 58 | 0x350ec240 | 0x350f9930 | explosion/pressure (center, kr, r) (inf). Per-triangle impulse scaled by `E+668`, clamp/flag `E+676`. |
| 61 | 0x350ec810 | 0x350f9e70 `CSoftEntity__OnDelayedStep` | OnDelayedStep. Reads the host frame (globals `0x3521B2D8..`) + vertex array + edge fields (+12,+16) from the stream. `E+632` = max vertex displacement². Sets performed=full, then calls Step. |
| 66 | 0x350fde40 | 0x3510b520 | GetMass = part[0].mass / nVtx |
| 73 | 0x350ed140 | 0x350fab80 | DrawHelperInformation |

---

### 2. Params / Status / Action structs

#### 2.1 Particle

**SetParams id 3 = `pe_params_particle`** (size 168). The validator string is `"CParticleEntity:SetParams(pe_params_particle)"`. GetParams id 3 is the exact inverse unless noted.

| P+ | type | field (inf) | unused test | stored → | notes |
|---|---|---|---|---|---|
| 4 | int | flags | `0x80000000` | E+92 (entity flags) | bit 0x400 → (un)register in world grid (traceable) |
| 8 | float | `mass` | 0xFFA00000 mask | E+448 | |
| 12 | float | `size` | mask | E+452 = size/2 (radius r), E+456 = 1/r, E+460 = size/2 | size==0 logs `"Invalid Particle size!!"`. Get: r·2 |
| 16 | float | `thickness` | mask | E+460 = thickness/2 | Get: ·2 |
| 20 | vec3 | `heading` (unit) | mask on x | E+540 | |
| 32 | float | velocity (scalar) | mask | E+552 = heading·v | if \|v\|>0: state=1 (awake/flying). Get: \|E+552\| |
| 36 | float | kAirResistance | mask | E+464 | |
| 40 | float | kWaterResistance | mask | E+468 | GetParams does **not** return it |
| 44 | float | accThrust | mask | E+472 | |
| 48 | float | accLift | needs P+48 **and** P+32 set | E+476 = accLift/\|vel\| (0 if vel 0) | Get: E+476·\|v\| |
| 52 | int | surface_idx | `0x80000000` | E+600 | |
| 56 | vec3 | wspin (angular vel) | mask | E+564 | |
| 68 | vec3 | gravity | mask | E+480 **and** E+492 (copy) | |
| 80 | vec3 | waterGravity | mask | E+504 | not returned by Get |
| 92 | vec3 | `normal` | mask | E+528 | |
| 104 | vec3 | rollAxis | mask | E+516 | |
| 116 | quat (x,y,z,w) | q0 | mask tested on **P+128 (w)** | E+576..588 | |
| 132 | float | minBounceVel | mask | E+592 | |
| 136 | float | minVel (sleep speed) | mask | E+596 | |
| 144 | ptr | pColliderToIgnore | `-1` | E+608 | Release old (vfunc+24), AddRef new (vfunc+16) |
| 152 | int | collision pierceability/class (4 bits) | `0x80000000` | E+668 bits 22..25 | goes into RWI flags |
| 156 | int | collTypes (entity type mask) | `0x80000000` | E+616 | |
| 160 | int | areaCheckPeriod | `0x80000000` | low **byte** of E+668 (stored as word) | Get: not returned |

After applying: if `!(flags&2)`, the orientation is rebuilt: `flags&8` → `q = q0`, else `q = q0 * basis(heading, normal×heading…)`. Then the BBox is set to pos ± r.
Post-hook: if the base handled params id **15** (flags), it re-applies a blank `pe_params_particle` (`sub_350408F0` fills every field with its sentinel) so the flag side-effects run.

**GetStatus** (base first):

| id | handling |
|---|---|
| 9 | (inf: "collided since last query") returns E+668 bit 16 and clears it |
| 8 | (inf: `pe_status_dynamics`): P+12 v (E+552), P+24 w (E+564), P+36 accel = g + heading·accThrust − v·kAir + liftDir·kLift·\|v\| (water variants used when depth E+636 < 0), P+60 centerOfMass = pos, P+112 mass |

**Action**:

| id | struct | handling |
|---|---|---|
| 2 | impulse (80 B): P+4 impulse v3, P+16 angImpulse v3 (mask), P+52 point v3 (mask) | ang = angImpulse if set, else (point−pos)×impulse. v += impulse/m (if m>0). If m·r>0 and !(flags&2): w += ang / (0.4·m·r). |
| 10 | set velocity (40 B): P+12 v (mask), P+24 w (mask) | v=P+12, heading=normalize(v); w=P+24 |
| 4 | reset (8 B) | v=0, w=0 |
| other | → base Action | |

#### 2.2 Rope

**SetParams id 10 = simulation params** (84 B; inf `pe_simulation_params`). Also handled by Soft.

| P+ | field (inf) | rope E+ | soft E+ |
|---|---|---|---|
| 4 | iSimClass (int) | – | byte E+71 (+Reposition) |
| 8 | maxTimeStep | 476 (ctor 0.05) | 604 (ctor 0.1) |
| 12 | minEnergy | 480 (ctor 0.0016) | 600 (ctor 1e-4) |
| 16 | damping | 472 (ctor 0.2) | 612 |
| 20 | gravity v3 | 448 & 460 (copy) | 588 (ctor (0,0,-9.8)) |
| 32 | dampingFreefall | – | Get only (= E+612) |
| 36 | gravityFreefall v3 | – | Get only (= E+588) |
| 52 | mass | – | rescales vertex m/minv and edge reduced-mass; part[0].mass |
| 56 | density | – | E+652; E+696 = mass/(nVtx·density) |

**SetParams id 13 = `pe_params_rope`** (248 B):

| P+ | type | field (inf) | unused | E+ | ctor default |
|---|---|---|---|---|---|
| 4 | float | length | mask | 520. If unset and pPoints given: sum of point distances | 0 |
| 8 | float | mass | mask | 600 | 1.0 |
| 12 | float | collDist | mask | 604 (+ calls `sub_350CE770`, recomputes collision extents) | 0.01 |
| 16 | int | surface_idx | 0x80000000 | 608 | |
| 20 | float | friction | mask | 612 | 0.2 |
| 24 | float | (passed to the tie constraint as a force limit) | mask | 1004 | |
| 28 | float | (friction/solver param; `/dt` in `sub_350D06A0`) | mask | 616 | world+260 or 10.0 |
| 32 | float | stiffnessAnim (target-pose spring) | mask | 544 | 70.0 |
| 36 | float | target-pose blend along rope | mask | 548 | 0 |
| 40 | float | host-motion follow factor | mask | 552 | 0 |
| 44 | float | stiffnessDecayAnim (k·(1 − i·decay/n)) | mask | 624 | 0.75 |
| 48 | float | dampingAnim | mask | 620. Added to damping when the target pose is off | 0 |
| 52 | int | bTargetPoseActive (0/1/2) | 0x80000000 | 540 | 0 |
| 56 | vec3 | wind | mask | 628, also 640 & 652 | 0 |
| 68 | float | windVariance | mask | 668 | 0 |
| 72 | float | airResistance | mask | 664 | 0 |
| 76 | float | waterResistance | mask | 676 | 5.0 |
| 80 | float | density | mask | 680 = **1/density** | 1/0.002 |
| 84,88 | float | (length-solver tolerances; used in `sub_350DA760`) | mask | 684, 688 | 0 |
| 92 | float | max re-tie distance (slot 62) | mask | 692 | 0.05 |
| 96 | float | tie break strength (also constraint limit, slot 58) | mask | 696 | |
| 100 | float | strain / max-force threshold | mask | 704 | 2·world+260 or 5.0 |
| 104..116 | float×4 | (contact / collision coefficients; +112,+116 used in `sub_350E3680`) | mask | 712,716,720,724 | 0,0,0.5,0.5 |
| 120 | int | solver iterations | 0x80000000 | 708 | 650 |
| 124 | int | nSegments | 0x80000000 | 524. Reallocates the segment array | |
| 128 | int | part-flags collision mask | 0x80000000 | 700 | 3 |
| 132 | int | subdivision min vertex gap | 0x80000000 | 984 | 3 |
| 136 | 2×vec3 | a line/segment (both ends) | mask on P+136. Zero-length → stored unused | 916..936 | 916 = unused |
| 160 | ptr | pPoints (float3, strided) | -1 / 0 | segment pos + prevpos | |
| 168 | int | pPoints stride | | | |
| 176 | ptr | pVelocities (strided) | -1 / 0 | seg+48 | |
| 184 | int | vel stride | | | |
| 192 | ptr[2] | pEntTiedTo[2] (-10 = world) | -1 | 728, 736 (AddRef'd) | |
| 208 | int | bLocalPtTied | | | |
| 212 | vec3[2] | ptTiedTo[2] | mask on x | 744, 756 (stored in the tied part's local frame). If unset, taken from the end vertex. | |
| 236 | int[2] | idPartTiedTo[2] | 0x80000000 | part **index** 768/772, part **id** 776/780 | |

GetParams 13 returns the same fields. Notes:
- ptTiedTo is returned in **world** space.
- pPoints = `E+528` (segment array, stride 184). That array holds **double3**, while the struct declares a float stride, so the caller receives raw doubles (x64 quirk).
- pVelocities = seg+48.
- id 916 unused → zeros.

Side-effects of SetParams 13:
- if any value changed (accumulated Δ>1e-6): awake (E+132 bits 24..27 = 1), `E+492=0`, Reposition.
- Changing a tie releases the old constraint with Action **6** (P+4 = id E+784, remove).
- If both ends are tied to different (or non-static) entities with Σ GetMassInv > 0, it adds a constraint with Action **5** on the lighter/heavier entity (E+788 = index). Fields: pBuddy, 2 points, 2 part ids, limit E+1004, E+696, pConstraintEntity = rope. The constraint id is stored at E+784.
- Post-hook on base params id 0 (pos/q): all segments are transformed rigidly by the Δpose, then q is reset to identity.
- Turning flag 0x400 on calls `sub_350CE770`.

**GetStatus id 14 = rope status** (inf `pe_status_rope`):

| P+ | content |
|---|---|
| 4 | nSegments (returns 0 if <0) |
| 8 | in ptr → float3 positions from seg+24 (relative to the host pos E+560 if P+112) |
| 16 | in ptr → float3 velocities (seg+48) |
| 24 / 28 | out: nCollStat / nCollDyn (count of segs with a contact ent, split by ent+68>>24 static/dyn) |
| 32 | bTargetPoseActive (E+540) |
| 36 | stiffnessAnim (E+544) |
| 40 / 48 | pContactEnts = seg0+112, stride 184 |
| 56 / 64 | nVtx (E+976) and in ptr → subdivided vertex positions (only if flag 0x100) |
| 72 | in ptr → contact normals (seg+72), only for segs with a contact |
| 80 | E+536 (world frame id at the last step; inf timeLastActive) |
| 84 | posHost (E+560 double→float, or 0 if bLocal) |
| 96 | qHost (E+584) |
| 112 | byte bLocal (in) |

**Action (rope)**:

| id | handling |
|---|---|
| 14 | (before the guard) if P+4==0 and the rope is not tied at both ends: snap the tied ends to the host points, re-lay segments straight via `sub_350DA760(len/nSegs, …)`, prev=cur, wake + reposition (unless flag 0x20). Returns 1 always. |
| 2 | impulse: P+4 impulse, P+52 point (mask), P+64 ipart / P+68 partid (=segment idx, 0x80000000 unused). Segment: point given → nearest vertex within 3·collDist, split between i and i+1 by projection t; nothing given → middle (nSegs/2). Δv = impulse·(nSegs+1)/mass, clamped to world+364 (max vel). Added to the **seg+128** accumulator (i gets (1−t), i+1 gets t). Wake, reposition; P+76 iApplyTime ≤0 zeroes the idle timer E+252; awakes colliders. |
| 12 | target pose (inf `pe_action_target_vtx`; 48 B): P+4 n, P+8 vec3*. If n == nSegs+1: seg+140 ← target, and positions are snapped if flag 0x20 or the jump is >5·length (and no 0x18 flags); with E+540 the tie points are re-derived. Otherwise the target = current pose (in the host-local frame when flags 0x18). E+904 = local direction of the first segment. |
| 4 | reset: v=0, contacts released (with flag 0x100 also the subdivided-vertex contacts and colliders), E+1000=0 |
| else | base |

#### 2.3 Soft

**SetParams id 17 = soft-body params** (100 B; inf `pe_params_softbody`). GetParams 17 is the exact inverse.

| P+ | type | E+ | ctor | meaning (inf, from use) |
|---|---|---|---|---|
| 4 | float | 636 | 0.04 | thickness (collision radius; bbox ± 2·thickness for the entity query) |
| 8 | float | 648 | 0.2 | friction |
| 12 | float | 640 | 10.0 | stiffness. If <0: scaled by −mass/(maxTimeStep²·nVtx). Edge+20 = 2·√(k/(minv0+minv1))·damping |
| 16 | float | 644 | 0.9 | edge damping ratio |
| 20 | float | 660 | 0 | solver coefficient (shape stiffness) |
| 24 | float | 712 | 0 | water/contact resistance (used once in the solver) |
| 28 | float | 716 | 0 | airResistance (wind·this>0 keeps awake) |
| 32 | vec3 | 720 (+744, +732 copies) | 0 | wind. Nonzero → Awake(1) |
| 44 | float | 760 | 0.2 | windVariance |
| 48 | int | 620 | 20 | nMaxIters |
| 52 | float | 616 | 0.01 | accuracy (solver exit residual) |
| 56 | float | 664 | 0.05 | impulse scale (Action 2) |
| 60 | float | 668 | 0.001 | explosion scale (slot 58) |
| 64 | float | 672 | 1.0 | ? |
| 68 | float | 676 | 3000 | explosion impulse clamp (slot 58) |
| 72 | int | 680 | 15 | collTypes (entity mask for GetEntitiesAround, `|0x30040`). Flag bits 0x8/0x10 get toggled when attached to type-4 entities |
| 76 | float | 684 | 0 | ? |
| 80, 84 | float | 688, 692 | 0 | volume / pressure-like coefficients (block active when sum>0) |
| 88 | float | 700 | 0 | shape-restore strength toward the rest pose |
| 92 | float | 704 | 0 | falloff exponent over graph distance from attached vertices (vtx+136) |
| 96 | float | 708 | 0 | minimum restore blend |

Unused tests: floats use the mask, ints (P+48, P+72) use 0x80000000.

**SetParams id 117** (x64 10.x only; not present in x86 1.2): P+8/P+16 = `int*` begin/end. It replaces the `std::vector<int>` at E+808..824. Behaviour: a list of **part ids to ignore** in soft-body collision (Step skips entity parts whose id is in the list) (inf).

**Base post-hook (id 0, q set)**: the vertices are rebuilt from the mesh vertices rotated by the new q (relative to q0 at E+500) and stored relative to vertex 0 (absolute origin E+488). Attached vertex local points (vtx+112) are recomputed in the host part frame. Then E+500 = q, entity q = identity.

**GetStatus (soft)**:

| id | handling |
|---|---|
| 17 | soft vertices (inf). P+4 nVtx, P+8 mesh vtx ptr (geom+80), P+16 its stride (geom+88), P+24 = vtx array +32 (normals) / P+32 stride 144, P+40 mesh+104, P+48 flags (bit0 lock entity, bit1 unlock), P+52..64 host q (E+532..544), P+68..76 host pos (E+520..528), P+80 pos (E+568 if E+516 else E+408), P+92 q |
| 9 | collisions (inf `pe_status_collisions`): P+8 buffer of 80-byte entries, P+16 max/out count. From vertices with a contact (not attached). Each entry: pt, n (vtx+44), v (vtx+12), v_other (vtx+92), mass, 1/GetMassInv(other), entity id, partid (vtx+74), mat ids (vtx+104/+106). If full, the weakest entry is replaced. |

**Action (soft)**:

| id | handling |
|---|---|
| 2 | impulse (validated). P+4 impulse·E+664, P+52 point (unused → bbox center), P+68 partid / P+64 ipart (= triangle index; ≥ nTris → spread over nearby triangles). Distributes Δv into vtx+12 weighted by vtx+24 (minv). Wakes unless flag 0x4. |
| 11 | attach points (inf; 48 B): P+8 pEntity (-10 world; 0 + P+24 unused → detach all, E+484=0), P+16 partid, P+24 int* vertex idx, P+32 vec3* points, P+40 n. n<0 → auto-pick vertices inside entities from GetEntitiesAround(mask 263) and allocate the list into P+24. Attaching is done by `sub_350EFC00`. |
| 12 | set local target points: P+4 n, P+8 vec3*, P+16 offset v3, P+28 quat → vtx+112 = q⁻¹(pt−offset) for non-attached vertices |
| 4 | reset: v=0. P+4: 2 → drop contacts; 3 → restore the rest shape from vtx+124 and recompute the mesh normals |
| else | base |

---

### 3. Member layouts

#### 3.1 CParticleEntity (0x2A8)

| E+ (dec) | type | meaning (inf) | ctor |
|---|---|---|---|
| 448 | f | mass | 0.2 |
| 452 | f | radius r = size/2 | 0.05 |
| 456 | f | 1/r | 20 |
| 460 | f | thickness/2 | 0.05 |
| 464 | f | kAirResistance | 0 |
| 468 | f | kWaterResistance | 0.5 |
| 472 | f | accThrust | 0 |
| 476 | f | kLift (= accLift/\|v\|) | 0 |
| 480 | v3 | gravity | world+284 (world gravity) or (0,0,-9.81) |
| 492 | v3 | gravity0 (copy) | = gravity |
| 504 | v3 | waterGravity | 0.8·gravity |
| 516 | v3 | rollAxis | (0,0,1) |
| 528 | v3 | normal | (0,0,1) |
| 540 | v3 | heading | (1,0,0) |
| 552 | v3 | velocity | 0 |
| 564 | v3 | w (spin) | 0 |
| 576 | quat | q0 | identity |
| 592 | f | minBounceVel | 1.5 |
| 596 | f | minVel (sleep) | 0.02 |
| 600 | i | surface idx | 0 |
| 608 | ptr | pColliderToIgnore | 0 |
| 616 | i | collTypes | 0x11F |
| 620 | v3 | last contact normal | (0,0,1) |
| 632 | f | ? (forced to 1.0 if 0 at step) | 0 |
| 636 | f | water depth (<0 = submerged; selects water gravity/resistance) | 0 |
| 640 | v3 | water flow velocity | 0 |
| 652 | f | step time performed | 0 |
| 656 | f | step time full | – |
| 660 | f | time in sliding state (>40 s → state 2) | 0 |
| 664 | f | time accumulated while asleep | 0 |
| 668 | u32 | bits 0..7 areaCheckPeriod (6), 8..15 area countdown byte (+669), 16 collided-flag, 17..19 bounce counter, 20..21 state (0 sleep, 1 flying, 2 resting/sliding; ctor 2), 22..25 pierceability (ctor 15), 26 (0x4000000) contact/sliding mode | |
| 672 | lock | particle RW lock | |

Flags E+92 (behaviour): 1 = no tangential motion / stop on contact. 2 = no rotation (keep q). 4 = no spin from contact. 8 = q = q0. 0x10 = zero spin in flight. 0x100 = no collision with particles that also have 0x100. 0x200 = only report the impulse in the event, don't apply it. 0x400 = traceable (in grid). 0x2000 = immediate collision callback (world+21248 list). 0x2000000 = queue collision event (world+21088 list, 160 B). 0x800000 = skip area checks.

#### 3.2 CRopeEntity (0x400)

| E+ | meaning (inf) |
|---|---|
| 448/460 | gravity / gravity copy (ctor world+284) |
| 472 | damping (0.2) · 476 maxTimeStep (0.05) · 480 minEnergy (0.0016) |
| 484 / 488 | step performed / full |
| 492 | sleep frame counter · 496 last dt · 500,508 ? · 504 ? |
| 512 / 516 | locks for vertex / contact lists |
| 520 | length · **524 nSegs** · **528 segment array** (nSegs+1 entries, stride **184**) |
| 536 | world frame id of the last step |
| 540 | bTargetPoseActive · 544 stiffnessAnim · 548 pose blend · 552 host-follow |
| 560 | host pos (double3) · 584 host q |
| 600 mass · 604 collDist · 608 surface · 612 friction · 616 ? (world+260) · 620 dampingAnim · 624 stiffnessDecayAnim |
| 628 wind · 640 wind prev · 652 wind next (random) · 664 airRes · 668 windVariance · 672 wind lerp timer (4 Hz) · 676 waterRes · 680 1/density |
| 684..724 | see params table · 700 part-flag mask (3) · 704 strain threshold · 708 iterations (650) |
| 728/736 | pEntTiedTo[2] · 744/756 ptTied local · 768/772 part index · 776/780 part id · 784 constraint id · 788 which end owns the constraint |
| 792..900 | host-frame history: pos 792(prev)/804(cur) host0, 816/828 host1, quats 840/856, 872/888 |
| 904 | first-segment local dir · 916..936 optional line (P+136) |
| 940 | delayed-step marker (ctor −1; main path when <0) |
| 944 / 952 | subdivided vertex arrays (stride 128, 2·nSegs+2 alloc; flag 0x100) · 960 ? · 968 nVtx used · 972 alloc count · 976 nVtx reported · 984 min gap |
| 1000 | contacts-active flag · 1004 constraint limit · 1008 energy · 1016 ? |

**Segment (184 B)**: +0 pos (**double3**, current) · +24 pos at step start (double3) · +48 vel (float3) · +60 float3 (rotated with the body) · +72 contact normal float3 (0xFFBFFFFF = none) · allocation is pre-filled with `0xDEADBEEF`/`0xCADEFACE` markers, then memset to 0 · +112 contact entity ptr · +120 contact part idx · +128 Δv/impulse accumulator float3 · +140 target-pose point float3 · +156 float · +172 int (vertex index into the subdivided list).

Subdivided vertex (128 B): +0 pos double3 · +24 double3 · +48 vel · +112 contact ent · +120 part idx.

Flags E+92 (behaviour): 0x2 = energy from velocities only (no friction solver). 0x8/0x10 = target pose relative to host 0/1. 0x20 = do not wake tied entities / kinematic (`E+540==2` pins vertex 0 and returns). 0x80 = collision variant. 0x100 = subdivided vertices. 0x400 = traceable. 0x200000/0x400000 = collision mode bits (fed into the GetEntitiesAround mask `((f&0x400000)|((f>>12)&0x60000)|0xC011FFFF)>>14`). 0x10000 = no re-step within frame. 0x40000000 = keep idle timer on Awake. ctor = 0x20000000.

#### 3.3 CSoftEntity (0x360)

| E+ | meaning (inf) |
|---|---|
| **448** | vertex array (stride **144**) · **472 nVtx** |
| **456** | edge array (stride **36**) · **476 nEdges** |
| 464 | per-edge 8-byte array (adjacency) · 552 per-edge array |
| 480 | has-mesh/initialized (Step exits if 0) · 484 nAttached |
| 488 | absolute pos of vertex 0 (vertices stored relative to it) |
| 500 | q0 (rest orientation) · 516 host-attached flag · 520/532 host pos/q · 568 pos alt |
| 580 / 584 | step full / performed |
| 588 gravity · 600 minEnergy · 604 maxTimeStep · 608 sleep counter (asleep after 3 quiet steps) · 612 damping · 616..760 see params 17 · 624 last dt · 632 max displacement² (delayed step) · 652 density · 696 per-vertex volume |
| 732/744 | wind prev/next (randomised every 0.25 s via the E+756 timer, amplitude \|wind\|₁·E+760) |
| 768 | helper/proxy entity (SetParams id 0 when going to sleep) · 792 temp buffer · 808..824 ignore-part-id vector (id 117) · 848 event storage · 856 soft lock |

**Vertex (144 B)**: +0 pos (rel. to E+488) · +12 vel · +24 massInv · +28 mass · +32 normal · +44 contact normal · +56 int (index list: entries 0..nAttached-1 hold attached vertex indices) · +72 byte bAttached · +74 short partid · +80 contact/attached entity · +92 contact entity velocity · +104/+106 short surface ids · +112 attach/target point (host-local) · +124 rest position (mesh local) · +136 int graph distance from attachment.

**Edge (36 B)**: +0 i0 · +4 i1 · +8 rest length · +12/+16 (restored by OnDelayedStep) · +20 damping coefficient · +24 reduced massInv (1/(minv0+minv1)).

AddGeometry: unique triangle edges come from a bit-matrix of n(n+1)/2 bits.

---

### 4. Step algorithms

#### 4.1 Particle DoStep `0x3503c210` (slot 25; x86 `0x35042c60`)

1. Read thread index → per-thread hit buffer `0x3520B690 + 640·idx` (8 hits × 80 B). Choose air or water params: if depth E+636<0, use waterGravity scaled by `clamp(-depth/r, 0, 1)` and kWaterRes.
2. Time slice: `performed += dt` (with a 1e-4 cap at the end). If the collider-to-ignore is a rope type (0x7000000), clear it. If `!IsAwake` → `E+664 += dt`, return.
3. Register itself in a per-thread "current entity" slot (used by RayTrace self-skip).
4. Integration: semi-implicit midpoint. If not in contact mode, `v_mid = v + a·dt/2`. `newpos = pos + v_mid·dt`. Bounce counter decays.
5. **Contact (sliding) mode** (E+668 & 0x4000000):
   - Probe ray from newpos along `-normal·1.1r` (RWI, objtypes = collTypes, flags = pierceability|0x80000000).
   - Miss → leave contact mode. If rolling is allowed, set w from gravity×heading.
   - Hit → snap to surface + thickness/2. Get the surface velocity (hit entity vfunc +72 = GetStatus dynamics). Tangential friction = (friction[mat_hit]+friction[mat_self]) clamped ≥0, from table `world+6504[idx&0x1FF]`. Velocity: `v_t` reduced by `μ·|v_n|·…`.
   - With flags 1/4 or normal.z<0.5 → friction branch, reset w/q. Otherwise roll: w = n×v / r, and q integrates around rollAxis (the q update uses `sub_35040F80`).
   - The state becomes 1 or 2 (2 if the surface is static (type ≤ 2) or slid >40 s).
6. **Free flight**: v += (g + heading·accThrust + lift(v) − v·kAir)·dt. Spin integrates q (unless flag 2/0x10); flag 8 forces q=q0. Heading re-normalised from v.
7. **Collision sweep**:
   - (a) When `iCaller<4` and `world+1744==2`: GetEntitiesAround(pos±r, mask 4 = rigid) and per-part `world->RayTraceEntity` (vfunc +488) for parts with flag 0x8000. On hit, a second RWI with mask 257 and flags `|0x80000080`.
   - (b) Otherwise: one RWI from the old pos to (newpos + dir·r), objtypes = collTypes|0x200, flags = pierceability|0xA0001500, up to 8 hits, tag `"RayWorldIntersection(PhysParticles)"`.
8. **Per hit** (iCaller<4): build EventPhysCollision (160 B):
   - Gather the other entity's point velocity via vfunc +512 (GetRigidBody: v + w×r) and the part mass.
   - If v_rel·n<0 and \|v\|>minBounceVel: impulse magnitude `m1·m2/(m1+m2)·k`, with k scaled by `(1−(pierce_mat − pierce_self)/15)/2`. Applied via Action(2) on the hit entity unless flag 0x200.
   - Event dispatch: flag 0x2000 → immediate callbacks, 0x2000000 → queued log.
   - A hit on surface `world+20840` → special handler `sub_350FF0E0` (inf: water/decal surface). Otherwise set the collided bit and bump the bounce counter.
9. **Response** (first solid hit):
   - Restitution e = avg bounciness (`world+4456[idx]`) clamped to [0,1]. It is zeroed if v_n > −minBounceVel or the particle is "flat" (r·0.3 > thickness/2).
   - Friction μ as above. Mass ratio uses the other entity's GetMassInv (vfunc +552).
   - v' = v − (1+e)(1−ratio)·v_n·n with Coulomb clamp on v_t.
   - If \|v'\| < minVel and moving into gravity, or flag 1 → stop: w=0, q=q0, enter contact mode, pos = hit + n·thickness/2. Otherwise pos = hit − dir·r, w = n×v_t / r (clamped \|w\|≤20).
   - State bits: 1 if n.z ≤ 0.7 or (dynamic surface and slide ≤ 40 s), else 2.
10. Commit under lock: pos, q, BBox, v, contact normal, heading. If flag 0x400: Reposition (`sub_35080A00(world,this,1,bbox)`).
11. Area check every `areaCheckPeriod` steps (countdown in byte E+669, unless flag 0x800000): world->CheckAreas → gravity override (gravity/waterGravity), water depth E+636 (min over areas), water flow E+640.
12. iCaller<4: post a PostStep event (id 4: entity, pos, q, dt, frame id `world+21572`). Optional profiling (`world+336`).

#### 4.2 Rope Step `0x350ea720` → Step2 `0x350e5020` (x86 `CRopeEntity__Step2` `0x350f11b0`)

1. Exit if nSegs ≤ 0 or asleep. segLen = length/nSegs. Damping factor = `max(0, 1 − (damping + (targetPose? 0 : dampingAnim))·dt)`.
2. Untie ends whose entity was deleted or whose part id no longer matches (re-lookup part by id), via SetParams. Flag 0x20 with E+540==2 → pin seg0 to host, update bbox, return.
3. Time slice (E+484/488). `world+1744==2` → dt clamped to the remaining time (≥0.001). E+496=dt, E+536 = frame id.
4. CheckAreas (unless flag 0x800000; only when E+232==0) → gravity override, plus up to 4 water/buoyancy areas. Post a **PreStep** event (id 4).
5. Wind: every 0.25 s pick next = wind + rand·\|wind\|₁·variance, then lerp. Host-follow (E+552): shift all vertices by the host displacement.
6. **Integrate** each vertex:
   - prev(+24) = pos(+0).
   - pos += v·dt, with optional target-pose blending (mode 1, E+544/E+548).
   - v += g·dt.
   - v += (wind − v)·min(1, dt·airRes).
   - For each submerged area (depth>0): `v += (flow − v)·min(1,dt·waterRes) − g·min(1,depth/collDist)·(areaDensity·1/ρ)·dt`.
7. Tie strain/break handling. A tie to a dynamic host may break if ends > 0.95·length and E+704 > 0 → flag 0x400000 cleared. Breaking posts 128-byte events and unties via SetParams.
8. **Length solver** `sub_350DA760(segLen, …, E+540)`: iterative distance-constraint (position) solver over consecutive vertices with the tied end points fixed. Tolerances E+684/E+688. Then target-pose spring `sub_350E2450` (stiffness E+544 decaying along the rope by E+624, damping E+620).
9. **Collision**: GetEntitiesAround (bbox of rope ± collDist, mask derived from flags) → per entity/part (part flags & E+700) primitive tests (`sub_35196030` box overlap, `sub_351981B0` geometry intersect, geometry vfuncs +88/+176/+376). Contacts are stored per segment (+72 normal, +112 entity, +120 part). Then:
   - non-subdivided: `sub_350E3680` (contact projection with collDist, coefficients E+720/E+724)
   - flag 0x100: `sub_350DD620` (subdivided vertex rope: vertices inserted around contacts, min gap E+984, friction E+612, threshold E+716)
10. Velocity/friction pass `sub_350D06A0` (iterations E+708, friction E+612, E+616/dt) returns kinetic energy E. With flag 2, E = Σ\|v\|² directly.
11. **Sleep**: if `E < (nSegs+1)·minEnergy` → `++E+492`, else reset. Asleep when the counter ≥4 and not tied to an awake rope, and the idle timer (E+252 vs maxIdle E+256) allows it. When going to sleep, the interior vertex velocities are zeroed. Sets E+132 bits 24..27.
12. Commit: bbox, entity pos = seg0.pos, prev = cur, host frame E+560/584 from tie 0 (GetPartTransform). Flag 0x400 → Reposition. Returns `performed ≥ full`.
13. Slots 40/41 add LCP contacts with dynamic entities (176-byte contacts) through the world solver. Slot 58 handles explosion breaking.

#### 4.3 Soft Step `0x350f8b10` (x86 `CSoftEntity__Step` `0x351072b0`); OnDelayedStep `0x350ec810`

1. Exit if nVtx ≤ 0, asleep, or E+480==0. Flags 0x10020 → just post the PostStep event (id 4) and return.
2. Drop attachments to rope-type entities that are being deleted. Time slice E+580/584.
3. CheckAreas (4 buoyancy areas). Wind randomisation (every 0.25 s: E+744 = wind + rand·\|wind\|₁·E+760).
4. Collision gather (if thickness>0): when only attached to one host with collTypes 0 → use that host. Otherwise GetEntitiesAround(bbox ± 2·thickness, collTypes|0x30040). Parts are filtered by (part flags & own part flagsCollider) and by the id-117 ignore list. Pre-tests via geometry intersect (`sub_351981B0`). Builds contact records (472 B each) in a stack buffer.
5. Mesh update (`sub_350FDBE0` per-vertex normals, `CTriMesh::RebuildBVTree` `sub_351C02E0`).
6. **Solver** `sub_350F2D60` (x86 `0x35100940`, ~15 KB). Iterative edge-spring/constraint solver: up to `nMaxIters` (E+620) iterations, early exit when residual ≤ accuracy (E+616).
   - Springs use stiffness E+640 and per-edge damping (edge+20).
   - Contact friction E+648, water resistance E+712, pressure terms E+688/E+692.
   - Shape restore toward the rest pose with strength E+700, falloff `1 − (graphDist·x)^(1/E+704)` and min E+708.
   - Attached vertices follow their host part pose (vtx+112).
   - Gravity, wind and airRes E+716 are applied to velocities.
7. Commit: bbox, entity pos += vtx0 offset (vtx0 re-zeroed), E+624=dt.
8. **Sleep**: awake if wind²·airRes > 0 or energy > minEnergy (E+600). Otherwise `++E+608` and sleep at ≥3 (also notifies the proxy entity E+768 via SetParams id 0).
9. Reposition (`sub_35080A00(…,2)`). If the part has no flag 0x80000, refresh the geometry in the world (`sub_35081760`). Apply part scale. Post a PostStep event with collision storage E+848.
10. OnDelayedStep (slot 61; same pattern for rope): consume a recorded record from the stream (host frame, vertex array, edge +12/+16), compute the max displacement, mark the step done, call Step.

---

### 5. Main uncertainties

- The role names for many rope/soft scalar parameters are behavioural guesses: rope P+24/28/84/88/104..116/128/132, soft P+20/24/64/76/80/84/88..96. The offsets and unused tests are exact.
- Particle DoStep and the rope/soft solvers (`sub_350DA760`, `sub_350E3680`, `sub_350DD620` 3.3k lines, `sub_350D06A0`, soft `sub_350F2D60` 3.7k lines) are outlined, not fully decoded. The exact friction/restitution formulas only appear in §4.1(9) as read.
- Rope segment +0 vs +24: Step2 shows +0 = current and +24 = start-of-step. GetStatus/GetLength read +24, so outside a step they are equal.
- Rope GetParams returns a double array with a float stride (24/184). This looks like an x64-port bug kept as is.
- Particle slot 16 / soft slot 16 semantics ("alt snapshot") are unclear. Particle E+632 is unknown.
- Soft params id **117** is x64-only (10.x). Its meaning (ignore-part-id list) is inferred from one use in Step.
- The x86 particle vtable has 78 entries. x86 slot 75 is the base ComputeBBox, while x64 overrides it.

