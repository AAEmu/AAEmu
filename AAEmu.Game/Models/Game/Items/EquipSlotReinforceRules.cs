namespace AAEmu.Game.Models.Game.Items;

/// <summary>What one reinforcement request did, so callers can answer the client and log truthfully.</summary>
public enum EquipSlotReinforceChange
{
    /// <summary>The request was valid but would change nothing.</summary>
    Unchanged,

    /// <summary>Exp was banked and the slot still needs more before it can level.</summary>
    ExpGained,

    /// <summary>Exp was banked up to the step's requirement; the surplus was discarded.</summary>
    ExpCapped,

    /// <summary>The slot had a full bar and took the next level.</summary>
    LeveledUp,

    /// <summary>The ladder ends here, or the character does not hold what the step costs.</summary>
    Refused
}

/// <summary>
/// Pure decisions for equip slot reinforcement: what a feeding banks, when a level-up is available and
/// which bonuses a set of levels unlocks. The ladder itself (levels, requirements, costs, effect
/// thresholds) always comes from content data — nothing here invents a level cap or a price.
/// </summary>
public static class EquipSlotReinforceRules
{
    /// <summary>
    /// Exp a feeding banks: the room left before the current step's requirement, capped by what the
    /// material grants. A bar that is already full banks nothing.
    /// </summary>
    public static int ExpAccepted(int currentExp, int needExp, int gainExp)
    {
        if (gainExp <= 0)
            return 0;

        var room = needExp - currentExp;
        if (room <= 0)
            return 0;

        return Math.Min(room, gainExp);
    }

    /// <summary>The slot can level up when its bar is full for the level it is working towards.</summary>
    public static bool CanLevelUp(sbyte level, int exp, EquipSlotReinforceStep nextStep)
    {
        if (nextStep == null)
            return false;

        return level + 1 == nextStep.Level && exp >= nextStep.NeedExp;
    }

    /// <summary>The step that follows the given level, unchanged when the ladder ends there.</summary>
    public static EquipSlotReinforceStep NextStep(sbyte level, IEnumerable<EquipSlotReinforceStep> ladder)
    {
        if (ladder == null)
            return null;

        var wanted = (byte)(level + 1);
        foreach (var step in ladder)
        {
            if (step.Level == wanted)
                return step;
        }

        return null;
    }

    /// <summary>Total reinforcement level across every slot that belongs to one attribute.</summary>
    public static int AttributeTotal(EquipSlotReinforceAttribute attribute,
        IEnumerable<EquipSlotReinforceState> states,
        Func<byte, EquipSlotReinforceAttribute?> attributeOfSlot)
    {
        if (states == null || attributeOfSlot == null)
            return 0;

        var total = 0;
        foreach (var state in states)
        {
            if (state == null || attributeOfSlot(state.SlotTypeId) != attribute)
                continue;

            total += state.Level;
        }

        return total;
    }

    /// <summary>Highest set effect level whose threshold the attribute total has reached.</summary>
    public static byte SetEffectLevel(EquipSlotReinforceAttribute attribute, int attributeTotal,
        IEnumerable<EquipSlotReinforceSetEffect> effects)
    {
        byte best = 0;
        if (effects == null)
            return best;

        foreach (var effect in effects)
        {
            if (effect.Attribute != attribute || attributeTotal < effect.RequireReinforceLevel)
                continue;

            if (effect.Level > best)
                best = effect.Level;
        }

        return best;
    }

    /// <summary>Highest bundle effect level whose three thresholds are all reached.</summary>
    public static byte BundleEffectLevel(int offenceTotal, int defenceTotal, int supportTotal,
        IEnumerable<EquipSlotReinforceBundleEffect> effects)
    {
        byte best = 0;
        if (effects == null)
            return best;

        foreach (var effect in effects)
        {
            if (offenceTotal < effect.RequireOffenseLevel ||
                defenceTotal < effect.RequireDefenseLevel ||
                supportTotal < effect.RequireSupportLevel)
                continue;

            if (effect.BundleEffectLevel > best)
                best = effect.BundleEffectLevel;
        }

        return best;
    }

    /// <summary>
    /// Level effects a slot is entitled to at its level, in content order. The player picks one of
    /// these as the slot's active effect, which is what the index in the per-slot state refers to.
    /// </summary>
    public static List<EquipSlotReinforceLevelEffect> EligibleLevelEffects(byte slotTypeId, sbyte level,
        IEnumerable<EquipSlotReinforceLevelEffect> effects)
    {
        var eligible = new List<EquipSlotReinforceLevelEffect>();
        if (effects == null)
            return eligible;

        foreach (var effect in effects)
        {
            if (effect.SlotTypeId == slotTypeId && level >= effect.TriggerLevel)
                eligible.Add(effect);
        }

        return eligible;
    }

    /// <summary>
    /// The chosen effect index after a level change: kept when it is still eligible, otherwise the
    /// last eligible one (so a slot never points at an effect it has outgrown or lost).
    /// </summary>
    public static int NormalizeLevelEffectIndex(int currentIndex, int eligibleCount)
    {
        if (eligibleCount <= 0)
            return -1;

        if (currentIndex < 0 || currentIndex >= eligibleCount)
            return eligibleCount - 1;

        return currentIndex;
    }
}
