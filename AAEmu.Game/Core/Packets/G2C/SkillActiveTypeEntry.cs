namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>One skill-active-type mapping owned by the character.</summary>
public readonly record struct SkillActiveTypeEntry(
    int HeirSkillType,
    int SkillType,
    byte ActiveType);
