# W09A — event board row projection (rows → runtime projection + diagnostics)

Status: W09A feature branch; this slice is intentionally read-only and diagnostics-only.
Base: `2c19b936` (`client_version/zone-10.0.2_r575`).

This slice is deliberately the small one. It makes the event board's content situation
**measurable and typed** and nothing else: it projects content schedule rows onto the
fields a board entry needs, records exactly which fields have no content source yet, and
refuses to invent the rest. It sends no board packet, keeps no board state and performs
no schedule transition.

## What the ledger asked for

GF-W09 is "implement the generic Event Center": ~922 schedule rows exist, but the board
requests answer empty instead of listing events. The row was marked partial because the
merged board code answers the two request opcodes with a count and an empty packet and no
event row ever reaches the client.

## What already exists (re-audited, unchanged by this slice)

| Piece | Where | What it does |
| --- | --- | --- |
| `SCEventInfoCountPacket` (`0x2DD`) | `AAEmu.Game/Core/Packets/G2C/SCEventInfoCountPacket.cs` | head of the board: `u32 count`, `u64 loadedTime` |
| `SCEventEmptyPacket` (`0x2DF`) | `AAEmu.Game/Core/Packets/G2C/SCEventEmptyPacket.cs` | bodyless clear |
| `CSRequestEventInfoCountPacket` (`0x1B7`) | `AAEmu.Game/Core/Packets/C2G/CSRequestEventInfoCountPacket.cs` | answers `0x2DD` with the real count and load time |
| `CSRequestEventMainInfoPacket` (`0x1B8`) | `AAEmu.Game/Core/Packets/C2G/CSRequestEventMainInfoPacket.cs` | answers `0x2DF` — a server that runs no board events owes exactly this |
| board wire tests | `AAEmu.UnitTests/Game/Core/Packets/G2C/EventBoardWireTests.cs` | pins the count/empty pair |

The count is therefore already real: it counts what the projection would publish, which
today is zero.

## The entry row's shape, and what is proven about it

The board entry is one `s32` main order plus three sub-structs. Their field names come from
the client's own serializer, and the widths are the wire contract:

| Sub-struct | Fields |
| --- | --- |
| window | `u64 startTime`, `u64 endTime`, `string title` (≤ 1200), `string link` (≤ 511) |
| body | `string body` (≤ 1600) |
| reward | `s32 exp`, `s32 money`, `s32 aaPoint`, `s32 laborPower`, `s32 honor`, `s32 crime`, `s32 living`, `u32 type`, `s32 count` |

The client window reads exactly those fields: `info.startTime`/`info.endTime` drive the
period bar, `info.title`/`info.body`/`info.link` the text, and `info.exp`/`info.money`/… the
reward block. `info.state` is **not** a server field — the window derives
in-progress / scheduled / ended itself from the two timestamps.

## Content evidence (read-only compact, 927 `game_schedules` rows)

`game_schedules` is the only table shaped like an event period, and it is already loaded by
the schedule game data into `GameSchedules`. Read across all 927 shipped rows, each row
lands in exactly one of these five outcomes, so the column sums to 927 once:

| Period outcome | Rows | What the projection does with it |
| --- | ---: | --- |
| resolves, no time-of-day window | 547 | projects the row's own UTC instants |
| resolves, with a time-of-day window | 240 | projects the instants and reports `RecurringWithinPeriod`; the pair is the envelope of many daily occurrences |
| no complete period (`0/0/0` or `0/1/1` on either bound) | 133 | reports `IncompleteCalendarPeriod` |
| end at or before start | 7 | reports `EndNotAfterStart` and drops the instants |
| malformed date or clock component | 0 | reports `MalformedCalendarBound` |

547 + 240 + 133 + 7 = 927. The seven rows whose period does not move forward are ids
14, 15, 271, 679, 680, 980 and 981. No shipped row has an out-of-range time-of-day window,
so `MalformedDailyWindow` never fires on this content (0 rows); it exists for a content
fault, not for this table.

Repeating is a second, **overlapping** dimension and is tallied separately, so it does not
appear in the table above:

| Repeating filter | Rows | What the projection reports |
| --- | ---: | --- |
| has a time-of-day window | 291 | `RecurringWithinPeriod` (240 of them resolve; 51 sit in the non-resolving rows above) |
| limited to one weekday, no time-of-day window | 12 | `RecurringByWeekday` — 4 also resolve, 1 also ends before it starts, 7 also have no complete period |

A weekday filter and a time-of-day window are independent and both can be present: 81 rows
carry a weekday filter, of which 12 are all-day and 69 also carry a time-of-day window.

One shipped row states its end bound as `24:00`, which is the end of that day rather than an
invalid hour, so the projector reads it as the following midnight and reports anything past
it instead of clamping.

For the four board text/reward fields the audit found **no** source, and the projection
therefore reports them rather than filling them:

- the schedule row's own `name` is an author display string with **no** `localized_texts`
  row, so nothing proves the retail board used it as the entry title;
- no table carries a board body, a board web link or a board reward block;
- no table ranks board rows, so the main order is server state with no content source.

The projection consequently reports `MissingTitleSource`, `MissingBodySource`,
`MissingLinkSource`, `MissingRewardSource` and `MissingMainOrderSource` on **every** row, and
`IsWireReady` is false for every row. That is the honest end state of this slice.

## What this slice adds

| File | Role |
| --- | --- |
| `AAEmu.Game/Models/Game/EventCenter/EventCenterRowProjection.cs` | the typed row projection and the gap flags |
| `AAEmu.Game/Models/Game/EventCenter/EventCenterRowProjector.cs` | the projection rules, reading only the row's own columns |
| `AAEmu.Game/Models/Game/EventCenter/EventCenterRowCatalog.cs` | all rows projected, with a gap tally |
| `AAEmu.Game/Scripts/Commands/EventCenterRows.cs` | read-only `/eventcenter_rows` diagnostics, optional gap filter |
| `AAEmu.Game/Core/Managers/GameScheduleManager.cs`, `IGameScheduleManager.cs` | a read-only view of the loaded schedule rows |
| `AAEmu.UnitTests/Game/Models/Game/EventCenter/EventCenterRowProjectionTests.cs` | one test per shipped row shape, plus catalog aggregation and the duplicate-id guard |

`/eventcenter_rows` prints the row count, how many resolved a period, how many are writable,
and the per-gap tally; with a gap name it lists the rows carrying it. It changes no state and
sends nothing to a client.

## Deliberately left for the next slice

1. **The entry sender.** Writing `0x2DE` needs a content source for title, body, link and
   reward, plus a rule for the main order. Until those exist the board keeps answering
   `0x1B8` with `0x2DF`, which is the correct answer for a server that runs no board events.
2. **Schedule transitions and board runtime state.** Which rows are current/upcoming, when
   the list changes at a start/end boundary, and what a restart replays are all a second
   slice: they need the current/upcoming decision to be made against the same row set and
   the client to be told when it changes.
3. **Occurrence expansion.** A row with a time-of-day window or a weekday filter is a set of
   occurrences; whether the board lists the row once or once per occurrence is a product
   question this slice does not answer, and it must not be guessed from the row count.
