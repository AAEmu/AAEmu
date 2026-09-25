# GF-S04B — safe guild-buff slice

Base: `2c19b936` (`client_version/zone-10.0.2_r575`).

## Scope

This slice hardens the existing guild prestige-buff path. It does not invent new buff
categories, role permissions, prices, or timers.

The shipped content relationships used here are:

- `expedition_buffs.active` and `expedition_buffs.expedition_level_id` gate a category.
- `expedition_buff_grades` supplies the ordered grade, minimum guild level, contribution cost,
  optional item cost, residence requirement, and capacity bonuses.
- `unit_modifiers` rows owned by `ExpeditionBuffGrade` supply the stat bonuses.
- `buff_modifiers` rows owned by `ExpeditionBuffGrade` supply authored buff-duration modifiers.
- `expedition_buff_purchases` remains the durable per-guild grade record.

`GuildBuffPurchaseRules` is the single eligibility/price gate used by the purchase path. The
loader now fails startup on duplicate/orphan grade relationships, malformed costs, missing level
requirements, or negative capacity values instead of silently accepting inconsistent content.

## Duration boundary

The grade table has no expiry or TTL column. A purchased grade is therefore persistent; this slice
does not fabricate an expiration timer. `GuildBuffDurationRules` is a typed diagnostic that
distinguishes `NotAuthored` from `AppliedThroughBuffModifier`: only an authored `Duration` row in
`buff_modifiers` is treated as a duration effect. The existing buff-modifier replacement path applies
those rows to online members on purchase and login and clears them on logout. The
`GetDurationSemantics` helper is diagnostic-only and has no production caller yet.

The item-cost branch is content-dead in the shipped catalog: all 93 authored grade rows have a null
`item_id`. The rules and loader still validate the relationship for future content, but this slice
does not imply that an item-cost purchase is live today. The same rule prevents a future content row
with an empty item cost or a cost without an item from being treated as free.

## Packet contract

The buff-grade request uses `u32` expedition id, `u32` buff id, and signed `s32` grade. Negative
or out-of-range grades are rejected before dispatch. The existing expedition id remains advisory;
server-side membership is authoritative.

## Verification

The focused rules and loader tests cover active state, grade ordering, level/residence gates,
contribution and item-cost validation, content diagnostics, and the typed duration diagnostic. The
base suite is 5,907/5,907; this slice adds 10 tests, for 5,917/5,917 total.
