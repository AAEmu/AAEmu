using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Server-side gear score, matching the client's own display math.
///
/// Per equipped piece, one of the shipped <c>formulas</c> rows is evaluated:
/// <see cref="FormulaKind.GearScoreWeaponArmorAcc"/> (weapons, from holdables),
/// <see cref="FormulaKind.GearScoreArmor"/>, <see cref="FormulaKind.GearScoreAccessory"/> —
/// then <see cref="FormulaKind.GearScoreSocket"/> plus
/// <see cref="FormulaKind.GearScoreEnchantingGem"/> once per filled socket, using that stone's level.
/// The unit's score is the sum over all equipped pieces.
/// </summary>
public static class GearScoreCalculator
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>World/level scaling factor; 1.0 until scaled content exists.</summary>
    public const double DefaultScalingMultiplier = 1.0;

    /// <summary>
    /// One piece's (or a character's) score: the total the boards order by, and the same figure
    /// without socketed gems — the ranking window paints those as <c>bare + (total - bare)</c>.
    /// </summary>
    public readonly record struct GearScoreParts(double Total, double Bare)
    {
        public long RoundedBare => (long)Math.Round(Bare);

        /// <summary>The stone points the window prints beside the piece. Rounded on their own.</summary>
        public long RoundedGems => (long)Math.Round(Total - Bare);

        public long RoundedTotal => RoundedBare + RoundedGems;
    }

    /// <summary>
    /// Adds gem contribution onto a piece score. Bare stays the piece; total is piece plus gems.
    /// </summary>
    public static GearScoreParts Combine(double piece, double gems) => new(piece + gems, piece);

    /// <summary>
    /// Gear score of one equipped piece, or 0 when the piece is not gear.
    /// </summary>
    public static double EvaluateItem(Item item) => EvaluateItemParts(item).Total;

    /// <summary>
    /// One equipped piece split into the score without gems and the score with them.
    /// </summary>
    /// <param name="slotGainItemLevel">
    /// The slot ladder's <c>gain_item_level</c> at the level the client shows. Zero when the slot has
    /// not been reinforced. It is not added as-is: the reinforce item-level formula turns it into the
    /// amount actually added to the piece's level.
    /// </param>
    public static GearScoreParts EvaluateItemParts(Item item, double slotGainItemLevel = 0)
    {
        if (item is not EquipItem equip || equip.Template is not ItemTemplate template)
            return default;

        var level = ItemLevelForScore(template.Level, slotGainItemLevel, SlotLevelBonus);
        // Grade channel multiplier (0.8 poor .. 2.1 arche-eternal); all var_* columns agree per row.
        var gradeTemplate = ItemManager.Instance.GetGradeTemplate(equip.Grade);
        var gradeMultiplier = gradeTemplate?.HoldableDps ?? 1.0;

        var parameters = new Dictionary<string, double>
        {
            ["item_level"] = level,
            ["item_grade"] = gradeMultiplier,
            ["scaling_multiplier"] = TemperMultiplier(equip),
            ["element_level"] = equip.ElementLevel,
        };

        FormulaKind kind;
        switch (template)
        {
            case WeaponTemplate weapon:
                parameters["gear_score_multiplier"] = FromStoredMultiplier(ItemManager.Instance.GetHoldable(weapon.HoldableTemplate?.Id ?? 0)?.GearScoreMultiplier ?? 0);
                kind = FormulaKind.GearScoreWeaponArmorAcc;
                break;
            case ArmorTemplate armor:
                parameters["gear_score_multiplier"] = FromStoredMultiplier(ItemManager.Instance.GetWearableSlot(armor.SlotTemplate?.SlotTypeId ?? 0)?.GearScoreMultiplier ?? 0);
                kind = FormulaKind.GearScoreArmor;
                break;
            case AccessoryTemplate accessory:
                parameters["gear_score_multiplier"] = FromStoredMultiplier(ItemManager.Instance.GetWearableSlot(accessory.SlotTemplate?.SlotTypeId ?? 0)?.GearScoreMultiplier ?? 0);
                kind = FormulaKind.GearScoreAccessory;
                break;
            default:
                return default; // non-equip gear (cosmetics, backpacks) carries no score
        }

        var piece = TruncateTenth(FormulaManager.Instance.GetFormula((uint)kind)?.Evaluate(parameters) ?? 0);
        var socket = FormulaManager.Instance.GetFormula((uint)FormulaKind.GearScoreSocket);
        var gems = ScoreGems(GemItemLevels(equip.GemIds, GemLevel),
            gemLevel => TruncateTenth(socket?.Evaluate(new Dictionary<string, double> { ["item_level"] = gemLevel }) ?? 0),
            _ => 0);

        return Combine(piece, gems);
    }

    /// <summary>
    /// Piece level the gear-score formulas see. <paramref name="slotBonus"/> is the reinforce formula's
    /// result, so a slot whose ladder gain is 2.5 does not become level + 2.5.
    /// </summary>
    public static double ItemLevelForScore(double templateLevel, double slotGainItemLevel, Func<double, double, double> slotBonus)
    {
        if (slotGainItemLevel == 0 || slotBonus == null)
            return templateLevel;

        return templateLevel + slotBonus(templateLevel, slotGainItemLevel);
    }

    /// <summary>The reinforce formula's item-level bonus, or 0 when that formula is not loaded.</summary>
    public static double SlotLevelBonus(double itemLevel, double gainItemLevel)
    {
        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.EquipSlotReinforceGainItemLevel);
        if (formula == null)
        {
            Logger.Warn("Gear score: reinforce item-level formula is missing, so a piece at level {0} gets no slot bonus", itemLevel);
            return 0;
        }

        return formula.Evaluate(new Dictionary<string, double>
        {
            ["item_level"] = itemLevel,
            ["gain_item_level"] = gainItemLevel,
        });
    }

    /// <summary>
    /// Tempering scale as the formula's scaling factor. An untempered piece stays at 1. A step whose
    /// scale column is 200 (+20) is 1.2.
    /// </summary>
    public static double TemperMultiplier(int scale) => scale <= 0 ? 1d : (1000d + scale) / 1000d;

    private static double TemperMultiplier(EquipItem equip)
    {
        if (equip.EnchantScale == 0)
            return 1d;

        return TemperMultiplier(ItemEnchantGameData.Instance.GetEnchantScaleValue((byte)equip.EnchantScale));
    }

    /// <summary>Keep one decimal and drop the rest, which is how a piece's score is stored before it is summed.</summary>
    public static double TruncateTenth(double value)
    {
        if (value <= 0)
            return 0;

        return Math.Truncate(value * 10d + 1e-6) / 10d;
    }

    /// <summary>
    /// Whether a paper-doll slot is part of the unit gear score. Looks, hair and the body slots are not.
    /// </summary>
    public static bool CountsTowardGearScore(int slot)
    {
        if (slot is 31 or 33)
            return false;

        if (slot is >= 15 and <= 18)
            return true;

        if (slot == 26 || slot == 27)
            return true;

        if (slot is >= 0 and <= 14)
            return true;

        return slot is > 28 and <= 33;
    }

    /// <summary>
    /// Item level of each filled socket. The socket and gem formulas take that level, the stone's own,
    /// rather than the piece it sits in.
    /// </summary>
    public static List<int> GemItemLevels(uint[] gemIds, Func<uint, int?> levelOf)
    {
        var levels = new List<int>();
        if (gemIds == null || levelOf == null)
            return levels;

        foreach (var gemId in gemIds)
        {
            if (gemId == 0)
                continue;

            var gemLevel = levelOf(gemId);
            if (gemLevel == null)
                continue;

            levels.Add(gemLevel.Value);
        }

        return levels;
    }

    /// <summary>Socket formula plus gem formula, once per filled stone.</summary>
    public static double ScoreGems(IEnumerable<int> gemItemLevels, Func<double, double> socketAt, Func<double, double> gemAt)
    {
        if (gemItemLevels == null || socketAt == null || gemAt == null)
            return 0;

        var sum = 0d;
        foreach (var gemLevel in gemItemLevels)
            sum += socketAt(gemLevel) + gemAt(gemLevel);

        return sum;
    }

    private static int? GemLevel(uint gemId)
    {
        var gem = ItemManager.Instance.GetTemplate(gemId);
        if (gem != null)
            return gem.Level;

        Logger.Warn("Gear score: socketed item {0} has no template, so it adds no stone score", gemId);
        return null;
    }

    /// <summary>
    /// Total gear score across a character's equipped pieces.
    /// </summary>
    public static int Evaluate(Character character) => (int)Math.Truncate(EvaluateParts(character).Total);

    /// <summary>
    /// A character's equipped score split the same way the ranking window shows it.
    /// </summary>
    public static GearScoreParts EvaluateParts(Character character)
    {
        if (character?.Inventory?.Equipment == null)
            return default;

        double total = 0;
        double bare = 0;
        foreach (var item in character.Inventory.Equipment.Items)
        {
            if (item == null || !CountsTowardGearScore(item.Slot))
                continue;

            var gain = character.EquipSlotReinforces?.ItemLevelGain((byte)item.Slot) ?? 0;
            var parts = EvaluateItemParts(item, gain);
            total += parts.Total;
            bare += parts.Bare;
        }

        return new GearScoreParts(total, bare);
    }

    /// <summary>
    /// The gear-score multiplier a template column carries, in the units the shipped formulas use.
    /// </summary>
    /// <remarks>
    /// The columns store the multiplier <b>per hundred</b>: a weapon that weighs 2.2 in formula 30 is
    /// stored as 220, an armor slot that weighs 0.78 in formula 56 as 78, and the two cosmetic tiers
    /// formula 56 singles out — the ones it compares against 0.01 and 0.02 — as 1 and 2. Feeding the
    /// stored number straight into a formula that expects the fraction inflates every piece a hundredfold,
    /// which is a hundredfold on the character's total.
    /// </remarks>
    public static double FromStoredMultiplier(int stored) => stored / 100.0;
}
