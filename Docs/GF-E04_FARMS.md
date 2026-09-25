# GF-E04 — public-farm placement / removal (show-area blocked)

Scope of this slice: the public-farm **placement** and **removal** request packets, wired through the
existing doodad/unit path. The farm growth/expiry core that already existed is not modified.

Base for this work: `client_version/zone-10.0.2_r575` @ `2c19b936`.

## What changed

| Packet | Direction | Opcode | Change |
|---|---|---|---|
| `CSPlaceCommonFarmPacket` | C2G | `0x164` | parser fixed to the real wire (signed count + full point loop); the request is now validated against the farm area and the group capacity |
| `CSRemoveCommonFarmsPacket` | C2G | `0x163` | was a no-op; now removes every farm doodad the character has planted |
| `CSShowCommonFarmAreaPacket` | C2G | `0x15E` | documented as an unreachable leftover (no 10.x sender); answers nothing |

New supporting code:

- `PublicFarmPlacementRules` — the pure placement decision table (no shipped values baked in).
- `PublicFarmManager.GetPlantedCount` / `RemoveCharacterFarms`.

`SC 0x220` (`SCShowCommonFarmPacket`) is **not** part of this slice — see the blocked remainder.

## Wire contract (evidence: the raw client packet schema `ir`)

The derived schema files omit the guarded loops; the authoritative source is the `ir` (intermediate
representation) in the raw client packet schema, which records each field plus its `if`/`loop`
structure.

- **CS 0x164 `CSPlaceCommonFarmPacket`** — `u32 type`, **`s32 count`**, then, only when
  `count > 0`, a loop of `count` × 12-byte `vec3 point` (count clamped to 128 by the writer).
  The previous parser read `count` as unsigned and consumed exactly one point, so a zero count
  under-flowed into a phantom point and a count above one left the rest of the body unread. It now
  reads the signed count and the whole point loop (bounded by the same 128-point wire clamp).
- **CS 0x163 `CSRemoveCommonFarmsPacket`** — empty body (`nullsub`). The removal handler is correct.
- **CS 0x15E `CSShowCommonFarmAreaPacket`** — its body shares a serializer with an unrelated packet
  (a folded leftover) and has no 10.x client sender.

## Content binding

- Farm tabs are `FarmType` (`Invalid=0, Farm=1, Nursery=2, Ranch=3, Stable=4`).
- Per-group capacity is `farm_groups.count` (content). A group with **no** row yields capacity `0`
  and is refused — there is no default capacity.
- Every point in a placement batch must resolve to the farm tab the request names, and the batch
  must fit the group's content capacity; otherwise the request is refused with the farm's own client
  error (`CommonFarmNotAllowedType` / `CommonFarmCountOver`).

## Blocked remainder

1. **Show-area (SC 0x220) is blocked.** Its real body is `u32 type`, `s32 count`, then a conditional
   loop of a **variable-layout client-internal unit-state position record** (11 quantized worldPos
   bytes + 11 selector bytes + two *conditional* 8-byte chunks gated on a float compare). The server
   cannot reproduce that record's bytes from the available evidence, so no response is synthesised
   and the show-area request is left unreachable. `SCShowCommonFarmPacket` is left at its
   pre-existing form and is not referenced by this slice. An earlier revision of this work wrongly
   reduced it to an 8-byte `type+count` writer; that was reverted.

2. **Subzone → farm-area binding is still the pre-existing hardcoded map.** The farm-area lookup
   (`sub_zones` → farm group) has no typed column that links a subzone to a `common_farms` row. The
   only correlation available in the shipped content is by display name, which this project forbids.
   This slice adds no new hardcoded ids and does not touch the existing
   `PublicFarmManager.Load()` map; making the area lookup content-driven is a follow-up that needs
   either a typed link or an explicit, reviewed decision.

3. **`CSPlaceCommonFarmPacket` does not itself create a doodad.** The packet carries no doodad or
   item identity, so it cannot. The actual per-doodad creation is the already-wired
   `CSCreateDoodadPacket` path (which validates the same area and capacity and then calls
   `CreatePlayerDoodad`). This handler adds the request-level refusal; it invents no mapping from
   (type, point) to a doodad template.

4. **Harvest / item-return semantics are out of scope.** `CSRemoveCommonFarmsPacket` is a bulk
   clear: it deletes the character's farm doodads without returning items. Returning the crop is the
   separate `DoodadFuncFinal` interaction path, not this packet, and is not touched here.

5. **`SCResponseCommonFarmListPacket` (serves the already-working `CSRequestCommonFarmList`) is not
   touched.** Its per-element layout is not proven by the available schema and it is outside the
   E04 row.

## Verification

- `CS 0x164` parser tests (evidence-backed): consumes the whole point loop; zero count carries no
  points; negative (signed) count carries no points; a hostile count is bounded by the wire clamp; a
  single point matches a one-entry request.
- Placement decision tests: farm-type validity, area mismatch, no-content capacity, empty request,
  exact fit, one-over, and a `uint.MaxValue` overflow guard.
- Full unit suite passing (see the task result for the exact count).
- `git diff --check` clean; existing files keep their original BOM convention (the four no-BOM packet/interface files remain no-BOM), and new C# files carry the repository UTF-8 BOM.

## Live acceptance still required

Per the five-gate testing protocol, this slice is verified at the unit/wire level only. Before it
can be called done it still needs the live gates: reachability from the real client farm UI, the
request on the wire, the server-side effect and persisted doodad row, a client-visible result, and
a clean-state restart.
