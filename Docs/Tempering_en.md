# The Tempering System

How tempering ("refurbishment", the `+N` on a piece of gear) works on this server: the
ladder it climbs, what a step costs, what a failure can do, and how the `+N` turns into
actual stats.

> **Naming.** The client's *Tempering* tab is 연마 in the data and `refurbishment` in the
> code and packets. The per-item state is called `enchant_scale` / `item_cap_scale`
> depending on which table you are looking at. Its neighbours in the same window are
> *Synthesis* = `evolving` and *Awakening* = `item_change_mapping`.

---

## 1. Where it lives

| Concern | File |
| --- | --- |
| The tempering action itself | `AAEmu.Game/Models/Game/Skills/Effects/SpecialEffects/ItemRefurbishment.cs` |
| Legacy 1.2 action (no rows in 10.0.2.13) | `.../SpecialEffects/ItemCapScale.cs`, `ItemCapScaleReset.cs` |
| Clearing a locked item | `.../SpecialEffects/RestoreDisableEnchant.cs` |
| Ladder + ban list loading | `AAEmu.Game/GameData/ItemEnchantGameData.cs` -> `LoadEnchantScaleRatios` |
| Ladder row model | `AAEmu.Game/Models/Game/Items/EnchantScaleRatio.cs` |
| Per-item state, stat multiplier | `AAEmu.Game/Models/Game/Items/EquipItem.cs` |
| Where the multiplier is applied | `AAEmu.Game/Models/Game/Items/Weapon.cs`, `Armor.cs` |
| Packets | `AAEmu.Game/Core/Packets/G2C/SCItemRefurbishmentResultPacket.cs`, `SCScaleEnchantBroadcastPacket.cs` |

**`ItemRefurbishment` is the one that ships.** 10.0.2.13 carries 15 `special_effects` rows
of that type, one per polish item. The older `ItemCapScale` / `ItemCapScaleReset` pair has
no rows left in this build and is kept only for older data sets.

Effect values on the polish:

| Value | Meaning |
| --- | --- |
| `value1` | Kind of gear the polish works on - 1 weapons, 2 armor. Goes out in the result packet's untitled int |
| `value2` | Non-zero on the "shining" variants - **the only ones that may overshoot by +2** |
| `value4` | 30, matching the top of the shipped ladder |

---

## 2. Data model

| Table | What it carries |
| --- | --- |
| `enchant_scale_ratios` | One **rung**: `name` ("+7"), `scale`, `success_ratio`, `grate_success_ratio` (the typo is the client's), `break_ratio`, `disable_ratio`, `down_ratio`, `down_max`, `cost`, `currency_id`. All ratios are **per 10000** |
| `item_cap_scale_forbids` | Items barred from tempering outright |
| `equip_slot_enchanting_costs` | Per-slot cost factor for the price formula |
| `items.max_enchant_scale_id` | The per-item ceiling - **12** wherever it is set at all |

Row 0 is called "none" in the data and reads back as `+0`. "+30" is listed twice at the top
of the ladder; the first row wins.

The shipped ladder's `scale` column runs `0, 10, 20 … 250` - ten times the rung number.
That distinction matters in two places (sections 5 and 6).

---

## 3. What an item stores

`EquipItem.EnchantScale` - a `ushort` holding an `enchant_scale_ratios` **row id**, so 0 is
untempered and the ceiling is the template's `MaxEnchantScaleId`.

On the wire it rides the u16 the detail serializer only calls `type`, at detail struct
`+0x3c`. The awakening preview reads that same word as a scale - it compares it against a
bound and clamps it to build the "+3 > +5" line (x2game.dll rva `0x12d8af` / `0x12d948`),
which is what both a generic `type` name and a 0-31 row id fit.

> The v1.2 layout put a **rune id** in that word instead. A template id does not survive
> being clamped like a scale, so the lunastone moved to its own slot in the detail blob
> (`EquipItem.RuneId`) rather than sharing this one.

A failed attempt can also set `ItemFlag.EnchantDisabled` (see section 4), which locks the
piece out of the whole enchant window until a restore item clears it.

---

## 4. One tempering attempt, start to finish

`ItemRefurbishment.Execute`:

1. **Validate** - character, item target, `EquipItem`, not `EnchantDisabled`.
2. **Two gates on what may be tempered at all**, and both have to be asked:
   - `MaxEnchantScaleId == 0` or the piece is already at it, **and**
   - `IsCapScaleForbidden(templateId)` - `item_cap_scale_forbids` is its own rule, not a
     restatement of the ceiling. Most items on it carry no ceiling anyway, but a couple do.

   Failing either sends `ErrorMessageType.GradeEnchantMax`. The client greys its own button
   here, so this only catches a stale window or a hand-built request.
3. **Read the rung** the item is currently on (`GetEnchantScaleRatio(beforeScale)`).
4. **Charge** (section 5) - before the roll, so a player who cannot cover it keeps both
   their coin and their polish.
5. **Roll** (section 6).
6. **Publish** (section 7).
7. **Quest hook** - `OnEnchantScale` fires with `Count = 1` on *every* outcome:
   `QuestActObjEnchantScaleCount` asks for a temper to be *used* ("Use a Temper x3"), and a
   failed or breaking attempt consumed the temper just the same.

---

## 5. Cost - formula 59

`TemperCost`, evaluating `enchant_scale_cost`:

```
formula 59 = FormulaKind.EnchantScaleCost
parameters:
    item_level             = item.Template.Level
    scale_cost             = ratio.Cost                        // the rung's own price
    equip_slot_enchant_cost = equip_slot_enchanting_costs[slotTypeId].Cost
    enchant_scale_cost_mul  = 0                                // no shipped table supplies it

cost = formula.Evaluate(parameters)     // NaN or <= 0 -> free
```

The slot id comes off the template (`WeaponTemplate.HoldableTemplate`,
`ArmorTemplate.SlotTemplate`, `AccessoryTemplate.SlotTemplate`).

Charged with `SubtractMoney(SlotType.Inventory, cost)` and **deliberately without an item
task type** - the same way regrade books its charge. Naming a task type here leaves the
purse on screen untouched until the next relog, so the coin goes without the player being
shown it going.

---

## 6. The roll

`Roll(ratio, item, maxScale, greatAllowed, out itemBroken)` - every ratio is per 10000 and
is read off **the rung the item is currently on**.

```
if Random(10000) < ratio.success_ratio:
    step = (greatAllowed and Random(10000) < ratio.grate_success_ratio) ? 2 : 1
    EnchantScale = min(maxScale, EnchantScale + step)
    -> Success / GreatSuccess

// failure, one roll read against cumulative bands:
roll = Random(10000)
roll < break_ratio                                   -> Break     (item destroyed)
roll < break_ratio + disable_ratio                   -> Disable   (EnchantDisabled = true)
roll < break_ratio + disable_ratio + down_ratio      -> Downgrade (-max(1, down_max))
otherwise                                            -> Fail      (nothing lost but the polish)
```

- **Great Success is gated by the polish, not the ladder.** `greatAllowed = value2 != 0`.
  The ladder offers the chance on every rung and cannot tell the two polish kinds apart, so
  the polish does: the shining variants promise the +2 in their tooltip, the plain ones
  preview Great Success at a flat 0%.
- **The shipped ladder is forgiving below +18.** `break_ratio` and `disable_ratio` are zero
  everywhere, and a downgrade only starts applying from +18 up. Below that a failed attempt
  costs nothing but the material.
- A **Break** removes the item via `ItemTaskType.ScaleCap` (127); `afterScale` is then
  reported as 0.

`ItemGradeEnchantResult`: `Break = 0`, `Downgrade = 1`, `Disable = 2`, `Fail = 3`,
`Success = 4`, `GreatSuccess = 5`, `RestoreDisable = 7`.

---

## 7. What goes back to the client

| Packet | Why |
| --- | --- |
| `SCItemDetailUpdatedPacket` | The piece itself, on its **own** packet - an `UpdateDetail` task carries an array the client does not decode as a detail, which leaves the item drawn as broken |
| `SCItemTaskSuccessPacket(ItemTaskType.ScaleCap, [], [])` | The tempering tab counts an attempt as settled only once a task of **this exact number** (127) says so. The list stays **empty on purpose** - the piece already went out above, and handing the same blob over twice is what breaks its drawing |
| `SCItemRefurbishmentResultPacket` | `result` i8, item struct, `scaleType` i32 (= the polish's `value1`), then `beforeScale` and `afterScale` as i16 |
| `SCScaleEnchantBroadcastPacket` | Server-wide notice: `charName`, `result` i8, item struct, two i16 scales |

Two things that cost time once:

- **Report the rung, not the `scale` column.** The ladder's own `scale` runs ten times the
  rung number, so sending it turned a `+1 -> +3` step into "+10 -> +30".
- **The broadcast threshold is read off the ladder**, not picked:
  `BroadcastFromScale()` returns the first rung whose `success_ratio` is below 10000 - i.e.
  `+10`, since everything up to +9 succeeds outright. A fixed threshold borrowed from
  regrade sat at +15, which **no item can reach** (`max_enchant_scale_id` is 12), so the
  broadcast never fired once. Only `Success` and `GreatSuccess` broadcast, and never a break.

---

## 8. How `+N` becomes stats

`EquipItem` turns the rung into a percentage multiplier:

```csharp
ScaleMultiplier = EnchantScale == 0 ? 0 : (ushort)(100 + GetEnchantScaleValue(scale) / 10);
TemperPhysical => ScaleMultiplier;
TemperMagical  => ScaleMultiplier;   // the same step drives both
```

The shipped ladder carries `scale` 10 per step, so `+12` lands at **112** - a 12% bonus.

Applied in `Weapon.cs` and `Armor.cs`, and **only above 100**:

```csharp
if (TemperPhysical > 100)
    result *= TemperPhysical / 100.0f;
```

Weapon uses it on DPS and both magical figures; armor on its physical and magical defence.
An untempered piece has a multiplier of 0, which the `> 100` guard skips - that guard is
what makes the "0 means untempered" encoding safe.

> Worth remembering: the client computes gear stats locally from its own tables. Tempering
> reaches the character sheet because the scale word travels in the item's detail block, not
> because the server sends any computed number.

---

## 9. Unlocking a disabled item

`RestoreDisableEnchant` clears `ItemFlag.EnchantDisabled` for a piece that a failed
awakening or a failed high-scale temper locked. It refuses on an item that is not actually
locked, so the restore item is not burned for nothing.

---

## 10. Legacy path (`ItemCapScale` / `ItemCapScaleReset`)

Kept only for pre-10.0 data sets:

- `ItemCapScale` rolls `Random(value1, value2)` - the range the old per-skill
  `item_cap_scales` lookup carried - and **sets** the scale straight to that, clamped to the
  template ceiling. No cost, no failure, no ladder.
- `ItemCapScaleReset` sets the scale back to 0.

Both publish only `SCItemDetailUpdatedPacket`. `ItemTaskType.ScaleCapReset` is 128.

---

## 11. Invariants worth keeping

1. `EnchantScale` is a **row id**, not a stat. The ladder's `scale` column is ten times the
   rung and must never be sent as `+N`.
2. Both gates - `max_enchant_scale_id` **and** `item_cap_scale_forbids` - have to be asked.
3. Great Success comes from the polish (`value2`), never from the ladder alone.
4. The charge is taken before the roll, without an item task type.
5. `ItemTaskType.ScaleCap` = 127 is matched literally by the client; the item list on it
   stays empty.
6. The piece goes out on `SCItemDetailUpdatedPacket` and nowhere else.
7. The stat multiplier is only applied above 100, which is what keeps `0 = untempered` safe.
