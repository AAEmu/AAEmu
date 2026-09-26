# GF-E04 — public-farm wire shape (placement / removal are parsed, not acted on)

Scope of this slice: the **wire shapes** of the three public-farm request packets and the one farm
response writer. **No request path in this slice mutates game state**, and the reason is evidence,
not omission. The farm growth/expiry core that already existed is not modified.

Base for this work: `client_version/zone-10.0.2_r575`.

## What changed

| Packet | Direction | Opcode | Change |
|---|---|---|---|
| `CSPlaceCommonFarmPacket` | C2G | `0x164` | **parser fixed to the real wire** (signed count + full point loop). `Execute` is a logged no-op |
| `CSRemoveCommonFarmsPacket` | C2G | `0x163` | **parsed and logged; deletes nothing.** Was: deleted every crop the caller had planted |
| `CSShowCommonFarmAreaPacket` | C2G | `0x15E` | parsed; answers nothing. Sender status stated narrowly — see below |
| `SCShowCommonFarmPacket` | S2C | `0x220` | **writer corrected to the real wire** — was writing a fixed 28-byte body with no position loop |

Removed supporting code: `PublicFarmPlacementRules` (and its tests) and
`PublicFarmManager.GetPlantedCount` / `RemoveCharacterFarms`.

`SC 0x220` is written to the real wire but nothing calls it, because the request that would trigger
it is `CS 0x15E`. See the blocked remainder.

## Why the two request handlers do nothing

Three independent sources agree that no gameplay path reaches `0x163` or `0x164`, and acting on
them was actively destructive:

1. **The raw `ir`.** `CSRemoveCommonFarmsPacket` has **zero body rows and no handler** in both the
   retail and the development build. `CSPlaceCommonFarmPacket` has three body rows and **no handler**
   in either. Nothing in the retail server reads these opcodes.
2. **The client.** Neither `0x163` nor `0x164` appears anywhere in the 7,737-file client Lua
   extract, by literal or by any farm-packet name.
3. **What they actually are.** They are the farm **shape-editor** commands
   (`CS_PACKET_PLACE_COMMON_FARM_SHAPE` / `REMOVE_COMMON_FARM_SHAPES`). A shape editor is not a
   player action, and a player never sends these.

**What was withdrawn and why.** `0x163` previously read as "drop every farm doodad this character
has planted" and did exactly that — deleting the caller's crops, rows included, and answering
nothing. That reading was *invented here* rather than recovered, and a packet that silently
destroys a player's world on arrival is not a behaviour to ship on the strength of a plausible name.

`0x164` previously ran a validation that **looked protective and was not**: it compared the
request's **point count** — the number of vertices in a shape polygon — against the farm group's
**crop capacity**. Those are unrelated numbers, so the check could not have meant anything. The
rules have been removed rather than retuned.

`PublicFarmRequestSurfaceTests` now pins the interface to carry **no** bulk-delete and **no**
planted-count member, so the hazard cannot be rebuilt under a new name. That test is the one
assertion that outlived the deleted tests, and it is mutation-checked: re-adding the bulk delete
to both the interface and the implementation fails 2 of 2.

## Wire contract (evidence: the raw client packet schema `ir`)

The derived schema files omit the guarded loops; the authoritative source is the `ir`, which records
each field plus its `if`/`loop` structure.

- **SC 0x220 `SCShowCommonFarmPacket`** — `u32 type` (object offset 16), then a **signed `s32
  count`** (object offset 20), then, only when `count > 0`, a loop of `count` elements at object
  offset 24 with a **24-byte stride**. Each element is the standard **11-byte quantized world
  position** — the same shared position serializer used by nine packets (SC `0x06E`, `0x0A2`,
  `0x0ED`, `0x11B`, `0x220`, `0x229`, `0x243`, `0x32B`, and CS `0x0BE`). The 24-byte stride is
  confirmed by the sibling packet that places that serializer at object offset 24 with its next
  field at object offset 48. The writer reuses `PacketStream.WritePosition`, which produces exactly
  those 11 bytes.

  This body **was wrong before**: it wrote a fixed 28-byte `(int, int, u64, u64, f32)` with no loop
  and no positions, so the count said one thing and the bytes said another. It is now
  `u32 type`, `s32 count`, then `count × WritePosition(...)`, and a negative count is refused
  instead of looped on.

  *Correcting an earlier note in this document:* a previous revision described the element as
  "11 quantized worldPos bytes + 11 selector bytes + two conditional 8-byte chunks gated on a float
  compare", and called it an unreproducible client-internal record. That was a misreading of the
  serializer — the "selector bytes" are the same 11 position bytes counted a second time, and the
  "conditional chunks" is a single SIMD shuffle on the write path, not additional fields. There is
  no unreproducible record here.
- **CS 0x164 `CSPlaceCommonFarmPacket`** — `u32 type`, **`s32 count`**, then, only when
  `count > 0`, a loop of `count` × 12-byte `vec3 point` (count clamped to 128 by the writer).
  The previous parser read `count` as unsigned and consumed exactly one point, so a zero count
  under-flowed into a phantom point and a count above one left the rest of the body unread — a
  stream desynchronisation, not just a miscount. It now reads the signed count and the whole point
  loop, bounded by the same 128-point wire clamp. **This parser fix is the substance of the slice**
  and is retained even though the handler no longer acts on the request.
- **CS 0x163 `CSRemoveCommonFarmsPacket`** — **empty body, zero `ir` rows, no handler.** Parsed as an
  empty body and logged.
- **CS 0x15E `CSShowCommonFarmAreaPacket`** — a **development-build-only** packet; absent from the
  retail schema. See the narrow sender claim below.

### The 0x15E sender claim, stated no more strongly than the evidence

An earlier revision of this document asserted flatly that `0x15E` has "no 10.x sender". That is
wider than what was actually checked. What is verified: the opcode does not appear anywhere in the
7,737-file client Lua extract, by literal or by farm-packet name; and the retail schema records no
such packet. What is **not** verified: the client console layer is **absent from that extract
entirely** — it contains no `cd_` command strings and no console-registration routine — so a
developer console command could still send it.

**Open question.** If a `cd_`-style developer command exists, `0x15E` is reachable from a console and
this handler should say so rather than claim no sender. Neither this document nor the reviewer who
suggested such a command can be settled from the artifacts available here.

## Blocked remainder

1. **Subzone → farm-area binding is still the pre-existing hardcoded map, and it is known-LOSSY.**
   The farm-area lookup (`sub_zones` → farm group) has no typed link to a `common_farms` row, and
   this has been confirmed rather than assumed. In the shipped content:

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

2. **The three request packets mutate nothing, by evidence rather than by omission.** They parse and
   log. Wiring a real behaviour to any of them needs a demonstrated 10.x sender first — which is the
   same open question recorded for `0x15E` above.

3. **The show-area response is implemented but not reachable.** `SC 0x220` is written to the real
   wire and unit-pinned, but nothing calls it, because `CS 0x15E` has no confirmed sender.

4. **Harvest / item-return semantics are out of scope.** Returning a crop is the separate
   `DoodadFuncFinal` interaction path and is not touched here. It is worth recording that the
   withdrawn `0x163` behaviour would have deleted crops **without** returning items.

## Verification

- `CS 0x164` **parser** tests: consumes the whole point loop; zero count carries no points; negative
  (signed) count carries no points; a hostile count is bounded by the wire clamp; a single point
  matches a one-entry request. These read real bytes and assert real values.
- `SC 0x220` **writer** tests (9): zero count is an 8-byte header with no positions; one count emits
  exactly one 11-byte position; `N` counts emit exactly `N` positions at the documented stride; a
  negative count is refused rather than looped on; a count past the available positions is bounded;
  a hostile count is bounded by the wire clamp. Position bytes are compared against the shared
  quantized-position block, so the test fails if the writer stops using the standard serializer.
  Both the loop and the negative-count guard were mutation-checked: disabling the loop fails 4 of
  the 9, accepting a negative count fails 2.
- `PublicFarmRequestSurfaceTests` (2): the farm manager interface exposes exactly
  `{PublicFarmTick, InPublicFarm, GetFarmType}` and no `int`-returning member. **Mutation-checked
  faithfully** — re-adding the bulk delete to *both* the interface and the implementation fails
  2 of 2. (An earlier mutation that touched only the interface did not compile and therefore proved
  nothing; that attempt is recorded because the meaningless result is the easy one to report as a
  pass.)
- Full unit suite passing — exact count in the task result.
- `git diff --check` clean. **BOM parity 0 drift measured in both directions**, binary-safely
  against base: `.cs` is the only extension where `.editorconfig` demands a BOM and the tree mostly
  lacks one (34.2% have one), so parity with base — not "always add a BOM" — is the rule.

## Live acceptance still required

Per the five-gate testing protocol, this slice is verified at the unit/wire level only. Before it
can be called done it still needs the live gates: a demonstrated sender, the request on the wire,
the server-side effect and a persisted doodad row, a client-visible result, and a clean-state
restart. **No live acceptance run has been performed on this slice.**
