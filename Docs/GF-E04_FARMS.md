# GF-E04 — public-farm placement / removal / show-area

Scope: the three named operations of the row — **place**, **remove**, **show-area** — on the shipped
10.0.2.13 client, plus the protection state they have to stay consistent with.

Base for this work: `client_version/zone-10.0.2_r575`.

## The premise, checked

The row text says placement and removal requests are no-ops. That is **not what the base contains**,
and the difference matters, because it changes which packet each operation lives on.

| Operation | The request that carries it | State in the base before this slice |
|---|---|---|
| **place** | `CSCreateDoodadPacket` (CS `0x131`) | **Live.** Gated by `PublicFarmManager.CanPlace`. |
| **remove** | `CSRemoveCommonFarmsPacket` (CS `0x163`) | Parsed, deliberately not acted on. |
| **show-area** | `CSShowCommonFarmAreaPacket` (CS `0x15E`) | Parsed, answered with nothing. |
| *list* | `CSRequestCommonFarmList` (CS `0x15F`) | Live, and the only farm request the shipped client actually sends. |

So the row's "placement is a no-op" was already false: planting a crop goes through the ordinary
doodad-create request and the farm manager gates it. What was missing was the **validation around
it**, the **removal path**, the **show-area answer**, and **a protection window that decides who may
take a crop rather than how long one lives**.

## What the shipped client actually sends

Three findings, each of which corrects something the earlier slice of this row asserted.

### `0x15F` is real, and it is the live farm request

`CSRequestCommonFarmList` carries a single `u32` farm tab and no other field. The client binds it to
the script call `X2:RequestRuntimeCommonFarmDoodadInfo()`, which the farm window issues when it is
shown, and the answer is `SCResponseCommonFarmListPacket` (`0x221`).

The request is rate-limited on the client to one send every two seconds, and it is refused outright
inside an instant zone, where the client reports its own reason and applies nothing. The refusal is
carried by the answer's leading count: a leading total of zero means "no list is available here".

This packet is absent from the project's protocol catalog because a catalog row is emitted per packet
*with a receive channel*, and a client-to-world packet has none by construction. Its absence from that
list said nothing about it.

### `0x163` and `0x164` are not sent by the shipped client

`CSPlaceCommonFarmPacket` (`0x164`) and `CSRemoveCommonFarmsPacket` (`0x163`) exist in the client's
packet set, but nothing in the client calls either sender. Both bodies match the farm shape-editor
pair byte for byte in shape — `0x161`/`0x162` place and remove area spheres and area shapes, and
`0x163`/`0x164` place and remove farm shapes with an empty body and a point list respectively.

**Correcting the earlier slice on its own reasoning.** That slice withdrew the handlers citing "the
raw schema records no handler, so nothing in the retail server reads these opcodes". The `handler`
field of a catalog row is a *client receive* handler. Every client-to-world row has a null one — 460
of 460 in the client's packet set and 479 of 479 in the zone build's — so the check selects nothing
and proves nothing in either direction. The conclusion survives on the two sound grounds above (no
caller, and the shape-editor pairing), not on the third.

### `0x15E` does not exist in the shipped client

There is no `0x15E` farm-area request class in the client's packet set at all; it is present only in
the zone build's. Nothing in the client script extract sends it either. **A shipped client therefore
cannot ask for a farm area** — the answer has to be pushed by the server. The request handler is
kept and now answers, because the writer needs one reachable caller and a console or tool that
speaks the opcode is a legitimate one; the deployment note is that the normal path is a push.

## Wire shapes used

`SCShowCommonFarmPacket` (`0x220`) — `u32 type`, **signed** `s32 count`, then `count` quantized
world positions, 11 bytes each. Two reader properties shape what is written:

- a count that is **not positive makes the reader drop the positions it was holding**, so a zero
  count is not an empty answer, it is the way an area is cleared;
- the reader honours at most **128** entries however large the count says.

`SCResponseCommonFarmListPacket` (`0x221`) — `u32 maxCount`, **signed** `s32 count`, then that many
crop records. One record is `u32` tab, `u32` doodad template, `u32` growing time, `u32` current
phase, the shared quantized world position, and a `u64` planting time. The reader honours at most
**64** records, and a leading total of zero is its "not available here" refusal.

`CSCreateDoodadPacket` (`0x131`) — `u32` template, position, `f32` z-rotation, `f32` scale,
`u64` item. The farm gate sits behind this and nothing else.

## What this slice changes

| Area | Before | After |
|---|---|---|
| Placement ordering | capacity before the allowed list | allowed list first, because it is the only question answerable without a number content may not have supplied |
| Unconfigured farm | refused **and told to the player the farm is full** | planted with no limit, the gap logged loudly once per tab, **nothing sent to the player** |
| Protection window | missing `doodad_groups` row answered as `0` | missing row reported as unknown, and a crop whose age cannot be measured is treated as untakeable rather than retired |
| Crop lifetime | a per-minute pass deleted every crop past its protection window | **no pass**: the window is a harvest permission, so an aged crop is unprotected, not gone |
| Show-area | no answer at all | answered: refused off the farm, cleared for a different tab, answered for the requested tab, with a zero count doing the clearing |
| Removal | none | a single-crop removal that refuses a foreign crop and a crop inside its window, and says which |
| Farm list | count written straight from the crop total | count and records come from one flattened list, bounded at the reader's 64 and reported when reached |

## The guard-direction trap, and where each guard falls

This codebase has been bitten repeatedly by a guard written in the safe-looking direction that is
actually wrong. The four that matter here, and the side of each line they sit on:

- **Allowed list before capacity** — sits on the *content* side. It is answerable for every tab,
  including a tab with no capacity row, so a content gap can no longer be reported in place of the
  real reason a crop was refused.
- **Unknown capacity is not zero capacity** — sits on the *missing-data* side. Answering a missing
  `farm_groups` row with `0` made a farm that accepts one crop and then refuses for ever.
- **The protection window is a permission, not a lifetime** — the one that mattered most, because
  getting it backwards deletes crops rather than refusing a placement. `CropHarvest` and
  `DoodadFuncUse` use `guard_on_field_time` only to decide who may take a crop, and the client's farm
  list shows an aged crop as *unprotected* rather than absent. So nothing retires a crop for age, and
  there is no periodic pass at all. Reading the window as a lifetime is not a narrow bug either:
  **103 of the 110 shipped `doodad_groups` rows carry `guard_on_field_time = 0`**, so it would have
  cleared almost the whole field a minute after planting.
- **Unknown protection window is not an empty protection window** — also on the *missing-data* side.
  A missing `doodad_groups` row means the length is unknown, and a crop whose age cannot be compared
  to anything is not known to be harvestable, so it is treated as untakeable rather than free.
- **A configured `0` window is still a configured window** — on the *other* side of the same line.
  Zero is a real content value meaning "no protection"; only a missing row is unknown, and the two
  are reported differently so an unprotected group is not mistaken for an unknown one.

## Open, not claimed

- The subzone-to-farm-tab map in `PublicFarmManager.Load()` is still the pre-existing hardcoded
  five-entry map, and it is known-lossy: four of the five subzones are content-indistinguishable and
  the map gives them four different tabs. It is not touched here, and no new ids were added.
- `common_farms` has 46 authored rows across just three names and no column that joins a farm area
  to a subzone. Making the area lookup content-driven needs a typed link in content or an explicitly
  reviewed decision, not a name match.
- Two of the four farm tabs (Nursery and Ranch) list allowed doodads in content — six each — but
  carry no `farm_groups` row, so no capacity was ever authored for them. Refusing those tabs made
  them unplantable while still stating no size, and reading the missing row as zero is what made the
  farm take one crop and then refuse for ever. Neither answer invents a number, so the count check
  is skipped for a tab with no authored size, the gap is logged once per tab, and the player can
  plant. **The cost is deliberate: until content carries a size, a Nursery or Ranch accepts any
  number of crops.** Inventing `5` to match the two tabs that do have rows would be a shipped value
  in C#, which is not permitted, so the looseness is the honest answer and the log line is where the
  gap stays visible.
- The `u64 planting time` in a farm-list record is written as the stored timestamp. The reader
  compares it against a file-time clock, so the epoch is worth confirming against a capture before
  the list is called correct end to end. Not changed here: nothing measured says it is wrong.
- Live acceptance has not been run on this slice. Per the five-gate protocol it is verified at the
  unit and wire level only.

## Verification

- `CommonFarmPlacementRules` — unconfigured capacity places at every planted count, an unconfigured
  tab shown to ignore a capacity it was never given, "no capacity row" pinned apart from "capacity
  reached", the capacity boundary asserted on both sides, the allowed-list ordering asserted against
  both an unconfigured and a full tab, and every enum member swept as reachable.
- `CommonFarmShowAreaRules` — each of the three outcomes asserted from both sides, the invalid tab
  and the off-farm position both refused, the response shown to name the land rather than the
  request, and every farm tab swept as answerable for itself.
- `CommonFarmListRules` — inside the bound, exactly at the bound, one past it, a hostile count against
  a short list, a negative count refused, and an empty list distinguished from a truncated one.
- `PublicFarmRequestSurfaceTests` — the interface is pinned to the four named operations, no member
  returns an `int`, **no member takes a collection of doodads**, the removal takes exactly one
  doodad, and the manager takes no task manager at all. The last two are the standing guard against
  the bulk delete this row once had, and the last is the guard against the expiry pass coming back:
  with no scheduler injected there is nothing for a periodic crop-deletion pass to run on.
- `CommonFarmGameDataTests` — both missing rows reported as unknown rather than as zero, and a
  configured zero window reported as configured rather than unknown.
