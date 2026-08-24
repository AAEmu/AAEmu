# Regrade

- Audience: Contributors and maintainers
- Last verified against: ArcheAge **10.0.2.13** client
- Prerequisites: Familiarity with `AAEmu.Game/Models/Game/Skills/Effects/SpecialEffects/GradeEnchant.cs`

<!-- markdownlint-disable-file MD013 -->

Regrade is the first tab of the enchant window: a scroll pushes an item one step up the
`item_grades` table, or punishes the attempt. It was inert in two independent ways — the tab could
not be opened, and had it been opened every attempt would have failed. Both are fixed; this page
records what was wrong and what the shipped data actually says.

## The tab was never built

The enchant window assembles its tab list from the feature set, and the first page is keyed on
`itemGradeEnchant` (bit 200). Anything absent from `Configurations/Features.json` stays off, and that
bit had never been listed, so `grade` was dropped from the page list before the window was drawn.
There was nothing to click and consequently no cast ever reached the server.

The bit is now set alongside the other enchant systems. Note that this is a client gate only —
turning it off again hides the tab but does not disable any of the server logic below it.

## The odds moved out of `item_grades`

10.0.2.13 removed the `grade_enchant_*` columns from `item_grades` and replaced one global set of
odds with per-item sets across three tables:

| Table | Holds |
|---|---|
| `item_enchant_ratios` | The odds, keyed by (`item_enchant_ratio_group_id`, `grade`) |
| `item_enchant_ratio_groups` | Which items a set applies to — `kind` 1 default, 2 item_impl, 3 custom |
| `item_enchant_ratio_items` | The membership lists of the custom groups |

None of the three was loaded, so `GradeTemplate.EnchantSuccessRatio` and its siblings stayed at their
default of zero. Zero is a valid chance, not an error, so nothing was logged and every roll fell
straight through to `Fail` — for every item, every grade and every charm.

`ItemEnchantGameData` loads them now and resolves a group most-specific-first, which is the only
order that can work: an explicit list names individual items, an item_impl group claims a whole item
class, and default takes what is left.

```text
item_enchant_ratio_items[itemId]           →  custom group   (2121 items across four groups)
item_enchant_ratio_groups by items.impl_id →  slave equipment 28, mount armour 30, summons 8
otherwise                                  →  the default group
```

Ordinary weapons, armour and accessories land on the default group. A missing default group is
warned about at load rather than left to produce silent zeroes again.

### `grade` is an id, not an order

`item_enchant_ratios.grade` is an `item_grades.id`. The two numberings disagree at the bottom of the
table — id 1 is Lv.0 with `grade_order` 0, id 0 is Lv.1 with order 1 — and the shipped costs confirm
the id reading: the row for grade 1 costs 1, the row for grade 0 costs 9, and everything above rises
monotonically. Rows are therefore matched on `Item.Grade`, while stepping a grade up or down goes
through `grade_order`. The same applies to `items.max_enchantable_grade`, which is resolved through
the grade table before being compared.

## How an attempt now resolves

The success roll comes first. Only the "shining" scrolls may overshoot by a grade, which is what
`value1` on the special effect marks; the plain ones preview Great Success at 0% even where the ratio
row offers it.

A failure then spends **one** roll across break, disable and downgrade in that order. Previously each
outcome drew its own roll, which let a single failure both destroy an item and downgrade it. The
shipped numbers are authored for a shared space, so the cascade reproduces them exactly while making
overlapping outcomes impossible.

`Disable` is new in this build — the client's `IGER_*` constants gained it, which is also what pushed
`Fail`, `Success` and `GreatSuccess` each one value up. It sets `ItemFlag.EnchantDisabled` and is
followed by `SCItemDetailUpdatedPacket`, because the grade task carries only the grade while the
tooltip's `isEnchantDisable` reads the flags byte.

The fee is evaluated before the roll, so a player who cannot cover it keeps both their coin and their
scroll. `item_grade` in formula 22 is not the grade but the ratio row's own cost factor — feeding it
the unpopulated `GradeTemplate.EnchantCost` was the second half of the data problem. The formula also
reads `grade_enchant_cost_mul`, which no shipped table supplies; omitting it from the parameter set
made the whole evaluation throw and fall back to a free regrade, so it is passed explicitly as
neutral.

## Defects fixed along the way

- **Grade 255.** A downgrade fed `grade_enchant_downgrade_min`/`_max` straight into
  `Random.Shared.Next` and cast the result to a byte. Both columns are -1 wherever a grade defines no
  downgrade, which is every low grade, so the item was stamped with grade 255. The guard against it
  tested a `byte` for being negative and could never fire. The range is now checked first, treated as
  inclusive on both ends, and a "downgrade" that would not go down is reported as `Fail`.
- **Null charm.** An item placed in the support slot that is not an enchanting support at all
  produced a null lookup that was dereferenced one line later, taking the cast down with it.
- **Scrolls burned on rejection.** Rejected attempts returned without setting `Skill.Cancelled`, and
  that flag is the only thing gating reagent consumption — so "not enough money" still cost a scroll.
  Every rejection path sets it now.
- **Slave equipment and mount armour were inert.** Neither carries a wearable slot, and the cost
  helper aborted the whole regrade when it could not find one. They now pay the formula's base term.
- **No target validation.** `value3` on the special effect is the `impl_id` the scroll is sold for —
  1 weapons, 2 armour, 24 accessories, 28 slave equipment, 30 mount armour — and is now checked
  against the item, so a weapon scroll cannot be aimed at a breastplate. `gradable`, an existing
  enchant lock-out, and the per-item grade ceiling are checked as well.

`items.max_enchantable_grade` had to be added to `ItemTemplate` for the ceiling check; -1 (no
ceiling) is what most items carry, and the ~6.3k that do carry one are capped at grade 7.

## What the shipped data means

Worked through for a level 48 weapon on the default group. Success is a share of attempts; break and
downgrade are shares of the failures left over:

| Grade | Success | Break | Downgrade | Fee |
|---|---|---|---|---|
| 0–5 | 100% | — | — | 8–32 g |
| 6 | 70% | — | — | 46 g |
| 7 | 60% | 50% | → grade 4 | 66 g |
| 8 | 20% | **100%** | — | 91 g |
| 9 | 12% | **100%** | — | 133 g |
| 10 | 3.8% | **100%** | — | 188 g |
| 11 | 2% | **100%** | — | 275 g |
| 12 | — | — | — | terminal |

From grade 8 upward `grade_enchant_break_ratio` is 10000, so **every** failure destroys the item.
That is deliberate rather than a defect: the charms exist to buy it down, and they ship
`add_break_mul` of -50 and -100. A server that wants this gentler should change the data, not the
roll.

Grade 12 has a success ratio of 0. That is the top of the ladder and is reported as
`GradeEnchantMax` rather than as a failed attempt, so it costs nothing.

## Not covered yet

- `item_grade_enchant_fail_break_rewards` is not read. The result packet's compensation fields go out
  as zero, so a broken item pays nothing back.
- The charm restrictions `restrict_item_tag_id`, `exclusive_item_tag_id` and
  `req_scale_min_id`/`_max_id` are loaded nowhere and unenforced. The scale range needs care before
  it is turned into a gate: 74 of the 100 rows carry 31/31 against a tempering ladder that tops out
  at 30, which reads as a sentinel for "no requirement", but genuine ranges such as 18–30 also exist.
- `SkillObjectType.ItemGradeEnchantingSupport` (7) reads 13 bytes off the wire. Whether this client
  really sends that shape, rather than treating the flag byte as a bit mask, is unverified. A cast
  *with* a charm that logs "Attempted to read beyond the end of the stream" would be this.
