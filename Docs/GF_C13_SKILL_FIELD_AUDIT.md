# GF-C13 skill content-field audit

## Ledger re-audit

GF-C13 asks for four populated `skills` columns whose ownership was not fully wired:

- `valid_height_edge_to_edge`
- `link_equip_slot_id`
- `auto_fire`
- `sensitive_operation`

The row depends on GF-C05 (skill catalog loading) and asks for evidence-backed ownership and real-row tests. The current base still loaded none of the four columns into `SkillTemplate`, and no runtime code consumed them.

This slice deliberately stops at **typed content, validation, and diagnostics**. It does not invent targeting, height, auto-fire, or security-verification behavior.

## Evidence boundary

The shipped client and zone skill loaders both select all four columns from `skills`, so they are real native content inputs rather than dead schema.

| Field | Proven input | Proven behavior in this slice | Not asserted |
|---|---|---|---|
| `valid_height_edge_to_edge` | Boolean column selected by both loaders beside `valid_height` and `target_valid_height` | Loaded without a fallback and counted by the audit | Any height formula, boundary, or target rejection rule |
| `link_equip_slot_id` | Integer column selected by both loaders | Resolved against `enum_equip_slot`; the no-link value comes from the catalog row named `invalid`; unknown ids fail startup | That casting a linked skill equips, unequips, or blocks an item |
| `auto_fire` | Boolean column selected by both loaders | Loaded and counted; documented as zone-owned | World-side target selection or an auto-attack loop |
| `sensitive_operation` | Boolean column selected by both loaders | Loaded and counted for the future verification gate | Client dialog order, packet result ordering, or which World caller must guard |

The dedicated zone owns the proven `auto_fire` effect: a normal skill start updates the unit's current target when the flag is set. The audit slice does not duplicate that targeting mutation in World.

The client exposes a `not_play_sensitive_operation` result, but the available evidence does not prove the complete request/verification sequence. Runtime gating therefore remains a separate evidence-backed change.

## Current content audit

The read-only shipped compact database was checked before implementation:

- every `link_equip_slot_id` value resolves to an `enum_equip_slot` row;
- most skills use the catalog's `invalid` no-link row;
- only a small populated set of skills links to a real slot;
- `auto_fire` and `sensitive_operation` are both genuinely populated, not schema-only columns.

No shipped ids, slot numbers, rates, or thresholds are embedded in the implementation or tests. The tests use synthetic catalog rows, including a synthetic no-link id, to prove the loader is catalog-driven.

## Implemented surface

- `SkillTemplate` carries the four typed fields.
- `SkillContentFieldReader` reads all four columns with no shipped-value fallback and rejects NULL rows.
- `SkillEquipSlotCatalog` loads `enum_equip_slot`, rejects duplicate ids/names, requires the catalog's `invalid` row, and validates every skill link.
- `SkillContentFieldAudit` produces deterministic counts and resolved link rows.
- `/skillfields` provides read-only diagnostics:
  - `/skillfields summary`
  - `/skillfields links <count>`
  - `/skillfields <skill id>`

## Test and audit matrix

| Check | Deterministic test | Live/evidence gate still required |
|---|---|---|
| All four columns map to typed fields | In-memory `skills` row with distinct values | World boot against the shipped compact database |
| NULL content is not silently defaulted | NULL boolean row fails with the column name | Compact schema is expected to be non-null |
| No-link sentinel is content-owned | Synthetic `invalid` row with a non-shipped id | None; this is a catalog join invariant |
| Unknown linked slot fails loudly | Synthetic skill link to an absent slot row | Confirm no future content introduces an unpaired slot |
| Duplicate slot ids/names fail loudly | Synthetic duplicate catalog rows | None |
| Audit counts and link ordering | Mixed synthetic flag/link rows | `/skillfields summary` against World |
| `auto_fire` runtime effect | Not implemented in World | Zone live cast with a populated auto-fire skill |
| `valid_height_edge_to_edge` runtime effect | Not implemented | Height-band live capture; no formula inferred here |
| `link_equip_slot_id` runtime effect | Catalog resolution only | Client/equipment interaction capture |
| `sensitive_operation` runtime effect | Loaded/counted only | Client verification-dialog and packet-order capture |

## Non-goals

- no guessed height or auto-fire formula;
- no World target mutation for `auto_fire`;
- no equipment behavior for `link_equip_slot_id`;
- no security-verification gate for `sensitive_operation`;
- no hardcoded skill ids, slot ids, thresholds, or percentages;
- no WZ or level-file changes.
