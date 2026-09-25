# GF-W08B — dominion tax and guard-tower content slice

Base: `2c19b936` (the freshly fetched official `client_version/zone-10.0.2_r575` tip).

## Scope

This slice is deliberately separate from W08A (`feat(siege): add score and settlement`, published as
[#1727](https://github.com/AAEmu/AAEmu/pull/1727)). It does not add score, settlement, or raid-commander
election behavior.

The shipped compact content proves these catalog relationships:

- `guard_tower_settings` — territory radii, gate/wall caps, and the initial buff id.
- `guard_tower_steps` — ordered per-setting caps and buff ids. These are **caps and buffs**, not a wall
  or gate spawn list.
- `siege_extortion_ratios` — a `(faction_id, dominion_count)` keyed ratio catalog.
- `doodad_func_dominion_tax_in_kinds` — an item/count/next-phase turn-in catalog.
- `content_configs.dominion_tax_limit` and the Hero tax-rate bounds — payout and tax-rate limits.

The loader now validates duplicate keys, unknown guard-tower settings, non-positive steps, caps above
the corresponding setting, non-positive extortion ratios, and malformed in-kind rows. Missing or
ambiguous required values fail loudly during game-data load or manager startup.

`DominionClaimRules.CapTax` now refuses a missing/non-positive tax limit instead of paying an uncapped
pool. Both `DominionManager` and the separate `GuildDominionManager` claim store require the same
tax-rate bounds and payout limit before loading claims.

`GuardTowerStepRules` and `SiegeExtortionRules` provide exact, pure lookups so a later evidence-backed
consumer cannot invent a fallback. Guard-tower setting id `0` remains a valid build-step-only lodestone
case; a missing nonzero setting id fails loudly. Narrowed content fields are range-checked before
conversion, so an out-of-range SQLite value cannot wrap into a valid-looking cap or radius.

## Intentionally not implemented

- No `siege_extortion_ratios` payout/rate formula: the available 10.0.2.13 evidence does not prove which
  runtime consumer applies the ratio or how it reaches the client.
- No `doodad_func_dominion_tax_in_kinds` item consumption or pool credit: the compact has the catalog,
  but the available content has no `special_effects` row of type 138 and no proven turn-in target/zone
  contract. The existing `DominionTaxInKind` stub remains unclaimed rather than guessing a target.
- No automatic wall/gate spawning from `guard_tower_steps`: the rows are caps/buffs, while walls and
  gates are player-placed housing drawings. The catalog and pure cap rules are ready for the proven
  placement consumer.
- No house-tax-to-pool credit: the housing tax path is explicitly documented as not crediting the pool
  until its client/packet contract is proven.

These are content/runtime gaps, not silent fallbacks. The full unit suite covers the loader and pure
rules; live MySQL/client checks remain required before merge.
