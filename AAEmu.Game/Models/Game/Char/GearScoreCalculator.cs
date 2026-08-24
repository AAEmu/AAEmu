using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Server-side gear score, matching the client's own display math.
///
/// Per equipped piece, one of the shipped <c>formulas</c> rows is evaluated:
/// kind 30 (weapons, from holdables), kind 56 (armor), kind 57 (accessories) —
/// then kind 31 per socketed gem socket plus kind 32 per gem, both using the
/// piece's level. The unit's score is the sum over all equipped pieces.
/// </summary>
public static class GearScoreCalculator
{
    /// <summary>World/level scaling factor; 1.0 until scaled content exists.</summary>
    public const double DefaultScalingMultiplier = 1.0;

    /// <summary>
    /// Gear score of one equipped piece, or 0 when the piece is not gear.
    /// </summary>
    public static double EvaluateItem(Item item)
    {
        if (item is not EquipItem equip || equip.Template is not ItemTemplate template)
            return 0;

        var level = (double)template.Level;
        // Grade channel multiplier (0.8 poor .. 2.1 arche-eternal); all var_* columns agree per row.
        var gradeTemplate = ItemManager.Instance.GetGradeTemplate(equip.Grade);
        var gradeMultiplier = gradeTemplate?.HoldableDps ?? 1.0;

        var parameters = new Dictionary<string, double>
        {
            ["item_level"] = level,
            ["item_grade"] = gradeMultiplier,
            ["scaling_multiplier"] = DefaultScalingMultiplier,
            ["element_level"] = equip.ElementLevel,
        };

        FormulaKind kind;
        switch (template)
        {
            case WeaponTemplate weapon:
                parameters["gear_score_multiplier"] = ItemManager.Instance.GetHoldable(weapon.HoldableTemplate?.Id ?? 0)?.GearScoreMultiplier ?? 0;
                kind = FormulaKind.GearScoreWeaponArmorAcc;
                break;
            case ArmorTemplate armor:
                parameters["gear_score_multiplier"] = NormalizeSlotMultiplier(ItemManager.Instance.GetWearableSlot(armor.SlotTemplate?.SlotTypeId ?? 0)?.GearScoreMultiplier ?? 0);
                kind = FormulaKind.GearScoreArmor;
                break;
            case AccessoryTemplate accessory:
                parameters["gear_score_multiplier"] = NormalizeSlotMultiplier(ItemManager.Instance.GetWearableSlot(accessory.SlotTemplate?.SlotTypeId ?? 0)?.GearScoreMultiplier ?? 0);
                kind = FormulaKind.GearScoreAccessory;
                break;
            default:
                return 0; // non-equip gear (cosmetics, backpacks) carries no score
        }

        var score = FormulaManager.Instance.GetFormula((uint)kind)?.Evaluate(parameters) ?? 0;

        // Socketed gems: kind 31 (socket) + kind 32 (gem) per filled socket, at the piece's level.
        var gemCount = equip.GemIds?.Count(id => id != 0) ?? 0;
        if (gemCount > 0)
        {
            var socketParams = new Dictionary<string, double> { ["item_level"] = level };
            score += gemCount * (FormulaManager.Instance.GetFormula((uint)FormulaKind.GearScoreSocket)?.Evaluate(socketParams) ?? 0);
            score += gemCount * (FormulaManager.Instance.GetFormula((uint)FormulaKind.GearScoreEnchantingGem)?.Evaluate(socketParams) ?? 0);
        }

        return score;
    }

    /// <summary>
    /// Total gear score across a character's equipped pieces.
    /// </summary>
    public static int Evaluate(Character character)
    {
        if (character?.Inventory?.Equipment == null)
            return 0;

        double total = 0;
        foreach (var item in character.Inventory.Equipment.Items)
        {
            if (item != null)
                total += EvaluateItem(item);
        }

        return (int)Math.Round(total);
    }

    /// <summary>
    /// wearable_slots stores the multiplier ×10000 for the special cosmetic tiers
    /// (0.01/0.02 in the formula) and plain weights elsewhere; the shipped rows are
    /// already in formula units, so pass through unchanged. Kept as a named hop so a
    /// future unit fix has one place to land.
    /// </summary>
    private static double NormalizeSlotMultiplier(int raw) => raw;
}
