# GF-W09B — event-center runtime state

## Scope

W09A (`feat/w09a-event-row-projection`, PR #1725) projects every `game_schedules` row into a typed
read-only description. W09B adds only the runtime state that sits on top of that projection:

- the row or rows that are **current** at a wall-clock instant;
- the earliest future **upcoming** row set;
- the next boundary at which that classification changes;
- a start-inclusive/end-exclusive transition and a restart reconstruction.

W09B does not repeat the W09A row projection, content-gap tallies, or `/eventcenter_rows` diagnostics.

## No board wire change

W09A established that no content source exists for the board title, body, link, reward block, or main
order. Therefore `IsWireReady` is false for every projected row. W09B deliberately does **not** implement
or send `SC 0x2DE`; the existing count/empty answers remain correct for a server that publishes no board
rows. The runtime manager is internal state for a future wire slice and a read-only diagnostic.

## Runtime rules

A projected row is runtime-eligible only when W09A resolved a positive-length period and the row has no
schedule-shape gap. A resolved period is half-open:

```text
start <= now < end   => current
now < start          => candidate upcoming
end <= now           => ended
```

`upcoming` is the set of eligible future rows sharing the earliest future start. A tie is preserved. The
next boundary is the earliest current end or earliest future start, both taken from projected rows. There
is no polling interval, look-ahead window, or hardcoded time bound. The manager arms exactly one one-shot
task for that boundary, re-evaluates, and arms the next projected boundary.

The schedule generation and retained handle are updated under the schedule gate before any task-manager
call. Refresh plus logical replacement planning is serialized under that gate; cancel/schedule calls happen
after the gate is released. A task that no longer owns the current generation is a no-op, so an interleaved
stale task cannot clear or replace a newer chain.

Rows with a recurring time-of-day window or weekday filter are deferred as `RecurringOccurrence`. This is
intentional: whether a recurring row is listed once or expanded per occurrence is a product question that
W09B does not answer and does not guess. Rows with unresolved or malformed periods are likewise deferred.

## Restart replay

The runtime has no persisted row state. On boot/restart it rebuilds the W09A catalog from loaded
`game_schedules` and evaluates it at the current UTC instant. A fresh reconstruction therefore produces
the same current/upcoming/boundary view as an uninterrupted process that refreshes across the same
instants. Tests pin this equality.

## Diagnostics

`/eventcenter_runtime` is read-only. It prints the evaluation instant, counts of current/upcoming/deferred/
ended rows, the next projected boundary, and the ids/bounds of the current and next-upcoming rows. It does
not refresh the state, change the catalog, schedule a task, or send a board packet.

## Deferred follow-ups

- Establish content sources for title, body, link, reward, and ordering before implementing `SC 0x2DE`.
- Decide once-versus-per-occurrence semantics for recurring rows.
- Verify the runtime transition output against a live board once a wire slice is evidence-backed.
