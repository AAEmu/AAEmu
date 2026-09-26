# GF-W05B — NoKillMin metadata slice

## Scope

The daily war-window half of GF-W05 is already implemented by the merged zone-window work. This slice audits the remaining `conflict_zones.no_kill_min_0` through `no_kill_min_4` values and exposes them as typed, validated metadata.

It deliberately does **not** apply a decay transition to `ZoneConflict`.

## Evidence

The read-only 10.0.2.13 content databases contain 33 `conflict_zones` rows. Every `no_kill_min_0..4` value is zero in the shipped compact and client databases. The same databases contain the daily `war_st_hour/min_*` values, but no nonzero NoKillMin row exists to exercise a decay rule.

The available native, client, and research material does not establish:

- which event starts the inactivity interval;
- whether the value is measured in minutes, or in another state-specific unit;
- whether the target is a lower trouble state, Tension, or a timed state;
- whether the timer is armed, cancelled, or reset by kills, schedule boundaries, or a state transition.

Applying a guessed rule would violate the project's content and evidence rules.

## Implementation

`ConflictZoneNoKillDecayMetadata` loads all five required columns without defaults, rejects a wrong shape or negative value, preserves the authored values, and produces a diagnostic string. `ZoneManager` binds one metadata object per loaded conflict zone and fails startup if a conflict row was not bound. `ZoneConflict.GetNoKillDecayDiagnostic()` refuses to invent a default when metadata is absent.

`IsConfigured` is diagnostic only. It does not cause a state transition. The existing daily-window reset path is unchanged.

## Follow-up gate

A future behavioral slice must first provide evidence for the decay trigger, unit, target state, and reset/cancel rules. It should then add deterministic tests using authored nonzero content or a documented test fixture before changing the state machine.
