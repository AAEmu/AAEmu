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

    /// <summary>The slot re-rolled an effect it already held, which is what the Replace window does.</summary>
    EffectReplaced,

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

    /// <summary>
    /// Banks a feed into a slot's bar and reports what happened. The surplus is discarded rather than
    /// carried, which is what the client's "experience would overflow" step is about.
    /// </summary>
    public static EquipSlotReinforceChange ApplyExp(EquipSlotReinforceState state, IEnumerable<EquipSlotReinforceStep> ladder,
        int gainExp)
    {
        if (state == null)
            return EquipSlotReinforceChange.Refused;

        var next = NextStep(state.Level, ladder);
        if (next == null)
            return EquipSlotReinforceChange.Refused;

        var accepted = ExpAccepted(state.Exp, next.NeedExp, gainExp);
        if (accepted <= 0)
            return EquipSlotReinforceChange.Unchanged;

        state.Exp += accepted;
        return state.Exp >= next.NeedExp ? EquipSlotReinforceChange.ExpCapped : EquipSlotReinforceChange.ExpGained;
    }

    /// <summary>
    /// Takes the next level when the bar is full, and reports whether there was one to take. The bar
    /// resets: its surplus was already discarded when it was fed, and the new level starts its own.
    /// </summary>
    public static EquipSlotReinforceChange TryLevelUp(EquipSlotReinforceState state, IEnumerable<EquipSlotReinforceStep> ladder,
        out EquipSlotReinforceStep reached)
    {
        reached = null;
        if (state == null)
            return EquipSlotReinforceChange.Refused;

        var next = NextStep(state.Level, ladder);
        if (!CanLevelUp(state.Level, state.Exp, next))
            return EquipSlotReinforceChange.Refused;

        state.Level = (sbyte)next.Level;
        state.Exp = 0;
        reached = next;
        return EquipSlotReinforceChange.LeveledUp;
    }

    /// <summary>
    /// The item a feeding spends: the first member of the material's item set that the character holds
    /// in full. The members are alternatives — one of them pays for the feed, not all of them — which is
    /// why a set may list both a single item and a bulk one.
    /// </summary>
    public static (uint ItemId, int Count)? PickConsumable(IEnumerable<(uint ItemId, int Count)> members,
        Func<uint, int> heldCount)
    {
        if (members == null || heldCount == null)
            return null;

        foreach (var (itemId, count) in members)
        {
            if (itemId == 0 || count <= 0)
                continue;

            if (heldCount(itemId) >= count)
                return (itemId, count);
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
    /// Level effects a slot is entitled to at its level, in content order — every tier whose trigger level the
    /// slot has reached. Each of them is meant to have handed the slot one effect by the time it is listed.
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
    /// The tier a slot reaches at exactly this level, or null when that level unlocks no tier. A tier is a
    /// <c>equip_slot_reinforce_level_effects</c> row, and the levels it is reached at are its trigger level.
    /// </summary>
    public static EquipSlotReinforceLevelEffect TierAtLevel(byte slotTypeId, sbyte level,
        IEnumerable<EquipSlotReinforceLevelEffect> effects)
    {
        if (effects == null)
            return null;

        foreach (var effect in effects)
        {
            if (effect.SlotTypeId == slotTypeId && effect.TriggerLevel == level)
                return effect;
        }

        return null;
    }

    /// <summary>
    /// Rolls one modifier out of a tier. A row's <c>weight</c> is its share of the pool, and a row the
    /// character already holds is skipped — "each artifact effect can only be obtained once" — which is also
    /// what makes a re-roll come back with a different row. Returns null when the tier is empty or has
    /// nothing left to hand out.
    /// </summary>
    /// <param name="roll">
    /// Any integer. It is taken modulo the weight still in the pool, so callers own the randomness and a test
    /// can pin an outcome by passing a value.
    /// </param>
    public static EquipSlotReinforceUnitModifier RollModifier(IEnumerable<EquipSlotReinforceUnitModifier> pool,
        Func<uint, bool> alreadyObtained, int roll)
    {
        if (pool == null)
            return null;

        List<EquipSlotReinforceUnitModifier> eligible = [];
        var total = 0;
        foreach (var modifier in pool)
        {
            if (modifier == null || modifier.Weight <= 0)
                continue;

            if (alreadyObtained != null && alreadyObtained(modifier.Id))
                continue;

            eligible.Add(modifier);
            total += modifier.Weight;
        }

        if (total <= 0)
            return null;

        var band = roll % total;
        if (band < 0)
            band += total;

        foreach (var modifier in eligible)
        {
            if (band < modifier.Weight)
                return modifier;

            band -= modifier.Weight;
        }

        return eligible[^1];
    }

    /// <summary>
    /// Whether a replace can roll a different row: the previous one is excluded, and so is every row the
    /// character already holds. Spending the stone before this check would pay for a no-op.
    /// </summary>
    public static bool HasRerollCandidate(IEnumerable<EquipSlotReinforceUnitModifier> pool, uint currentId,
        Func<uint, bool> alreadyObtained)
    {
        if (pool == null)
            return false;

        foreach (var modifier in pool)
        {
            if (modifier == null || modifier.Weight <= 0 || modifier.Id == currentId)
                continue;
            if (alreadyObtained != null && alreadyObtained(modifier.Id))
                continue;
            return true;
        }

        return false;
    }
}

