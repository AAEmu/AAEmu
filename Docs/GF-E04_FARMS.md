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
| `SCShowCommonFarmPacket` | S2C | `0x220` | **writer corrected to the real wire** — was writing a fixed 28-byte body with no position loop |

New supporting code:

- `PublicFarmPlacementRules` — the pure placement decision table (no shipped values baked in).
- `PublicFarmManager.GetPlantedCount` / `RemoveCharacterFarms`.

`SC 0x220` (`SCShowCommonFarmPacket`) is now written to the real wire, but nothing calls it — see the
blocked remainder for why the request path is still unreachable.

## Wire contract (evidence: the raw client packet schema `ir`)

The derived schema files omit the guarded loops; the authoritative source is the `ir` (intermediate
representation) in the raw client packet schema, which records each field plus its `if`/`loop`
structure.

- **SC 0x220 `SCShowCommonFarmPacket`** — `u32 type` (object offset 16), then a **signed `s32
  count`** (object offset 20), then, only when `count > 0`, a loop of `count` elements at object
  offset 24 with a **24-byte stride**. Each element is the standard **11-byte quantized world
  position** — the same shared position serializer used by nine packets (SC `0x06E`, `0x0A2`,
  `0x0ED`, `0x11B`, `0x220`, `0x229`, `0x243`, `0x32B`, and CS `0x0BE`). The 24-byte stride is
  confirmed by the sibling packet that places that serializer at object offset 24 with its next
  field at object offset 48. The writer is the same shape the farm-list response already uses, and
  reuses `PacketStream.WritePosition`, which produces exactly those 11 bytes.

  This body **was wrong before**: it wrote a fixed 28-byte `(int, int, u64, u64, f32)` with no loop
  and no positions, so the count said one thing and the bytes said another. It is now
  `u32 type`, `s32 count`, then `count × WritePosition(...)`, and a negative count is refused
  instead of looped on.

  *Correcting an earlier note in this document:* the previous revision described the element as
  "11 quantized worldPos bytes + 11 selector bytes + two conditional 8-byte chunks gated on a float
  compare", and called it an unreproducible client-internal record. That was a misreading of the
  serializer — the "selector bytes" are the same 11 position bytes counted a second time, and the
  "conditional chunks" is a single SIMD shuffle on the write path, not additional fields. There is
  no unreproducible record here.
- **CS 0x164 `CSPlaceCommonFarmPacket`** — `u32 type`, **`s32 count`**, then, only when
  `count > 0`, a loop of `count` × 12-byte `vec3 point` (count clamped to 128 by the writer).
  The previous parser read `count` as unsigned and consumed exactly one point, so a zero count
  under-flowed into a phantom point and a count above one left the rest of the body unread. It now
  reads the signed count and the whole point loop (bounded by the same 128-point wire clamp).
- **CS 0x163 `CSRemoveCommonFarmsPacket`** — empty body (`nullsub`). The removal handler is correct.
- **CS 0x15E `CSShowCommonFarmAreaPacket`** — absent from the raw schema for this build (it is a
  folded leftover) and has no 10.x client sender, so nothing asks for the show-area response.

## Content binding

- Farm tabs are `FarmType` (`Invalid=0, Farm=1, Nursery=2, Ranch=3, Stable=4`).
- Per-group capacity is `farm_groups.count` (content). A group with **no** row yields capacity `0`
  and is refused — there is no default capacity.
- Every point in a placement batch must resolve to the farm tab the request names, and the batch
  must fit the group's content capacity; otherwise the request is refused with the farm's own client
  error (`CommonFarmNotAllowedType` / `CommonFarmCountOver`).

## Blocked remainder

1. **Subzone → farm-area binding is still the pre-existing hardcoded map, and it is known-LOSSY.**
   The farm-area lookup (`sub_zones` → farm group) has no typed link to a `common_farms` row, and
   this has now been confirmed rather than assumed. In the shipped content:

   | Table | Columns | Rows |
   |---|---|---|
   | `common_farms` | `id`, `name`, `guard_time`, `farm_group_id`, `comments` | 46 |
   | `sub_zones` | `id`, `idx`, `name`, `x`, `y`, `w`, `h`, `linked_zone_group_id`, `parent_sub_zone_id`, `category_id`, … | 1356 |

   A column scan of all 1374 tables finds no join. `common_farms` carries only `farm_group_id`
   (1 = 38 rows, 4 = 8 rows); `sub_zones` carries only `parent_sub_zone_id`. Every one of the five
   farm subzones has `linked_zone_group_id = 0`, so that column cannot discriminate either, and
   `category_id` does not discriminate because all five are `category_id = 1` among 64 rows.

   The **only** correlation available is `sub_zones.name == common_farms.name`, which is Korean
   display text. This project forbids display-name classification, and the correlation is lossy on
   its face: **46 authored farms collapse onto just 5 subzone rows** across 3 distinct names. Four
   of those five subzones are content-indistinguishable — same name, same `category_id`, same
   `linked_zone_group_id` — yet the hardcoded map in `PublicFarmManager.Load()` assigns them four
   *different* farm tabs (`966` = Farm, `967` = Ranch, `968` = Nursery, `998` = Farm). That mapping
   is arbitrary and cannot be reconstructed from content.

   So the existing 5-entry map is **known-lossy, not merely unproven**. This slice adds no new
   hardcoded ids and does not touch that map. Making the area lookup content-driven needs either a
   typed link in content or an explicit, reviewed decision — and until one exists, the farm type
   reported for subzones 966/967/968 is a guess that should not be trusted.

2. **`CSPlaceCommonFarmPacket` does not itself create a doodad.** The packet carries no doodad or
   item identity, so it cannot. The actual per-doodad creation is the already-wired
   `CSCreateDoodadPacket` path (which validates the same area and capacity and then calls
   `CreatePlayerDoodad`). This handler adds the request-level refusal; it invents no mapping from
   (type, point) to a doodad template.

3. **Harvest / item-return semantics are out of scope.** `CSRemoveCommonFarmsPacket` is a bulk
   clear: it deletes the character's farm doodads without returning items. Returning the crop is the
   separate `DoodadFuncFinal` interaction path, not this packet, and is not touched here.

4. **The show-area response is implemented but not yet reachable.** `SC 0x220` is now written to the
   real wire, but nothing calls it: the show-area request that would trigger it is `CS 0x15E`
   (below). So the writer is correct and unit-pinned, and the flow remains unexercised.

## Verification

- `CS 0x164` parser tests (evidence-backed): consumes the whole point loop; zero count carries no
  points; negative (signed) count carries no points; a hostile count is bounded by the wire clamp; a
  single point matches a one-entry request.
- `SC 0x220` writer tests (evidence-backed, pinned against the raw schema shape): zero count is an
  8-byte header with no positions; one count emits exactly one 11-byte position; `N` counts emit
  exactly `N` positions at the documented stride; a negative count is refused rather than looped
  on; a count past the available positions is bounded; a hostile count is bounded by the wire clamp.
  The position bytes are compared against the shared quantized-position block, so the test fails if
  the writer ever stops using the standard serializer. Both the loop and the negative-count guard
  were mutation-checked: disabling the loop fails 4 of the 9 tests, and accepting a negative count
  fails 2.
- Placement decision tests: farm-type validity, area mismatch, no-content capacity, empty request,
  exact fit, one-over, and a `uint.MaxValue` overflow guard.
- Full unit suite passing (see the task result for the exact count).
- `git diff --check` clean; existing files keep their original BOM convention (the four no-BOM packet/interface files remain no-BOM), and new C# files carry the repository UTF-8 BOM.

## Live acceptance still required

Per the five-gate testing protocol, this slice is verified at the unit/wire level only. Before it
can be called done it still needs the live gates: reachability from the real client farm UI, the
request on the wire, the server-side effect and persisted doodad row, a client-visible result, and
a clean-state restart.
