# GF-W07 — Conflict spawner switching

This slice completes the peace/war spawner toggle that the merged conflict escalation
(GF-W04 #1650), daily war windows (GF-W05 #1666) and runtime persistence (GF-W06 #1672)
left unwired, using only the shipped `conflict_zone_npc_spawners` content.

## What the base already had

- `ConflictZoneGameData` loads `conflict_zone_npc_spawners` rows
  (`npc_spawner_id`, `zone_state_kind_id`, `spawn_activate`, `use_despawn`) keyed by
  `conflict_zone_id`, plus the realtime schedules and participation lists.
- `ZoneConflict.PublishState()` publishes every transition to
  `WorldIntegration.RelayConflictZoneStateToZone`, and `Program.cs` fans that out to every
  `ZoneLoaded` host in the group as a `WZConflictZoneState` (0x084). `Program.cs` also
  replays the same packet on `ZoneLoaded` / reconnect through
  `NotifyZoneReadyForConflictZone`.
- The two state enums are content-grounded and unchanged:
  `ConflictZoneStateKind` = none/peace/war (`enum_conflict_zone_state_kinds`) and
  `ZoneConflictType` = trouble_0..trouble_4 / battle / war / peace
  (`enum_honor_point_war_states`, war=6, peace=7 — the same `hpws` byte on the wire).

## The gap (why the base did not toggle spawners)

The Zone host receives 0x084 and stores the war state for its own unit/skill requirement
evaluators (war-vs-peace gating). It does not arm the conflict spawners from that state.

So the `Program.cs` comment "hosts arm their own conflict_zone_npc_spawners placements" is
incorrect: peace/war spawners did not visibly toggle. World owns the toggle, and the war
state still has to ride 0x084 so the host's requirement checks keep working.

## What this slice adds

`ConflictZoneSpawnerRules` (AAEmu.Game) — pure, content-driven:

- `ResolveStateKind(ZoneConflictType)` → the spawner-state kind. `War`→`War`, `Peace`→`Peace`,
  every escalation step (tension…conflict, battle)→`None` (those states carry no spawner
  rows in the shipped content, so no fallback is guessed).
- `ResolveActions(...)` → the per-placement `ConflictZoneSpawnerAction` set for the state's
  own kind, each carrying its own `spawn_activate`/`use_despawn` flags, sorted by placement id
  so a republish is byte-identical.
- `BuildPlan(...)` → the complete toggle: the arm set and the retire set. Within the active
  state a row is honoured verbatim (a `spawn_activate=false` row is an *explicit
  deactivation*, the ledger's specific W07 case). The **complementary** state's rows are
  retired when it is left — an armed row is deactivated, a suppressed row is re-armed — so a
  peace↔war switch toggles both sides (groups 63/147 carry both).

`ConflictZoneSpawnerRelay` (AAEmu.World) — drives the existing paths:

- On every transition and on every `ZoneLoaded`, for each loaded zone in the group, the
  target placements are resolved from the zone's own `npc_spawners.g`
  (`ZoneSpawnerPlacementCatalog`).
- Armed placements are announced with `WZActivateNpcSpawnersInArea` (0x042, `activate=true`);
  retired placements with `activate=false`, and when the row's `use_despawn` is set their
  live NPCs are retired through the same deferred-despawn path `NpcScheduleGate` uses:
  `WZNpcStartDespawn` + `OnZoneNpcRemove` + `NpcSpawnRelay.ForgetNpcState`, leaving the
  bcId registered until the Zone confirms `ZWRemoveNpc`.
- A placement id that is not in the zone's `npc_spawners.g` is skipped and logged — the
  content id is never invented and no coordinate is fabricated.

## Content evidence (shipped compact, read-only)

`enum_conflict_zone_state_kinds`: none=0, peace=1, war=2.
`enum_honor_point_war_states`: war=6, peace=7.
`conflict_zone_npc_spawners` groups (zone_state_kind, spawn_activate, use_despawn, rows):
every group is (war, t, t) except group 139 (peace, **f**, t) and groups 63/147 which carry
both a peace and a war row. Group 139 is the concrete `spawn_activate=false` case this
slice honours.

## Reused / not duplicated

- 0x042 arming circle: the same typed `NpcSpawnerActivateConfig.Radius` knob the
  player-scoped and prewarm arming already use; no new radius constant, and a non-positive
  or non-finite configured radius is rejected loudly (no magic fallback).
- Despawn: the existing `WZNpcStartDespawn` + `OnZoneNpcRemove` + `ForgetNpcState` path,
  with the bcId kept registered until the Zone confirms ZWRemoveNpc (deferred release, so a
  late confirmation cannot free an id another unit already took).
- Placement positions: the existing `ZoneSpawnerPlacementCatalog` (zone-local, the space 0x042
  is evaluated in).
- Both state enums and the loader: unchanged from the base.

## Known limitation (documented, not guessed)

0x042 is a circle (centre + radius), not a per-placement id, so a placement is armed by
announcing its own small circle centred on the placement's own zone-local coordinates. That
is the only arming primitive the Zone exposes and there is no per-placement activation
opcode, so isolation between two nearby conflict placements inside one activation radius is
not provable from the shipped data. The radius is the existing typed knob, not a tuned
per-placement value.

## Verification

- World build: 0 errors.
- Unit tests: full suite 5933/5933, 0 failed, 0 skipped; W07 rules tests 16/16; W07 relay
  tests 10/10 (fanout, exact 0x042 activate/deactivate wire, complementary retirement,
  id+type despawn with deferred bcId release, missing placement, unknown/escalation no-op).
- UTF-8 BOMs on the new C# files; `git diff --check` clean; no Server access, no push.

## Live acceptance (still required before merge)

- Reachable: a player in a conflict zone with a war/peace schedule.
- On the wire: capture WZConflictZoneState (0x084) and WZActivateNpcSpawnersInArea (0x042)
  across a peace→war→peace transition.
- Server effect: World log lines `ConflictZoneSpawnerRelay ... armed/deactivated`.
- Client-visible: peace/war NPCs appear/disappear in the owning zone only.
- Clean state: restart World + Zone and confirm the toggle re-applies on reconnect.
