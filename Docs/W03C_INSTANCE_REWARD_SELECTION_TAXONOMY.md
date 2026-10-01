# W03C — Instance-reward selection taxonomy

Metadata and gate correction only. **This slice delivers no gameplay by design.** Its purpose is to
remove a false premise from W03A so that a future soldier-rank slice is a drop-in rather than a
re-derivation, and to make each blocked class name the exact artifact that would unblock it.

Stacks on W03B, which in turn builds on the W03A delivery that shipped in `AAEmu/AAEmu#1723`. All
three are consolidated on one branch here; nothing already in #1723 is repeated.

## The false premise this corrects

W03A refused a reward kind with this log line:

> `unsupported non-difficulty or unauthored reward selection for instance {1}, kind {2} ({3}); soldier-rank values are not inferred`

The second half is **false**. Soldier-rank is authored end to end. It was refused for one narrow
reason: the difficulty selector required `instance_difficult_infos`, and the soldier-rank instance has
no row there. Everything else the class needs is shipped:

| Artifact | Evidence |
|---|---|
| Delivery action | `indun_actions` 408, zone group 158, `detail_type = 'IndunActionSendMailReward'`, `detail_id = 2` |
| Kind mapping | `indun_action_send_mail_rewards` row `(2, 7)` → reward kind 7 |
| Trigger | `indun_events` 354, zone group 158, `IndunEventNpcKilled` on condition 85, `start_action_id = 408` |
| Instance | `instances` 80 → `target_id = 158`, `target_type = 'IndunZone'` |
| Mail copy | `instance_reward_mail_texts` 11; its body states the grant is based on production-activity ranking |
| Rank bands | `instance_rewards` 623/624/625/626/634 — ranges (1,1) (2,2) (3,3) (4,4) (1,4) |
| Team size | `instance_factions` 371, `min_player = max_player = 4` |

The correct statement is: **the rank bands are authored, the score that orders players into them is
not.** W03C now says exactly that.

## The three-way classification

`InstanceRewardTaxonomyRules.Classify` decides structurally. No reward-kind name and no literal kind id
takes part in any test, so renaming or renumbering content cannot move a kind between classes.

| Class | Selection source | Shipped instances | Deliverable now |
|---|---|---|---|
| `DifficultyBacked` | `instance_difficult_infos` difficulty inside an authored range | kind 6, instance 66 (difficulties 1–12) | **Yes** |
| `RoundBacked` | `indun_rounds` count exactly covers the authored ranges (1..N) | kind 5, instances 52/53/54 (50/21/6 rounds vs ranges 1–50/1–21/1–6) | No — no trigger authored |
| `RankBacked` | fixed-size `instance_factions` team spanning the bands 1..N | kind 7, instance 80 (team 4, bands 1–4) | No — no score source |
| `Unsupported` | none of the above | the legacy arena kinds | No — no selection source |

The round proof is the exact cover: zone group 125 has 50 rounds and instance 52's authored ranges run
1–50, zone group 126 has 21 and instance 53 runs 1–21, zone group 130 has 6 and instance 54 runs 1–6.
A partial cover is rejected, so the match cannot be coincidence.

## Why the score source is genuinely absent

This is the question the earlier evidence hunt flagged as open, and it was checked directly:

- Instance 80's 18 point doodads resolve — through `doodad_func_groups.doodad_almighty_id` →
  `doodad_funcs` — to **`DoodadFuncUse` and `DoodadFuncSkillHit` only**. Neither produces a score.
- `doodad_func_competition_points` ships 10 rows, but **no `doodad_funcs` row has
  `actual_func_type = 'DoodadFuncCompetitionPoints'`**, so none of them is reachable from any doodad.
- The point doodads are *named* for scoring (grade-2/grade-3 score checks per activity, plus a
  per-army flag check), but a name is not a threshold and is never read as one.
- `instance_mini_scoreboards` and `instance_gain_rules` are loaded here as typed metadata and are
  documented as **display and grouping only**: they carry a category name, an icon key, a sort order
  and a merge target, and no score value.
- `instance_factions` proves the *shape* of the ranking (a fixed 4-player team) and nothing about the
  outcome.

Correction to the prior findings file: `indun_event_zone_score_level_changeds` has no zone-group
column at all (it is `source_faction_id, zone_score_kind_id, level, change_way`). Its 3 shipped rows
are all `zone_score_kind_id = 6`. The claim that those rows are "bound to zone group 130" is not
supported by the schema; the substantive conclusion — that no production-ranking kind is bound — still
holds.

## `count` is not a stack number

Recorded because it is the easiest thing to get wrong downstream. Buff **7149** is a permanent,
`max_stack = 1` account-privilege flag, and it is authored with `count = 555` on some bonus rows.
555 stacks of a permanent membership flag is meaningless, so `count` is an authoring artifact, not a
quantity to apply. W03B's refusal to map it onto buff stacks or charges is correct and is carried
forward unchanged. W03C adds no bonus application of any kind.

## What this slice refuses to do

- **No delivery for round-backed rewards.** The selection value is proven, but no
  `indun_action_send_mail_rewards` row is authored for a round-backed kind — the shipped table has
  exactly two rows, for kinds 6 and 7. Authoring one would be inventing content.
- **No score, no ordering rule and no tie rule for rank-backed rewards.** The bands exist; what fills
  them does not.
- **No guessed round→trigger mapping.** The round state machine exists (`IndunRound`,
  `IndunRoundState`, `IndunRoundRules`) and zone groups 125/126/130 do carry authored round actions
  (`IndunActionNextRound`, `IndunActionRoundAlarm`, `IndunActionSetRoomCleared`, …), but nothing
  authored routes a round to a reward delivery.
- **No bonus application**, no authored content row, no change to W03A's soldier-rank *outcome*
  (it still delivers nothing) or to W03A/W03B's exactly-once claim guard.

## For the eventual live run

- **Client-visible surface:** `SCMiniScoreboardUpdate` `0x339`, `SCMiniScoreboardRemoved` `0x33A`,
  `SCMiniScoreboardClear` `0x33B`. These are the packets that would reveal the live ranking.
- **The action fires on boss death** — `indun_events` 354, `IndunEventNpcKilled` on condition 85 — so
  a capture must span a boss kill, not just instance entry.
- **Capturing the Island of Plenty instance (instance 80) would recover the score-to-rank rule.**
  Until such a capture exists, the ordering stays refused.

## Verification

- Full TUnit suite: **5935 / 5935** (18 new taxonomy tests; the renamed W03A test is 1:1).
- Opt-in runtime-compact content tests: **7 / 7**.
- Opt-in MySQL delivery tests (W03A + W03B): **11 / 11** against MySQL 8.0.46.
- Content-loading tests share a non-parallel xUnit collection. `IndunGameData` is a process-wide
  singleton whose `Load` replaces every catalog collection; two classes loading it concurrently
  corrupt its non-concurrent collections and produce phantom duplicate diagnostics. This was observed
  and is fixed by `IndunRewardContentCollection`, not by loosening an assertion.

## Where this leaves the row's "exactly once across restart"

Not delivered, and not deliverable from the current catalog. The claim ledger *is* durable
(`indun_reward_claims` and `indun_reward_bonus_grants`), and the delivery service is exactly-once
against a run id it is handed. What does not exist is a durable *run*: `Dungeon.RewardRunId` defaults
to a process-scoped live-copy key, and the per-character dungeon counters behind
`SCInstanceVisitCounts` (`EntryHistory`, `ResetPurchaseCount`, `PermitBonusCount`) are plain in-memory
dictionaries. A process restart therefore loses the copy, the counters and the run identity together,
and nothing in the tree ever passes a persisted `rewardRunId` to the `Dungeon` constructor.

Closing that gap is a design decision, not a wiring task: it means deciding what a run *is* across a
restart (resume the copy, forfeit it, or re-run it) and what durable rows that implies. It is
deliberately left unbuilt here rather than approximated.

## Note on the base tree

The provenance cleanup noted in earlier drafts of this file is already in the base tree
(`chore: remove reverse-engineering provenance from comments and docs`), so nothing here depends on
it.
