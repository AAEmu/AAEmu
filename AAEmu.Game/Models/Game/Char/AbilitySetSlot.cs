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
}
