using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>damage_effects.use_element_effect</c> / <c>formulas</c> 65: the scale a specialised weapon
/// element adds after the effect's own multiplier.
/// </summary>
/// <remarks>
/// Formula 65 is
/// <c>(element_value / (element_value + 8000)) * element_effect_ratio *
/// (1 - element_resist_value / (element_resist_value + 200))</c>.
/// <c>element_value</c> is <c>item_elements.default_element_value + add_element_value * element_level</c>
/// for the attacker's weapon element; resist and ratio come from <c>armor_element_resists</c> for the
/// victim's armor type against that element. The factor is applied as <c>damage * (1 + factor)</c>, so
/// a missing table, a weapon with no element, or a row that clears the flag leaves the hit unchanged.
/// </remarks>
public static class ElementDamageRules
{
    /// <summary>Identity when the element term is not in play.</summary>
    public const float NeutralBonus = 0f;

    /// <summary>
    /// <c>armor_element_resists.effect_ratio</c> is authored 0..1000; formula 65 reads the ratio as a
    /// unit interval.
    /// </summary>
    public const float EffectRatioScale = 1000f;

    /// <summary>
    /// When the victim has no armor type (most NPCs), formula 65 still runs at full ratio with no resist —
    /// the type chart only applies once an armor type is known.
    /// </summary>
    public const float UnarmoredEffectRatio = 1f;

    public static int ElementValue(int defaultValue, int addValue, int elementLevel) =>
        defaultValue + addValue * Math.Max(0, elementLevel);

    public static int ElementResistValue(int defaultResist, int addResist, int armorElementLevel) =>
        defaultResist + addResist * Math.Max(0, armorElementLevel);

    public static float EffectRatio(int effectRatioPermille) =>
        effectRatioPermille / EffectRatioScale;

    /// <summary>
    /// Formula 65's result, or <see cref="NeutralBonus"/> when the row is absent or will not evaluate.
    /// </summary>
    public static float Factor(Formula formula, int elementValue, float elementEffectRatio, int elementResistValue)
    {
        if (formula == null || elementValue <= 0 || elementEffectRatio <= 0f)
            return NeutralBonus;

        var parameters = new Dictionary<string, double>(3, StringComparer.Ordinal)
        {
            ["element_value"] = elementValue,
            ["element_effect_ratio"] = elementEffectRatio,
            ["element_resist_value"] = Math.Max(0, elementResistValue)
        };

        return formula.TryEvaluate(parameters, out var value) ? (float)value : NeutralBonus;
    }

    /// <summary><c>damage * (1 + factor)</c>; factor 0 is bit-for-bit the input.</summary>
    public static float ApplyBonus(float damage, float factor) => damage * (1f + factor);

    /// <summary>
    /// Resolves the attacker's weapon element id and specialisation level for one hit.
    /// </summary>
    public static (uint ElementId, int ElementLevel) ResolveAttackerElement(
        Unit caster, int weaponSlotId, bool useMainhand, bool useOffhand, bool useRanged)
    {
        if (caster is Npc npc)
            return (npc.Template?.WeaponElementId ?? 0, npc.Template?.WeaponElementLevel ?? 0);

        Item weapon = null;
        if (weaponSlotId >= 0)
            weapon = caster.Equipment?.GetItemBySlot(weaponSlotId);
        else if (useMainhand)
            weapon = caster.Equipment?.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
        else if (useOffhand)
            weapon = caster.Equipment?.GetItemBySlot((int)EquipmentItemSlot.Offhand);
        else if (useRanged)
            weapon = caster.Equipment?.GetItemBySlot((int)EquipmentItemSlot.Ranged);

        if (weapon is not EquipItem equip || equip.Template is not WeaponTemplate wt)
            return (0, 0);

        return (wt.HoldableTemplate?.ElementId ?? 0, equip.ElementLevel);
    }

    /// <summary>
    /// Resolves the victim's armor type and element-resist level (NPC template fields, or the majority
    /// armor set's summed specialisation levels for a character).
    /// </summary>
    public static (uint ArmorTypeId, int ArmorElementLevel) ResolveVictimArmor(Unit victim)
    {
        if (victim is Npc npc)
            return (npc.Template?.ArmorTypeId ?? 0, npc.Template?.ArmorElementLevel ?? 0);

        if (victim is not Character)
            return (0, 0);

        var byType = new Dictionary<uint, int>();
        foreach (var item in victim.Equipment.Items)
        {
            if (item is not Armor armor || armor.Template is not ArmorTemplate template)
                continue;
            if (template.SlotTemplate?.SlotTypeId == (ulong)EquipmentItemSlotType.Back)
                continue;

            var typeId = template.KindTemplate?.TypeId ?? 0;
            if (typeId == 0)
                continue;

            byType.TryGetValue(typeId, out var sum);
            byType[typeId] = sum + armor.ElementLevel;
        }

        if (byType.Count == 0)
            return (0, 0);

        // Majority armor type (piece count), then the summed specialisation levels of that type.
        var counts = new Dictionary<uint, int>();
        foreach (var item in victim.Equipment.Items)
        {
            if (item is not Armor || item.Template is not ArmorTemplate template)
                continue;
            if (template.SlotTemplate?.SlotTypeId == (ulong)EquipmentItemSlotType.Back)
                continue;
            var typeId = template.KindTemplate?.TypeId ?? 0;
            if (typeId == 0)
                continue;
            counts.TryGetValue(typeId, out var n);
            counts[typeId] = n + 1;
        }

        uint bestType = 0;
        var bestCount = -1;
        var bestLevel = 0;
        foreach (var (typeId, levelSum) in byType)
        {
            var count = counts.GetValueOrDefault(typeId);
            if (count > bestCount || (count == bestCount && levelSum > bestLevel))
            {
                bestType = typeId;
                bestCount = count;
                bestLevel = levelSum;
            }
        }

        return (bestType, bestLevel);
    }

    /// <summary>
    /// Full factor for one live hit: content lookup + formula 65. Returns <see cref="NeutralBonus"/>
    /// when the flag is off, the weapon has no element, or the element row is missing.
    /// </summary>
    public static float FactorFor(
        bool useElementEffect,
        Unit caster,
        Unit victim,
        int weaponSlotId,
        bool useMainhand,
        bool useOffhand,
        bool useRanged)
    {
        if (!useElementEffect || caster == null || victim == null)
            return NeutralBonus;

        var (elementId, elementLevel) = ResolveAttackerElement(caster, weaponSlotId, useMainhand, useOffhand, useRanged);
        if (elementId == 0)
            return NeutralBonus;

        var element = ItemManager.Instance.GetItemElement(elementId);
        if (element == null)
            return NeutralBonus;

        var elementValue = ElementValue(element.DefaultElementValue, element.AddElementValue, elementLevel);
        var (armorTypeId, armorElementLevel) = ResolveVictimArmor(victim);

        float ratio;
        int resist;
        if (armorTypeId == 0)
        {
            ratio = UnarmoredEffectRatio;
            resist = 0;
        }
        else
        {
            var resistRow = ItemManager.Instance.GetArmorElementResist(armorTypeId, elementId);
            if (resistRow == null)
            {
                ratio = UnarmoredEffectRatio;
                resist = 0;
            }
            else
            {
                ratio = EffectRatio(resistRow.EffectRatio);
                resist = ElementResistValue(
                    resistRow.DefaultElementResistValue,
                    resistRow.AddElementResistValue,
                    armorElementLevel);
            }
        }

        return Factor(
            FormulaManager.Instance.GetFormulaOrNull(FormulaKind.ElementDamageEffect),
            elementValue,
            ratio,
            resist);
    }
}
