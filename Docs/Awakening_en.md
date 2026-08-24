# The Awakening System

How awakening works on this server: which recipes a scroll offers, the odds and the pity
counter behind an attempt, how a piece is rewritten in place, and what carries over into
the new tier.

> **Naming.** The client's *Awakening* tab is 각성 in the data and `item_change_mapping`
> everywhere in the code. Its neighbours in the same window are *Synthesis* = `evolving` and
> *Tempering* = `enchant_scale` / `refurbishment`.

---

## 1. Where it lives

| Concern | File |
| --- | --- |
| The awakening action | `AAEmu.Game/Models/Game/Skills/Effects/SpecialEffects/ItemChangeMapping.cs` |
| Recipe + group models | `AAEmu.Game/Models/Game/Items/ItemChangeMapping.cs` |
| Table loading, candidate lookup | `AAEmu.Game/GameData/ItemEnchantGameData.cs` -> `LoadChangeMappings`, `GetChangeMappings` |
| Synthesis ladder maths it leans on | `ItemEnchantGameData.GetCumulativeExp` / `PlaceCumulativeExp` / `TranslateGroupsToCategory` |
| Result packet | `AAEmu.Game/Core/Packets/G2C/SCItemChangeMappingResultPacket.cs` |
| Clearing a locked item | `.../SpecialEffects/RestoreDisableEnchant.cs` |

> The effect class **must** be named after its `SpecialType` for the reflection lookup in
> `SpecialEffect.Apply`, which collides with the recipe model of the same name - hence the
> `using ItemChangeMappingData = ...` alias at the top of the effect file.

---

## 2. Data model

| Table | What it carries |
| --- | --- |
| `item_change_mappings` | One **recipe**: `source_item_id`, `target_item_id`, `source_grade_id` (-1 = any grade), `target_grade_id` (-1 = carry the source's grade), `mapping_group_id` |
| `item_change_mapping_groups` | The **odds** behind a set of recipes: `success`, `disable`, `fail_bonus` (all per 10000), `selectable`, `evolving_exp_inherit` |

The awakening scroll's skill names the group through its special effect's **`value1`**.

### Candidate lookup

```csharp
GetChangeMappings(groupId, itemId, grade):
    every mapping with this source item
    filtered to this group
    filtered to source_grade_id == grade  (or -1)
```

**More than one candidate is normal** - that is exactly what the awaken tab's radio buttons
pick between.

---

## 3. What an item stores

| Field | Meaning |
| --- | --- |
| `MappingFailBonus` | The **pity counter** - how many failures this piece has accumulated. Carried in the detail block as its own byte |
| `EnchantDisabled` (`ItemFlag`) | Set by a failure that also locks the piece; blocks the whole enchant window until a restore item clears it |

Everything else an awakening touches (`TemplateId`, `Grade`, `EvolvingExp`,
`RndAttrGroupIds`, `EnchantScale`) belongs to the general item state.

---

## 4. One awakening attempt, start to finish

`ItemChangeMapping.Execute`:

1. **Validate** - character, item target, `EquipItem`, not `EnchantDisabled`.
2. **Resolve the group** from `value1`; **resolve the candidates** for this item and grade.
   No candidates means the scroll does not apply to this piece - the client filters that out
   itself, so it only fires on a stale window.
3. **Pick the candidate** (`PickCandidate`, section 5).
4. **Roll**:

   ```
   bonusRate = item.MappingFailBonus * group.fail_bonus
   succeeded = Random(10000) < group.success + bonusRate
   ```

   `bonusRate` is what the awaken tab shows as "bonusRate" next to the base chance, so it
   goes out in the result packet **either way**.
5. **On failure**:
   - `Random(10000) < group.disable` additionally locks the piece (`EnchantDisabled`).
   - `MappingFailBonus++`, capped at what a byte holds. A group with `fail_bonus = 0` never
     moves it anyway.
   - `SCItemDetailUpdatedPacket`, then `SCItemChangeMappingResultPacket` with the **same
     item sent twice** and result `Fail` / `FailDisableEnchant`.
6. **Class guard before applying a success**:

   ```csharp
   if (targetTemplate.ClassType != equipItem.GetType()) -> refuse
   ```

   Every shipped mapping stays inside one item class (weapon to weapon, cape to cape).
   Crossing classes would leave the stat code casting the wrong template, so the attempt
   stops rather than handing the player a broken item.
7. **On success** - `ApplySuccess`, section 6.

`ItemChangeMappingResult`: `Success = 0`, `Fail = 1`, `FailDisableEnchant = 2`
(`ICMR_*` in the client's Lua constants).

---

## 5. Which result the attempt aims at

```csharp
if (group.Selectable && skillObject is SkillObjectItemChangeMapping { MappingId: > 0 } choice)
    candidates.Find(c => c.Id == choice.MappingId)   // honoured only if genuinely a candidate
    ?? random candidate
else
    random candidate
```

The tab sends the chosen `item_change_mappings` **row id**. It is treated as a *request*,
not an instruction: the row is honoured only when it is genuinely one of the candidates
already established for this group, item and grade - so a hand-built request cannot name a
mapping off some other item. A non-selectable group hides the radio buttons and the server
rolls one itself.

---

## 6. Rewriting the piece - `ApplySuccess`

The item **keeps its id and its slot** and only changes what it is. That is what lets the
socket, dye and look state ride along untouched.

Order matters here, and the sequence is:

1. **Read `sourceCategoryId` before the rewrite** - the pool the piece is leaving is what
   its banked synthesis progress is measured against.
2. **Build a throwaway snapshot** of the old piece
   (`ItemManager.Create(oldTemplateId, 1, grade, false)`). The client reads the old grade and
   gear score off the result packet's *first* item, so it needs the piece as it was. No id is
   handed out for it, it never enters an inventory, and it lives only as long as the packet.
3. **Swap the template** (`TemplateId`, `Template`).
4. **Reset the pity counter** - `MappingFailBonus = 0`.
5. **Synthesis carry-over** - section 7.
6. **Clamp tempering** to the new template's ceiling:
   `if (EnchantScale > Template.MaxEnchantScaleId) EnchantScale = maxScale;`
7. **Publish** - section 8.

---

## 7. What carries over from synthesis

Controlled by the group's `evolving_exp_inherit`:

```csharp
if (!group.EvolvingExpInherit) { EvolvingExp = 0; RndAttrGroupIds = new uint[5]; }
```

### The grade

- A mapping that names `target_grade_id >= 0` wins outright - **none of the shipped ones
  do**.
- Otherwise the piece's **cumulative** synthesis total is rebuilt in the old pool and
  re-placed on the new pool's ladder:

  ```csharp
  carried = GetCumulativeExp(sourceCategoryId, grade, EvolvingExp);
  placed  = PlaceCumulativeExp(targetCategoryId, carried);
  Grade = placed.Grade; EvolvingExp = placed.SectionExp;
  ```

  An item only stores progress *inside* a grade, so the total has to be reassembled from the
  grade it is leaving and split again over the grade it arrives at.

  **The ladders are cut to line up.** A Brilliant Jerkin maxed at Unique has banked 1607, and
  1607 is exactly what the Hiram Jerkin's ladder charges to reach Arcane - the grade the
  awakening window previews. Across the shipped mappings, **7437 of 7658** land on an exact
  rung this way.

  Each tier's pool begins granting effects one rung higher than the last, and that rung is
  the entry point: the main-quest one-handed chain reads 2, 3, 4 across its tiers, which is
  the Arcane to Grand, Heroic to Rare and Unique to Arcane the previews show.

### The effect lines

They survive - the awakening window previews them re-valued for the grade the piece lands
on ("Spirit 27 to 44"), which works because an item stores an effect's **group**, not its
magnitude.

But they are **re-homed by attribute**, not carried across as ids:

```csharp
carried = TranslateGroupsToCategory(UsedRndAttrGroupIds, targetCategoryId);
cap     = GetRndAttrCap(targetCategoryId, Grade);
// keep min(cap, carried.Count) of them, then:
ItemEvolving.TopUpAttributes(equipItem, targetCategoryId);
```

A group id only means something inside the pool it came from - both its magnitude *and* the
bundle that owns it. An id carried across unchanged leaves an effect the new pool cannot
value and, worse, one that **no bundle recognises as its own** - so the bundle that already
supplied it hands out a second of the same kind, which is how a piece ends up wearing two
main stats. An attribute the new pool does not offer at all is dropped; `TopUpAttributes`
then fills whatever room the new grade allows.

`EvolveChance` (the synthesis change attempts) rides along untouched - it belongs to the
piece, not the tier.

---

## 8. What goes back to the client

| Packet | Why |
| --- | --- |
| `SCItemTaskSuccessPacket(ItemTaskType.EnchantMagical /* 51 */, [ItemAdd(equipItem)], [])` | The item is **re-stated, not swapped** |
| `SCItemDetailUpdatedPacket` | On failure only - the pity counter changed |
| `SCItemChangeMappingResultPacket` | `oldItem` struct, `newItem` struct, `bonusRate` i32, `result` u8 |

**Why re-stated and not swapped.** The `Take` body carries the full item and overwrites the
entry already in that slot. Pairing it with a removal - the obvious way to express "this
became something else" - **destroys the item instead**: `Seize` names the id in its remove
field, and the client acts on that whatever follows it in the same packet.

**The result packet's shape** (10.0.2.13 serializer, x2game.dll rva `0xab59b0`): two item
structs back to back (the second at struct `+0xe0`, directly after the first), an i32 at
`+0x1b4`, and the `result` byte last. The client turns it into
`ITEM_CHANGE_MAPPING_RESULT(result, oldGrade, oldGearScore, itemLink, bonusRate)` - the
first item supplies the old grade and gear score, the second is what the player ends up
holding. **Sending the same item twice on a failure is what the client expects**; it still
needs a valid second item to build the link from.

`UpdateGearBonuses(null, null)` runs on success when the piece is worn, since gear bonuses
are otherwise only summed at equip time.

---

## 9. Failure states and recovery

| Outcome | Effect on the piece |
| --- | --- |
| `Fail` | `MappingFailBonus++` - strictly better odds next time |
| `FailDisableEnchant` | The same, plus `EnchantDisabled` - the piece is out of the enchant window until unlocked |
| `Success` | `MappingFailBonus = 0` |

`RestoreDisableEnchant` clears the lock. It refuses on an item that is not actually
disabled, so the restore item is not burned for nothing.

---

## 10. Invariants worth keeping

1. `value1` on the scroll's special effect is the **mapping group**, and the group holds the
   odds - the recipes hold none.
2. A player's pick is a request: it must be one of the candidates already resolved for this
   group, item and grade.
3. `bonusRate = MappingFailBonus * fail_bonus` goes out on **every** result, success or not.
4. Never cross item classes, even if a data row asks for it.
5. The old piece has to be snapshotted **before** the rewrite; the client reads the old grade
   and gear score off it.
6. Success is published as `ItemAdd` on an `EnchantMagical` task - **never** paired with a
   removal, which destroys the item.
7. Both result-packet item slots must be filled, even on a failure.
8. Synthesis progress crosses as a **cumulative total**, and effect lines cross **by
   attribute**, never by group id.
9. Tempering is clamped to the new template's ceiling.
10. `target_grade_id` overrides the ladder placement - and no shipped mapping uses it.
