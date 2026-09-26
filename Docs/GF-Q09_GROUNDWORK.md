# GF-Q09 groundwork

## Scope

This slice adds only a typed, content-backed descriptor catalog for
`doodad_func_spawn_slave_after_get_items`. The catalog is loaded during the
normal doodad-function load and exposes the table's descriptor id, item id,
delay, and the three authored placement values.

The descriptor is deliberately not a `DoodadFuncTemplate`. It does not grant an
item, resolve a slave, schedule work, choose a frame, place a unit, create a
slave, or apply a lifetime. No Q09 runtime behavior is enabled by this patch.

## Content and validation

The loader reads the shipped compact table with an explicit column list. It
fails startup for a missing table, an empty table, duplicate descriptor ids,
null required fields, non-positive ids, negative delays, non-finite values, and
values outside the supported primitive ranges. The shipped compact contains
an evidenced descriptor row linked to the Q09 function and to the summon-slave
item catalog; the unit test fixture uses synthetic values so no shipped id is
embedded in source.

## Full Q09 remains evidence-blocked

The descriptor is not a claim that the full Q09 flow is complete. The following
runtime questions remain unresolved and are deliberately out of scope:

- The authoritative source frame for the authored offset and angle, including
  whether the values are interpreted in the doodad's local, world, or another
  runtime frame.
- The atomic compensation protocol across item grant, delayed scheduling, slave
  creation, persistence, cancellation, and spawn failure. A later runtime
  implementation must prove that each failure path restores ownership and item
  state without duplication or loss.
- Live ownership, placement, visibility, persistence, relog, and cleanup
  behavior.

Until those frame and compensation questions have independent evidence and a
live acceptance pass, this branch must be treated as groundwork only.
