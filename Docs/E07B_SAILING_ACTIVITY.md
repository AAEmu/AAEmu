# GF-E07B — Sailing activity packets and content

The event-center *sailing* activity (`航海活动`). This is not the vehicle/slave system: nothing
here touches `slaves.portal_time`, ships, or any of the vessel research.

## Wire

Verified against the raw serializer schema. The derived `_packet_structs_*.json` files drop guarded
loops and mistype signedness, so the raw `ir` array is the authority used here.

| Dir | Opcode | Class | Body |
|---|---|---|---|
| CS | 0x212 | `CSSailingActivityEnterPacket` | s32 `activityId` |
| CS | 0x213 | `CSSailingActivityLeavePacket` | s32 `activityId` |
| CS | 0x214 | `CSSailingActivityClaimRewardPacket` | s32 `activityId` + container |
| CS | 0x215 | `CSSailingActivityRequestDataPacket` | s32 `activityId` |
| SC | 0x38D | `SCSailingActivityStageUnlockedPacket` | s32 `activityId` + container |
| SC | 0x38E | `SCSailingActivityClaimRewardResponsePacket` | s32 `activityId` + 3 containers |
| SC | 0x38F | `SCSailingActivityErrorPacket` | s32 `activityId`; s32 `errorCode` |
| SC | 0x390 | `SCSailingActivityEnterResponsePacket` | s32 `activityId`; s32 `errorCode` |
| SC | 0x391 | `SCSailingActivityListPacket` | s32 `activityCount` + N × { s32 `activityId`; u64 `startTime`; u64 `endTime` } |
| SC | 0x392 | `SCSailingActivityPointsChangedPacket` | s32 `activityId`; s32 `totalPoints` |

`CS 0x214` and `SC 0x38D`, `0x38E`, `0x391` are new. The three packets that already existed carried
their `activityId`, `errorCode` and `totalPoints` as `uint`; the raw schema marks all of them
`signed`, so they are now `int`. Nothing constructed them before this change, so no caller moves.

**The signedness is not cosmetic.** An `errorCode` that is negative in content would arrive as a
four-billion value through an unsigned field. The derived catalog summarises these same fields as
`u32`; where the two disagree, the raw schema wins.

## Not recovered, and not guessed

### Two opcodes

`SCSailingActivityDataPacket` and `SCSailingActivityTaskProgressPacket` exist in the client's type
table. Both have `opcode: null` — in *both* independent extractions. Their bodies are recovered (a
signed `activityId` followed by **ten** and **nine** containers respectively); their slots are not.

The known sailing slots run 0x38D–0x392, which leaves three one-slot gaps. Filling those by
counting is adjacency arithmetic, not an extraction: it assumes the run is contiguous, that nothing
was inserted between the packets, and that the two bodies sit in the order their vtables happen to
be laid out. A wrong guess is an opcode sent to a real client, so neither constant is filled in.
They live in `SailingActivityUnresolvedOpcodes` as named placeholders, the same treatment the
team-joint slice gave its unknown wire mode.

### The container element layout

`CS 0x214`, `SC 0x38D`, `SC 0x38E` and the two unresolved packets all carry the same generic vector
helper, whose element type is never named. Neither the element layout nor its width is known, so
`SailingActivityContainer` **has no API that builds a container from decoded elements.** A server
cannot author elements it cannot describe, and inventing a layout would put fabricated bytes on the
wire. The only way to get a non-empty container is `FromRaw`, which round-trips bytes that already
exist; `ReadRemainder` consumes the trailing bytes verbatim and logs a warning.

The read side is sound only because the container is always last: once the leading fields are
read, every remaining byte belongs to it, so the parser cannot desynchronise.

**This helper is not sailing's alone.** Seven packets call it — the five above plus
`SC 0x393 SystemFeatureStateList` (two containers) and `SC 0x394 SystemFeatureStateChanged` (one).
The type is named for sailing only because sailing is what this slice implemented. In all seven
packets today the container is the sole or the final element, which is what makes the "read the
remainder" approach valid. Before this class is reused for the 0x39x pair, re-derive that for those
two specifically: a container in the *middle* of a body would need a length prefix, and the
extraction exposes none.

## Content

`SailingActivityGameData` loads three tables:

- **`game_activities`** — the id the packets carry as `activityId`, plus `status`, `time_mode`,
  `start_time`, `end_time`, `server_groups` and `task_group_id`.
- **`game_activity_stages`** — 5 rows, ids 4–8, `unlock_mode` 3, `time_mode` 2,
  `unlock_time` `0`/`1`/`5`/`10`/`15`, `prerequisite_stage_id` 0.
- **`game_activity_tasks`** — 41 rows with `task_type`, `condition_type_id`, `reward_points`,
  `is_point_reward` and a `type|item|count` rewards cell, parsed by the same helper the plot-auction
  slice uses.

Activity ids, stage numbers, unlock values, condition kinds, task kinds, point values and reward
items all load through these tables. No whitelist is applied to `condition_type_id` or `task_type`:
the shipped sets are wider than any documented one (conditions run 29–33, 301–333 and 1001–1003;
task types run 1–7) and the meaning of each value is not recovered, so they are carried verbatim.

### Activity windows: two of four resolve

`game_activities` writes its window in two shapes. Activities 1 and 1001 use the self-describing
`year|month|day|hour|minute` form. Activities 2 and 3 write a bare integer — `0`/`50` and `0`/`14`.

A bare integer states no unit and no reference point: whether it counts hours, days or stages, and
whether it runs from server start, the client's local clock or the moment the panel opens, is not in
this table. So `SailingActivityWindow` refuses it, records the reason in
`Diagnostics.UnresolvedWindows`, and leaves `StartUtc`/`EndUtc` null. The five-part form needs no
assumption about what `time_mode` is called, because the value describes itself — which is why it is
accepted and the integer form is not.

### Stage unlock values are not instants

`unlock_time` is text and every shipped row is a bare number. The loader keeps the raw string and
exposes it as a non-negative integer, and **refuses** a value that is not a number or that is
negative — quietly nulling an authored threshold would make a stage unlock silently never fire. It
derives no instant: what the number counts is not recovered, and `SailingActivityStageRow` has no
`DateTime` on it at all.

## Shipped content is inconsistent, and is reported rather than repaired

**Orphan stage references.** Three task rows name stages that do not exist — two point at stage 1,
one at stage 2, while `game_activity_stages` only has 4–8. A naive fail-loud orphan check would
refuse to boot over an authoring mistake. Each is reported through `Diagnostics.Orphans` and
error-logged, the rows are still loaded, and loading continues.

Stage 0 is **not** an orphan. The column defaults to 0 and every stage uses 0 for "no prerequisite",
so a task with no stage is a normal shipped state. Five tasks are in that state.

**The sailing activity's own task group reaches no stage.** Activity 1 (`航海活动`) selects
`task_group_id` 1, and all three of its tasks point at stages 1 or 2 — both missing. The five stages
that do exist are reached by activity 3 (`启航活动`), not by the sailing activity. The selection is
still exact; it simply lands nowhere. This slice reports the shape and does not repoint it: which
group the sailing activity was *meant* to select is an authoring question, not something to guess at.

**Genuinely malformed rows still fail the load.** A rewards cell that is not `type|item|count`, a
duplicate id, and an `unlock_time` that is not a non-negative integer all stop startup, because a
server that silently drops a reward or an unlock threshold is worse than one that will not start.
Zero is accepted: it is what the shipped table uses for the first stage. A window that ends before
it starts is reported as unresolved rather than accepted.

Three of the 41 shipped reward cells are empty. That is legal content and the loader accepts it;
what an empty cell means for any particular task is not asserted here, because it is not the same
claim as "the cell is well formed".

## Not implemented

`SCSailingActivityListPacket.MaximumRows` is a defensive serialization bound chosen by us, not a
client limit — nothing in the extracted schema states a maximum for that count. It only stops a
runaway row array from producing an absurd frame.

No packet handler, no entry/leave state machine, no stage unlock evaluation, no task progress
tracking, no point accrual, and no reward payout. Each of those needs the element layouts, the
condition-kind meanings and the unlock units that are listed above as unrecovered, and none of them
are guessed. The packets parse and encode; nothing routes them yet — the same posture the three
pre-existing sailing packets have always had.

## Verification

Full TUnit suite, and byte-exact encode assertions for every packet body including the signed error
code and the list encoding. Both content databases (runtime `compact.sqlite3` and the client
`game.sqlite3`) were opened read-only and agree exactly on all three tables.
