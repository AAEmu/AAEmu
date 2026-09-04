using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>One skillsaver slot: active triad + learned combat skills/passives at save time.</summary>
public sealed class AbilitySetSlot
{
    public byte SlotIndex { get; init; }
    public AbilityType Ability1 { get; set; } = AbilityType.None;
    public AbilityType Ability2 { get; set; } = AbilityType.None;
    public AbilityType Ability3 { get; set; } = AbilityType.None;
    public List<uint> SkillIds { get; } = [];
    public List<uint> PassiveBuffIds { get; } = [];

    public bool IsOccupied =>
        Ability1 is not AbilityType.None and not AbilityType.General ||
        Ability2 is not AbilityType.None and not AbilityType.General ||
        Ability3 is not AbilityType.None and not AbilityType.General;

    /// <summary>True when the equipped three trees match this snapshot's triad order.</summary>
    public bool MatchesTriad(AbilityType ability1, AbilityType ability2, AbilityType ability3) =>
        Ability1 == ability1 && Ability2 == ability2 && Ability3 == ability3;

    /// <summary>
    /// True when the equipped combat skills/passives for this triad match the snapshot lists
    /// (order-independent). Used so same-triad activates are not treated as no-ops when the
    /// player reallocated points or is switching between two saved builds of the same trees.
    /// </summary>
    public bool MatchesSkillLoadout(
        IReadOnlyCollection<uint> equippedSkillIds,
        IReadOnlyCollection<uint> equippedPassiveBuffIds)
    {
        if (SkillIds.Count != equippedSkillIds.Count ||
            PassiveBuffIds.Count != equippedPassiveBuffIds.Count)
            return false;

        return SkillIds.ToHashSet().SetEquals(equippedSkillIds) &&
               PassiveBuffIds.ToHashSet().SetEquals(equippedPassiveBuffIds);
    }
}
