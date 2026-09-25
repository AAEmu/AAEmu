# Q13 drop-rule groundwork

## Scope

This branch only adds typed metadata loading, SQL-WHERE parser coverage, and startup
 diagnostics for the shipped drop-rule tables. It does **not** select loot packs, alter
`Unit.DoDie`, or replace the existing `loot_pack_dropping_npcs` path.

The existing Game/World loot path remains the sole runtime owner. No Zone, World, or
packet ownership is changed, and there is currently no evidence for transferring loot
ownership to a new rule service. `for_batch` is retained as loaded metadata only; its runtime
meaning is intentionally not inferred. `drop_rule_loot_packs` has no pack-level weight
column, so this branch does not invent a weighted union-selection policy.

## Read-only content findings

A read-only inspection of the current compact data found:

- 351 `drop_rules` rows and 1,094 `drop_rule_loot_packs` rows.
- 3 rules with no memberships.
- 316 membership rows referencing `loot_pack_id` values absent from `loot_packs`.
- 322 distinct matcher SQL expressions; all 322 parse in the typed parser probe.
- The diagnostic loader reports 68 invalid rules (mostly missing-pack memberships),
  140 `for_batch` rows, and 19,522 typed NPC subjects.
- A hypothetical fail-closed selection using the currently loaded packs would have
  rejected 81 NPC templates because of the missing-pack memberships. That selector is
  intentionally not in this branch, so this is a content-integrity warning, not a live
  loot behavior change.

The loader reports rule/membership counts, orphan memberships, missing-pack memberships,
invalid rules, `for_batch` rows, and loaded NPC subjects. Invalid metadata is logged and
retained for review rather than silently replaced with guessed content.

## Review boundary

Before any runtime wiring, the following need separate evidence and a focused design:

1. The meaning and ownership of `for_batch` and the rule-to-pack union behavior.
2. The intended behavior for the 316 absent-pack memberships and 81 affected NPC
   templates.
3. Whether direct `loot_pack_dropping_npcs` rows remain a fallback, default path, or are
   replaced by a separately authorized rule path.
4. A packet/ownership test proving that the chosen authority remains the existing loot
   owner.

Until those questions are resolved, the metadata and parser are diagnostic groundwork only.
