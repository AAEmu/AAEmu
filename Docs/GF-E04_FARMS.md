# GF-E04 — public-farm placement / removal / show-area

Scope: the three named operations of the row — **place**, **remove**, **show-area** — on the shipped
10.0.2.13 client, plus the growth/expiry state they have to stay consistent with.

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
it**, the **removal path**, the **show-area answer**, and **correct expiry and restart behaviour**.

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
| Unconfigured farm | refused **and told to the player the farm is full** | refused, logged loudly, **nothing sent to the player** |
| Protection window | missing `doodad_groups` row answered as `0` | missing row reported as unknown; the crop is kept, never retired on a length nobody wrote |
| Expiry | row re-saved as a system doodad; the doodad stayed in the world and on screen, and the row was never read again | the crop is deleted — world object, client object and row together — so a restart cannot resurrect it |
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
- **Unknown protection window is not an empty protection window** — also on the *missing-data* side.
  Answering a missing `doodad_groups` row with `0` retires a crop the moment it is planted, which is
  the exact inverse of what the field is for. A crop of unknown age is kept, and the gap is logged.
- **A configured `0` window is still a configured window** — on the *other* side of the same line.
  Zero is a real content value meaning "no protection"; only a missing row is unknown. A lookup that
  treated the value as its own truthiness would report both as the same thing, and the first crop of
  a genuinely unprotected group would be kept forever.

## Open, not claimed

- The subzone-to-farm-tab map in `PublicFarmManager.Load()` is still the pre-existing hardcoded
  five-entry map, and it is known-lossy: four of the five subzones are content-indistinguishable and
  the map gives them four different tabs. It is not touched here, and no new ids were added.
- `common_farms` has 46 authored rows across just three names and no column that joins a farm area
  to a subzone. Making the area lookup content-driven needs a typed link in content or an explicitly
  reviewed decision, not a name match.
- Two of the four farm tabs have allowed doodads in content but no capacity row and no authored farm
  area. Placement there is refused, loudly, rather than guessing a size. This is the inherited
  slice's fix, kept.
- The `u64 planting time` in a farm-list record is written as the stored timestamp. The reader
  compares it against a file-time clock, so the epoch is worth confirming against a capture before
  the list is called correct end to end. Not changed here: nothing measured says it is wrong.
- Live acceptance has not been run on this slice. Per the five-gate protocol it is verified at the
  unit and wire level only.

## Verification

- `CommonFarmPlacementRules` — unconfigured capacity refused at every planted count, the two refusals
  pinned apart, the capacity boundary asserted on both sides, the allowed-list ordering asserted
  against both an unconfigured and a full tab, and every enum member swept as reachable.
- `CommonFarmExpiryRules` — an unknown window keeps the crop at every age, a configured zero window
  still protects the planting instant, a crop with no planting time is kept, and the two
  "keep" reasons are shown to be different values.
- `CommonFarmShowAreaRules` — each of the three outcomes asserted from both sides, the invalid tab
  and the off-farm position both refused, the response shown to name the land rather than the
  request, and every farm tab swept as answerable for itself.
- `CommonFarmListRules` — inside the bound, exactly at the bound, one past it, a hostile count against
  a short list, a negative count refused, and an empty list distinguished from a truncated one.
- `PublicFarmRequestSurfaceTests` — the interface is pinned to the five named operations, no member
  returns an `int`, **no member takes a collection of doodads**, and the removal takes exactly one
  doodad. The last two are the standing guard against the bulk delete this row once had.
- `CommonFarmGameDataTests` — both missing rows reported as unknown rather than as zero, and a
  configured zero window reported as configured rather than unknown.
