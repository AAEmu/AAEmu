# GF-W08A — siege score and settlement

Scope: the score a zone group's siege runs on, and what happens to a dominion when that siege ends. The
score's *producer* (the guard tower), the tax pool and the guard-tower steps are not in this slice — see
**Not in this slice** at the bottom.

Base: `client_version/zone-10.0.2_r575` at `2c19b936`.

## What the shipped content says

**The score is guard-tower magic power** — `ui_texts` `siege_score_guide` (id 10543, category `DOMINION`):

> 공성측 승리 조건 — 수호탑 마력원을 일정량 이상 자신의 세력의 마력으로 정화해야 합니다.
> (Attacking side: purify a set amount of the guard tower's magic power with your own faction's power.)
>
> 해적 승리 조건 — 수호탑 마력원을 일정량 이상 파괴해야 합니다.
> (Raider: destroy a set amount of it.)
>
> 수성측 승리 조건 — 공성 종료 시까지 마력원의 정화, 파괴를 막아야 합니다.
> (Defender: prevent the purification and destruction until the siege ends.)

So there are three counters and three roles, and each attacker's win threshold is content:

| Key | Shipped value | Role |
|---|---:|---|
| `siege_defense_win_point` | 1000 | the magic power the defender keeps |
| `siege_offense_win_point` | 100 | the attacking alliance's purification |
| `siege_outlaw_win_point` | 100 | the raider's destruction |

A PvP kill is **not** a score event. The base tree carried a `SiegeManager.OnCharacterKilled` hook that
awarded a hardcoded `1` point per registered kill; it had no caller, and the guide text contradicts it. It is
removed, along with its `ISiegeManager` entry. Score now arrives through `AwardScore`, whose amount is the
caller's measured magic power.

**Who the sides are** comes from `siege_factions` + `siege_faction_troops`: the troop table says which
alliance can defend and which can attack, and the raider is derived as the one alliance with an offense
troop and no defense troop. In the shipped rows that is faction 114; the two defending alliances (148, 149)
each field both. Nothing here names a faction id in code — content that cannot single out one raider, or
cannot single out the alliance attacking a given defender, is refused at load.

## The two wire facts the base tree got wrong

Both follow from how the receiving side uses the fields, and both are why the old score path could never
have worked:

1. **The first field of `SCSiegeScorePointPacket` is the zone group.** The dominion record is found by it, and
   a packet whose key matches no record changes nothing. The base tree sent a literal `0`.
2. **The three values are totals, not deltas.** The counters are stored as they arrive (nothing is added to
   what is already there) and shown as a percentage of the side's own win point. The base tree broadcast the
   delta it had just added, so a client would have shown `1` forever.

`SCSiegeMemberPacket` (raid-team join/leave, the #1591/#1617 registration path) has the same key problem:
it was sent as `(0, zoneId, characterId)`, which is read as "dominion 0, team 149 / whatever".
It is now sent as `(zoneGroupId, alliance, characterId)`. The second field is the team key — the alliance
the character fights for, which for a normal defence registration is the dominion's own owner.

`SCDominionOwnerChangedPacket` (0x33) was never constructed at all. Its body is
`u16 zoneGroup, u32 owner, u64 timestamp, bool bestowed`, and it is what changes the owner shown for a
dominion — the settlement now sends it, flagged as bestowed rather than declared.

## What lands

- `SiegeScoreState` — one zone group's three counters, immutable, saturating add (a counter that wrapped
  would report a side as having lost ground).
- `SiegeScoreRules` — reach checks and the outcome decision, as pure functions.
- `SiegeFactionRoles` — the alliances and their sides, from content.
- `SiegeGameData` — loads `siege_factions` / `siege_faction_troops` and, in `PostLoad`, the three win points
  (required rows, positive; a missing one fails the content load rather than the first settlement).
- `SiegeManager.AwardScore(zoneGroup, side, amount)` — refused outside a siege of a zone group that has a
  `siege_zones` row, refused for a zero amount, then broadcast as whole totals read back from the row.
- `SiegeManager.Tick` — settles on the transition **out of** the `Siege` period, which is the only moment a
  siege has actually been fought. A cycle that never reached the siege period has no transition and settles
  nothing. The new phase is written **after** the settlement: a settlement that could not be written leaves the
  dominion in its siege period and is retried on the next minute-long tick, rather than ending the siege with
  nobody given the dominion.
- `ISiegeScoreStore` / `MySqlSiegeScoreStore` — the counters, the outcome row and the dominion's new owner are
  written in **one transaction** (`Settle`), so the database can never hold an outcome the dominion does not
  agree with. The returned record is the one on record: a repeated attempt converges on the stored outcome
  rather than deciding again from counters that have since been zeroed. Only a unique-key violation is
  treated as "already settled"; any other database error is raised and nothing is written.
- `DominionManager.ApplySettlement` — applies an already-persisted record: the in-memory dominion, then
  `SCDominionOwnerChangedPacket` (bestowed) when the owner changed, then the existing `ResyncZone` refresh for
  the clients and the zone. A settled dominion is a nation's, so `expedition_id` is cleared. It does not
  write, so it cannot half-apply.
- `siege_settlements` — one row per (zone group, cycle), with the final scores, the outcome, the defender,
  the winner (0 when the dominion did not change hands) and the reason. The unique key is what makes the
  settlement idempotent across a second tick or a World restart mid-transition.

## The decision, and where it is a policy

| Scores at the end | Outcome | Owner |
|---|---|---|
| an attacker at/over its win point, the other under | `OffenseBrokeThrough` / `OutlawBrokeThrough` | the attacking alliance (the one alliance that is neither the defender nor the raider) / the raider |
| neither attacker over its win point | `DefenseHeld` | unchanged (the shipped defense condition) |
| both attackers over theirs | `Contested` | unchanged, logged loudly, recorded |

Two sides reaching their win points in the same siege is not a shipped case — the guide gives each attacker
a separate win condition — so the settlement refuses to pick a winner rather than inventing a precedence.

A siege nobody won stores **no** winner: `winner_faction_id` is 0 for `DefenseHeld` and `Contested`, and the
alliance that held the ground is in `defender_faction_id`. A stored winner is always an alliance that took the
dominion.

The defender's own counter is deliberately not consulted. Its win point is the magic power it kept, which
is the total of what the two attackers did not take; that total belongs to the guard-tower runtime, and
deriving it here would mean inventing a tower size.

## Not in this slice

- **The score producer.** Nothing calls `AwardScore` yet. Feeding it is the guard-tower runtime: the tower's
  magic power, purified by one alliance and destroyed by the raider, measured per event. Until that lands,
  every siege settles as `DefenseHeld` — which is the correct outcome for a siege where nothing broke
  through, not a silent success.
- **The tax pool.** `siege_extortion_ratios` (tax rate by dominion count), `dominion_tax_limit`,
  `doodad_func_dominion_tax_in_kinds` and the weekly `DominionTaxPayoutTask` path are untouched.
- **Guard-tower steps** (`guard_tower_settings` / `guard_tower_steps`, gates/walls/buffs) and the two
  revival doodads (`siege_defense_first_revival_doodad_type`, `..._second_...`).
- **Raid-commander election** (`SCElectSiegeRaidOwnerPacket`), already called out as a separate voting
  subsystem on the base.
- **Siege rewards.** The `siege_game_reward_win_*` / `..._lose_...` content rows (items, service points,
  leadership points) and the buffs per role are untouched: delivering them needs a mail path and a winner
  resolution this slice does not have.

## Verification

- `SiegeScoreStoreTests` — the store's own statements on a database: first award creating the row, each side's
  counter to itself, saturation, reset, the three settlement writes together, the dominion change for a winner
  and for a defended siege, a second `Settle` for the same cycle returning the stored outcome, and a missing
  dominion row rolling the whole thing back.
- `SiegeManagerSettlementTests` — the phase tick: a siege settles when its period ends, the winner reaches the
  dominion, a defended siege records no winner, a settlement that cannot be written leaves the period alone and
  succeeds on the next pass, an outcome already on record is re-applied instead of re-decided, a cycle that
  never reached the siege settles nothing, an unsettleable defender hands the dominion to nobody, and
  `AwardScore`'s refusals.
- `SiegeScoreStateTests`, `SiegeFactionRolesTests`, `SiegeScoreRulesTests`,
  `SiegeGameDataScoreContentTests`, `SCSiegeScorePointPacketTests`, `SCSiegeMemberPacketTests`.
- Full unit suite on the branch.

Live gates still owed: apply the migration, run a real siege, and read the score, the owner-changed event and
the dominion's owner after the period ends.
