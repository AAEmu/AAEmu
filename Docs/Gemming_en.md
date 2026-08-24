# The Gemming System

How lunagem socketing and the lunastone work on this server: how many sockets a piece has,
what an attempt costs, what a failure can destroy, how the gems reach the character sheet,
and what the window needs to hear before it will take another press.

> **Naming.** Two different things share this window. **Lunagems** are the socketed gems -
> up to nine per piece, held in `EquipItem.GemIds` - and go through `ItemSocketing`. The
> **lunastone** ("magical enchant") is a single separate value, `EquipItem.RuneId`, set
> through a world interaction rather than a special effect.

---

## 1. Where it lives

| Concern | File |
| --- | --- |
| Seating and clearing lunagems | `AAEmu.Game/Models/Game/Skills/Effects/SpecialEffects/ItemSocketing.cs` |
| The lunastone | `AAEmu.Game/Models/Game/World/Interactions/MagicalEnchant.cs` |
| Socket limits, odds, break rules | `AAEmu.Game/Core/Managers/ItemManager.cs` |
| Per-item state + wire/DB layout | `AAEmu.Game/Models/Game/Items/EquipItem.cs` |
| Gem stats folded into the character | `AAEmu.Game/Models/Game/Units/Unit.cs` -> `UpdateGearBonuses` |
| Packets | `AAEmu.Game/Core/Packets/G2C/SCItemSocketingLunagemResultPacket.cs`, `SCItemSocketingLunastoneResultPacket.cs` |

---

## 2. Data model

| Table | What it carries |
| --- | --- |
| `item_socket_num_limits` | `(slot_id, grade_id) -> num_socket`. The piece's **slot** decides the shape of the run and its **grade** how far along it opens - from nothing on a poor item to nine on the highest |
| `item_socket_chances` | One row per "scale type", with columns `socket0 … socket9` - the chance of landing the Nth gem - plus `fail_break` |
| `item_sockets` | `item_id -> item_socket_chance_id`: **each lunagem names its own row of odds** |

### The two traps in `item_socket_chances`

**Column indexing.** 10.0.2.13 changed this table from per-row `(num_sockets,
success_ratio)` to the wide format above. `socket{N}` is the chance of landing the Nth gem,
so the columns are keyed by their own number and `GetSocketChance`'s "sockets already
filled, plus one" lands on the right one. Shifting them up by one instead pointed the first
gem at `socket0`, which is **zero in every shipped row** - so a first gem could never be
socketed at all.

**Per-gem rows.** 494 of the shipped gems sit on the "v2" rows, which succeed outright on
every socket; the older "default" row is the one whose odds fall away after the first gem.
Reading every gem off that older row made most of the game's gems a coin flip they were
never meant to be.

```csharp
GetSocketChance(gemTemplateId, filledSockets):
    row = item_sockets[gemTemplateId]                  // the gem's own odds
    return socketChanceRows[row][filledSockets + 1]    // fall back to the first row's column
```

`GetSocketFailBreaks(gemTemplateId)` reads `fail_break` off that same row - and defaults to
**true** for a gem with no row of its own.

---

## 3. What an item stores

| Field | pisc slot | Meaning |
| --- | --- | --- |
| `GemIds[9]` | 4-12 | The socketed lunagems, by item template id, in socket order |
| `RuneId` | 1 | The lunastone applied to the piece |

Both live in the 18-value `pisc` detail block (`EquipItem.WriteDetails` / `ReadDetails`),
which is also the `items.details` blob format.

> `GemIds` used to live **only in memory**: filled when a gem was set, read when gear
> bonuses were totalled, but never written into the block - so the gems reached neither the
> database nor the client and were gone at the next restart.

`SocketSlots` is **9**. The client's own chance table carries ten columns, but the detail
block's run between the dye and the synthesis effect lines is nine, and no shipped
`item_socket_num_limits` row asks for more than six.

`RuneId` has its own slot rather than sharing the u16 at struct `+0x3c`: that word is what
the client reads as the **tempering scale**, and a template id does not survive being
clamped like one.

---

## 4. One socketing attempt, start to finish

`ItemSocketing.Execute`. The cast carries the **gem as the caster item** (`SkillItem`) and
the **gear as the target** (`SkillCastItemTarget`).

The very first branch splits on the gem's template id:

```csharp
if (gemItem.TemplateId != Item.DawnStone)   // 327
```

### 4.1 Seating a lunagem

1. **Count the filled sockets** - the number of non-zero entries in `GemIds`.
2. **Check the limit** - `GetSocketNumLimit(slotTypeId, grade)`, with the slot id taken off
   the template (`WeaponTemplate.HoldableTemplate`, `ArmorTemplate.SlotTemplate`,
   `AccessoryTemplate.SlotTemplate`). The window greys its own button when the run is full,
   so this only catches a stale window or a hand-built request.
3. **Charge** (section 5) - before the roll. A player who cannot cover it keeps both their
   coin and their gem; a failure that clears the piece is still paid for, the same way a
   failed regrade is.
4. **Roll**:

   ```
   Random(10000) < GetSocketChance(gemTemplateId, filledSockets)
       -> GemIds[filledSockets] = gemTemplateId;  result = 1
   otherwise, and only if GetSocketFailBreaks(gemTemplateId):
       -> every entry in GemIds is cleared
   ```

   A failure on a non-breaking row costs nothing but the gem and the fee.
5. `installed = true` either way - the flag says an install was *attempted*, not that it
   worked.

### 4.2 Clearing with a Dawnstone

Item template **327**. Wipes every entry in `GemIds` and reports `result = 1`. No cost, no
roll, no limit check.

### 4.3 After either branch

- `IsDirty = true`.
- `SCItemDetailUpdatedPacket` - and **nothing else carrying the piece** (section 6).
- `UpdateGearBonuses(null, null)` if the piece is currently worn, since gear bonuses are
  otherwise only summed at equip time.
- The result packet, plus the run-closing second one (section 7).
- `SCSkillEndedPacket` broadcast.

---

## 5. Cost - formula 38

`SocketingCost`, evaluating `item_socketing_cost`:

```
formula 38 = FormulaKind.ItemSocketingCost
parameters:
    item_level              = equipItem.Template.Level     // the gear's level
    socket_item_level       = gemItem.Template.Level       // the gem's own level
    item_used_socket        = filled sockets
    item_socketing_cost_mul = 0        // no shipped table carries it, so it stays neutral

cost = formula.Evaluate(parameters)    // NaN or <= 0 -> free
```

So the price climbs with the gear's level, the gem's level **and** how many sockets are
already filled. Taken with `SubtractMoney(SlotType.Inventory, cost)`.

---

## 6. How gem stats reach the character

There is no gem-specific stat path. In `Unit.UpdateGearBonuses`, for every worn `EquipItem`:

```csharp
foreach (var gem in ei.GemIds)
    foreach (var template in ItemManager.Instance.GetUnitModifiers(gem))
        AddBonus(GearBonusesIndex, new Bonus { Template = template, Value = template.Value });
```

The gem's template id is looked up in the same `item_unit_modifiers` the gear itself uses.
Nothing gates this on a client-side activation byte - which is why gems totalled correctly
on the character sheet even while the synthesis lines did not (see the synthesis document,
the activation mask).

---

## 7. What the window needs to hear

`SCItemSocketingLunagemResultPacket` - the client calls it `SCItemSocketingResultPacket`.
Field order and widths come from the client's own serializer (x2game.dll rva `0xa9c530`),
which names each value as it writes it:

```
u8   result        // 0 = failure, 1 = success
u64  itemId
u32  type          // the gem's template id
u8   kind          // seating a gem vs clearing the piece
bool success
```

> `kind` was **missing** for a long time. Sending four fields put the install flag where the
> client reads `kind` and left it reading `success` off the end of the packet - which is why
> the gear window never came back for a second attempt.

### The multi-attempt tally

The tab has a spinner: the player can ask for up to six gems in one press. That count
arrives in the cast's extra values - a byte for "all", then the count as an int, so a press
for three shows up as `0x00000300` in the first of the thirteen values:

```csharp
requested = (extras.Values[0] >> 8) & 0xFF;
```

The tab keeps that tally and **takes no further press while it stands above zero**. A
successful result takes one off it; only an unsuccessful one clears it outright. One cast
seats one gem, so a press for several would leave the rest of the tally hanging and the tab
shut. Closing the run off explicitly hands it straight back:

```csharp
if (requested > 1 && result != 0)
    send SCItemSocketingLunagemResultPacket(0, itemId, gemTemplateId, kind, false);
```

### Why the piece goes out only on the detail packet

Handing the same blob to the window a second time as a task item is what leaves it **drawn
as broken**. The window learns the cast is over from the skill's own reagent task
(`ItemTaskType.SkillReagents` = 42) and re-reads the piece out of the bag itself, so it does
not need to be told. Tempering has always published this way and its piece survives;
socketing did not, and its piece did not.

---

## 8. The lunastone

`MagicalEnchant`, a world interaction rather than a special effect - so it runs through a
doodad/skill interaction path, not `SpecialEffect.Apply`:

```csharp
equipItem.RuneId = skillItem.ItemTemplateId;
SCItemSocketingLunastoneResultPacket(true, itemId, templateId);   // bool, u64, u32
SCItemTaskSuccessPacket(ItemTaskType.EnchantMagical /* 51 */, [ItemUpdate(equipItem)], []);
```

No roll, no cost, no failure state. It reads the target out of `Inventory.Bag` only, so a
worn piece is not a valid target on this path. Nothing reads `RuneId` back for stats yet -
it is stored, persisted and shown, and that is the whole of it.

---

## 9. Invariants worth keeping

1. `socket{N}` columns are keyed by their **own** number; `filledSockets + 1` is the index.
2. Every gem's odds come from **its own** `item_sockets` row, not from the first row.
3. `fail_break` is per row and is **not** the common case - do not assume a failure wipes
   the piece.
4. The socket count comes from `(slot_id, grade_id)`, so the same piece gains sockets purely
   by being regraded.
5. `GemIds` must be written into the detail block, or it exists only until the next restart.
6. The gem run is 9 slots (pisc 4-12) and sits between the dye and the synthesis lines.
7. The piece goes out on `SCItemDetailUpdatedPacket` and **never** additionally as a task
   item.
8. `kind` is a real field in the result packet; dropping it desynchronises everything after
   it.
9. A multi-gem press needs the closing `result = 0` packet, or the tab stays shut.
10. Dawnstone is item **327** and is checked by template id, not by any flag.
