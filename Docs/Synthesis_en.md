# The Synthesis System

How synthesis ("evolving") works on this server: the tables it reads, the state it keeps
on an item, how experience, gold, grades and stats are computed, and how the result
reaches the client.

> **Naming.** The client's *Synthesis* tab is `evolving` everywhere in the data and in
> the code. Its neighbours in the same window are *Awakening* = `item_change_mapping`
> and *Tempering* = `enchant_scale` / `item_cap_scale`. Anything named `rnd_attr`
> ("random attribute") is the synthesis effect system.

---

## 1. Where it lives

| Concern | File |
| --- | --- |
| Feeding materials, grades, gold, effect grants | `AAEmu.Game/Models/Game/Skills/Effects/SpecialEffects/ItemEvolving.cs` |
| Swapping one effect line | `.../SpecialEffects/ItemEvolvingReRollBase.cs` (+ `ItemEvolvingReRoll.cs`, `ItemEvolvingSelectReRoll.cs`) |
| Element specialisation | `.../SpecialEffects/ItemElement.cs` |
| Awakening carry-over | `.../SpecialEffects/ItemChangeMapping.cs` |
| All table lookups, rolls, magnitudes | `AAEmu.Game/GameData/ItemEnchantGameData.cs` |
| Table row models | `AAEmu.Game/Models/Game/Items/ItemRndAttrCategory.cs`, `ItemRndAttrUnitModifier.cs` |
| Per-item state + wire/DB layout | `AAEmu.Game/Models/Game/Items/EquipItem.cs` |
| Stats folded into the character | `AAEmu.Game/Models/Game/Units/Unit.cs` -> `UpdateGearBonuses` |
| Activation mask in the unit state | `AAEmu.Game/Models/Game/Items/EquipmentSerializer.cs` |
| Result packets | `AAEmu.Game/Core/Packets/G2C/SCItemEvolvingResultPacket.cs`, `SCItemReRollEvolvingResultPacket.cs`, `SCUnitEquipmentsRndAttrUnitModifierActivatedPacket.cs` |

Special-effect ids: `ItemEvolving = 123`, `ItemEvolvingReRoll = 136`,
`ItemEvolvingSelectReRoll = 187`, plus `ItemElement` for the third sub-menu.

---

## 2. Data model

Everything is table-driven; no quantity is hard-coded in the server.

| Table | What it carries |
| --- | --- |
| `item_rnd_attr_categories` | A **pool**. `max_evolving_grade`, `material_grade_limit` (255 = no limit), `currency_id`, `item_rnd_attr_category_group_id`, `re_roll_item_set_id` |
| `item_rnd_attr_category_properties` | Per pool **per grade**: `grade_exp` (price of a rung), `gain_exp` (worth as a material), `max_unit_modifier_num` (effect cap), `gold_mul` (per-mille price rate), `bonus_exp_chance/min/max`, `max_element_level`. `req_exp` is **zero on every shipped row** - do not use it |
| `item_rnd_attr_category_relations` | Which material **groups** a target group accepts |
| `item_evolving_materials` | Maps an infusion item id to the pool it feeds |
| `item_rnd_attr_unit_modifier_group_sets` | A **bundle** inside a pool: `weight`, `pick_num` ("randomly select up to N") |
| `item_rnd_attr_unit_modifier_groups` | One rollable **effect**: `unit_attribute_id`, `unit_modifier_type_id`, `weight`, `fixed_attr` |
| `item_rnd_attr_unit_modifiers` | Per group **per grade**: `min` / `max` magnitude range |
| `item_rnd_attr_category_elements` | Element ladder: `level`, `req_exp`, `tax`, `consume_lp` |

Loaded in `ItemEnchantGameData.LoadRndAttributes`.

### Which pool an item belongs to

Two different places, and **both** have to be asked:

```csharp
GetCategoryId(item):
    item_evolving_materials[item.TemplateId]        // registered infusions
    ?? EquipItemTemplate.RndAttrCategoryId          // everything the equipment tables know
```

Infusions filed as armor - the shipped Story Quest ones are - only appear in the second
place. Asking one source alone makes half the materials in the game look inert.

---

## 3. What an item stores

`EquipItem`, persisted inside the 18-value `pisc` detail block (`WriteDetails` /
`ReadDetails`), which is also the `items.details` blob format:

| Field | pisc slot | Meaning |
| --- | --- | --- |
| `EvolvingExp` | 3 | Progress **inside the current grade** - not a running total |
| `RndAttrGroupIds[5]` | 13-17 | The effect lines, as *group ids* only |
| `EvolveChance` | own u16 | Remaining change (re-roll) attempts |
| `ElementLevel` | own u8 | Element specialisation level |
| `Grade` | item header | **This is the synthesis grade.** There is no second field |

Two consequences worth internalising:

- **No magnitude is ever stored.** Only the group id. What a line is worth is looked up
  from its range at the item's grade and slid by its progress - which is why the same
  effect grows as the piece is synthesised, and why the window shows a span
  ("Strength 34~39") instead of a number. Storing rolled values would make the same item
  report different stats on every re-total, and leave the client - which does this lookup
  itself - with nothing to draw.
- **`EvolvingExp` is per-section.** The tooltip prints it straight against the current
  grade's `grade_exp` ("Current XP 520/230"). Treating it as cumulative leaves the grade
  standing while the bar reads past 100%. The running total is never stored; it is
  reassembled on demand by `GetCumulativeExp` (only awakening needs it).

---

## 4. One synthesis attempt, start to finish

`ItemEvolving.Execute`:

1. **Validate** - caster is a character, target is an item in the inventory, it is an
   `EquipItem`, it is not `EnchantDisabled` (a failed awaken/temper locks a piece), and
   its pool exists.
2. **Resolve materials** (`ResolveMaterials`) - the cast carries up to six slots in
   `SkillObjectItemEvolvingMaterials`. Each is settled fully *before* anything is
   consumed, so the consume phase cannot half-succeed. A slot is dropped when it is a
   duplicate id, is the target itself, is not in the player's bag, belongs to a pool the
   target's group does not accept, exceeds the pool's `material_grade_limit`, or is worth
   no `gain_exp` at its grade.
3. **Sum experience** - see section 5.
4. **Labor** - priced **per infusion**, not per cast. `skill.LaborUnits = materials.Count`
   hands the count to the generic charge in `Skill.EndSkill`; the effect only pre-checks
   affordability.
5. **Compute and charge gold** - see section 6. Charged *before* anything is granted, so a
   player who cannot pay keeps both coin and infusions.
6. **Consume materials** - as `ItemTaskType.SkillReagents`. The infusions are spent by
   this effect rather than by the skill engine, so without this task the synthesis tab
   never learns its cast is over and stays stuck.
7. **Bank the experience and step grades** (`ApplyExperience`) - see section 7.
8. **Grant effects** at each grade reached (`TopUpAttributes`) - see section 8.
9. **Re-total gear bonuses** if the piece is worn (`UpdateGearBonuses`), since bonuses are
   otherwise only summed at equip time.
10. **Reply** - `SCItemTaskSuccessPacket(Evolving, [ItemGradeChange])` when the grade moved,
    `SCItemDetailUpdatedPacket`, then `SCItemEvolvingResultPacket`.
11. **Quest hook** - `OnEvolvingMaterialConsumed` counts *materials*, not attempts.

---

## 5. Experience gained

```
addExp   = sum over materials: gain_exp of the MATERIAL'S OWN pool at the MATERIAL'S OWN grade
bonusExp = sum over materials: RollBonusExp(material property)
```

The material's worth comes from **its own** pool row, never the target's. A Rank 1
infusion sits at grade 2 of pool 672, which carries 50.

`RollBonusExp`:

```
roll Random(10000) < bonus_exp_chance               -> otherwise 0
percent = Random(bonus_exp_min .. bonus_exp_max)    // shipped default is 100/100
bonus   = gain_exp * percent / 100                  // a PERCENTAGE of the base gain
```

`purchased = min(addExp + bonusExp, GetExpToMaxGrade(...))` - experience past the top of
the ladder is the **overflow** the window reports beside the bar. It is banked but buys
nothing (and is trimmed at the ceiling, section 7).

---

## 6. Gold calculation

The price is **not** a multiple of the experience bought. The experience is laid across
the grades it will travel, each grade billing its own slice at its own `gold_mul`, and
only then is formula 64 applied.

### 6.1 Band pricing - `AccumulateBandPrice`

```
remaining = item.EvolvingExp + offeredExp      // offered = addExp + bonusExp
banked    = item.EvolvingExp                   // already paid for once - free
grade     = item.Grade
topOrder  = grade_order(category.max_evolving_grade)
accumulated = 0

loop (max 32 steps, while remaining > 0):
    property = properties[category, grade]         // stop if the pool never priced this grade
    chunk    = min(property.grade_exp, remaining)
    accumulated += (chunk - banked) * property.gold_mul / 1000     // gold_mul is PER MILLE
    banked = 0                                                     // only the first slice deducts

    order = grade_order(grade)
    if order >  topOrder: break
    if order == topOrder and remaining >= property.grade_exp: break   // top rung billed, walk ends

    remaining -= property.grade_exp
    grade = grade_of(order + 1)                    // grade_order, NOT the numeric id
```

- `gold_mul` is a **per-mille** rate (`/ 1000`).
- Banked experience is deducted **once**, from the first slice only; every later grade is
  entered from nothing.
- The walk follows `item_grades.grade_order`, never the numeric grade id (Crude has id 1
  but sits *below* Basic at id 0 - walking ids sends a Basic item backwards into Crude).
- Overflow past the ladder costs nothing.

### 6.2 Formula 64 - `EvolvingCost`

```
formula 64 = FormulaKind.ItemEvolvingCost
parameters:
    item_evolving_value    = accumulated        (from 6.1)
    item_level             = item.Template.Level
    item_evolving_cost_mul = caster's UnitAttribute 223 (ItemEvolvingCostMul)

cost = round(formula.Evaluate(parameters))      // <= 0 or NaN -> free
```

`item_evolving_cost_mul` is **not** the pool's `gold_mul`. It is unit attribute 223 on the
caster - a per-mille discount that is zero unless a buff grants one.

### 6.3 Charging - `TryCharge`

Pays in the pool's `currency_id`. Only `Gold` and `GoldWithAaPoint` are wired; any other
currency is **refused**, not silently waived, so a pool this server cannot bill does not
become a free ride.

---

## 7. Grades and the ladder - `ApplyExperience`

```
while true:
    next = NextGrade(item.Grade)                  // by item_grades.grade_order
    needed = grade_exp[category, next]            // a rung is priced by the grade it LEADS INTO
    if needed == 0:
        if not AllowsFreeStep(category, next): break     // unpriced rung = end of this pool
        needed = grade_exp[category, item.Grade]         // free step: pay by filling the grade below
    if item.EvolvingExp < needed: break

    item.EvolvingExp -= needed                    // subtract, carry the remainder up
    item.Grade = next
    if item.EvolveChance < 5: item.EvolveChance++ // one change attempt per grade gained
    TopUpAttributes(item)
```

Then, at the top of the ladder, the bar still fills but cannot tip over: experience past
the last rung's own cost is clamped to that cost, so a maxed piece reads 100% instead of
some number past it.

Related helpers in `ItemEnchantGameData`:

- **`GetLadderTop`** - the highest grade that carries a real `grade_exp`. This, not
  `max_evolving_grade`, is the honest ceiling: the shipped column says Celestial on 360
  categories whose rows go on charging real prices for Divine, Epic, Legendary and
  Eternal - and the awakening scrolls require those grades. (The *client* still gates its
  own window on the shipped column, so lifting it there needs a client-DB patch.)
- **`AllowsFreeStep`** - the story-quest sets are the reverse case: the ceiling names one
  grade more than the ladder prices, and that spare grade is the jump those pieces make
  from a full bar.
- **`GetCumulativeExp` / `PlaceCumulativeExp`** - total banked to (grade, section exp) and
  back. Used by awakening (section 10).
- **`GetExpToMaxGrade`** - what is still purchasable; everything beyond is overflow.

---

## 8. Effects - how a stat gets on a piece and on the character

### 8.1 Granting - `TopUpAttributes` + `RollRndAttrGroups`

```
cap = min(max_unit_modifier_num[category, grade], 5)
if used >= cap: nothing to grant
```

The lower rungs of a pool have a cap of 0 and exist purely as steps, which is why a fresh
piece can climb several grades before its first effect appears.

The draw itself follows the "Available Effects" window exactly:

- **Every bundle contributes** - bundles are not alternatives. A pool's `pick_num` values
  sum to its `max_unit_modifier_num`; a pool with a 1-pick and a 2-pick bundle grants three.
- **A bundle's allowance is per item, not per draw.** What the piece already holds from
  that bundle counts against its `pick_num`. (Drawing per empty slot instead is how a
  weapon ended up wearing both Spirit and Intelligence out of the same one-pick bundle.)
- **`fixed_attr` entries are granted, not rolled**, and use up the bundle's picks.
- **No attribute twice** - "Can't result in two of the same effect".
- **Weighted pick with a floor of 1**: rows with weight 0 stay reachable, because the data
  uses 0 for "rare, but not impossible".

### 8.2 Magnitude - `GetRndAttrModifier`

Neither rolled nor stored. Read off the range and slid by progress:

```
range = item_rnd_attr_unit_modifiers[group, grade]
value = range.min
span  = range.max - range.min
section = grade_exp[category, grade]
if span != 0 and exp > 0 and section > 0:
    value += span * min(exp, section) / section
```

- The guard is `span != 0`, **not** `span > 0`: reducing lines run from -18 to -25, so
  their span is negative. A `> 0` guard pins every reducing effect to the weak end.
- `ItemRndAttrUnitModifier.Value` is **signed** for the same reason - "Received Melee
  Damage -2.5%" is stored as -25.
- A rung that costs nothing has no progress to make and stays at its floor.

### 8.3 Folding into the character - `Unit.UpdateGearBonuses`

For every worn `EquipItem`, for every used group id: look up the modifier at the piece's
grade and progress, and add it to the gear bonus index.

```csharp
if (modifier == null || modifier.Value == 0) continue;   // not "> 0" - negatives count
```

Gear bonuses are summed at equip time only, so **every in-place change re-totals
explicitly** - synthesis, effect swap and awakening all call `UpdateGearBonuses(null, null)`
when the piece is in the equipment container.

### 8.4 The client-side activation mask *(the fix that made stats show up)*

The equipment block of the unit state ends in a u64 with **one bit per equipment slot**.
The client unpacks it to one byte per slot at `unit + 0x1be8` and checks it **last**,
after establishing the piece has a rolled-attribute pool at all, before folding those
attributes in (`0xbceedb`: `cmp byte [slot + unit + 0x1be8], 0` -> zero drops the whole
branch). Everything else about the slot - its own stats, its rune, its nine lunagems -
runs without that byte, which is exactly why only the synthesis rows were missing.

- `EquipmentSerializer.BuildRndAttrActivationMask` sets bit *i* where the item in slot *i*
  has a non-empty `UsedRndAttrGroupIds`. **Not an occupancy mask** - a piece with no rolled
  attributes has nothing to switch on.
- The mask otherwise only rides along with the unit state, which is not resent for a piece
  changed while worn - so `SCUnitEquipmentsRndAttrUnitModifierActivatedPacket` (opcode
  `0xC0`, BcObjId + i64) republishes it at the end of `UpdateGearBonuses`.

---

## 9. Swapping an effect - `ItemEvolvingReRollBase`

Two variants share one body: `ItemEvolvingReRoll` (the swap is rolled) and
`ItemEvolvingSelectReRoll` (the player may also name the *result*). Both let the player
choose *which line* is replaced.

- **Costs one change attempt.** `EvolveChance` is earned one per grade gained, capped at 5
  (the tooltip tops out there however far a piece is pushed). At zero the piece must be
  synthesised further first.
- **Which line** - the radio button's zero-based index arrives as
  `SkillObjectExtraValues.Values[0]` from *both* variants. Out of range falls back to a
  random line so a stale window cannot wedge the cast.
- **What it may become** - the replacement is drawn from **the same bundle** that owns the
  slot (`RollRndAttrGroup(..., sameBundleAs: beforeGroupId)`); that bundle's choices are
  the only ones the window offers for that line. Excluded are *all* attributes the piece
  wears, the line being replaced included - a swap is meant to trade the effect away, so
  handing the same one back is not a valid outcome.
- **A named pick** (`Values[2]`, selectable variant only) is a *request*: it must belong to
  the same bundle, belong to the pool, and not duplicate a held attribute - otherwise the
  swap falls back to rolling. A hand-built cast cannot pull in an effect from elsewhere.
- **Both magnitudes are read at the piece's own progress** for the before/after dialog.
  Asking without it returns the floor of the range, which is 0 for percentage effects -
  the dialog then offers to trade "0.0%" for "0.0%".

---

## 10. Neighbours that touch synthesis state

### Element specialisation - `ItemElement`

The third sub-menu of the Synthesis tab. Spends banked `EvolvingExp` to raise
`ElementLevel` one step along `item_rnd_attr_category_elements`, capped by the grade's
`max_element_level`. The ladder carries **its own** `tax` and `consume_lp` - the skill has
none - so money and labor are charged inside the effect.

### Awakening carry-over - `ItemChangeMapping`

- `EvolvingExpInherit == false` -> experience and effect lines are wiped.
- Otherwise the **cumulative** total is rebuilt in the old pool (`GetCumulativeExp`) and
  re-placed on the new pool's ladder (`PlaceCumulativeExp`), which yields the new grade
  *and* the leftover progress. The ladders are cut to line up: a Brilliant Jerkin maxed at
  Unique has banked 1607, and 1607 is exactly what the Hiram Jerkin's ladder charges to
  reach Arcane. 7437 of 7658 shipped mappings land on an exact rung.
- Effect lines survive, but **re-homed by attribute** via `TranslateGroupsToCategory` - a
  group id only means something inside its own pool (both its magnitude and the bundle that
  owns it). Carrying an id across unchanged leaves an effect no bundle recognises as its
  own, and that bundle then grants a second of the same kind. Attributes the new pool does
  not offer are dropped; `TopUpAttributes` fills whatever the new grade allows.
- `EvolveChance` rides along - attempts belong to the piece, not the tier.

---

## 11. Packets

| Packet | Opcode | Carries |
| --- | --- | --- |
| `SCItemEvolvingResultPacket` | see `SCOffsets` | `u64 itemId`, **`u8 newGrade` first**, `u8 oldGrade`, `u8 addCount`, `u32 addExp`, `u32 bonusExp`, `u32 addChance`, then `addCount x {i16 attr, u8 type, u32 value}` |
| `SCItemReRollEvolvingResultPacket` | see `SCOffsets` | `i64 itemId`, `i8 type`, `bool changeAttr`, then two modifier structs (before, after) |
| `SCUnitEquipmentsRndAttrUnitModifierActivatedPacket` | `0xC0` | BcObjId + `i64` slot mask |
| `SCItemDetailUpdatedPacket` | - | the refreshed detail block |
| `SCItemTaskSuccessPacket(Evolving, [ItemGradeChange])` | - | published only when the grade actually moved |

Notes that cost time once:

- The **new** grade comes first. Swapped, the client reads the attempt as a downgrade and
  the result window does not play out.
- Equal grades with `bonusExp = 0` is the defined **quiet outcome** for an attempt that only
  banked experience - send it unchanged rather than suppressing it.
- `addCount` is the reader's only length for the trailing run; it must match.
- The detail goes out on its **own** packet. The `UpdateDetail` item task carries an array
  the client does not decode as a detail, which leaves the item drawn as broken until relog.
- The shared modifier serializer  reads `attr` i16, `type` i8,
  `value` u32 - the same element in both result packets.

---

## 12. Invariants worth keeping

1. `EvolvingExp` is **progress inside a grade**, never a running total.
2. A rung is priced by the grade it **leads into** (`grade_exp`); `req_exp` is dead data.
3. Grades are walked by **`grade_order`**, never by the numeric grade id.
4. A material's worth comes from **its own** pool at **its own** grade.
5. `gold_mul` is **per mille**, and banked experience is discounted **once**.
6. Effect magnitudes are **derived**, never stored - group ids only.
7. Negative effect values are real: guard on `!= 0` / `span != 0`, never `> 0`.
8. Every in-place change to a worn piece must call `UpdateGearBonuses` **and** republish
   the activation mask.
9. Weight 0 means rare, not impossible.
10. A pool charging an unwired currency is refused, never waived.

## 13. Known rough edge

The gold walk (`AccumulateBandPrice`) stops at the pool's shipped `max_evolving_grade`,
while the experience ladder (`GetExpToMaxGrade`, `ApplyExperience`) uses `GetLadderTop` -
the last rung that carries a real price. On the 360 pools where the ladder runs past the
shipped column, those extra grades are climbed but never billed. Deliberate on the
experience side; whether the price walk should follow the same ceiling has not been
decided.
