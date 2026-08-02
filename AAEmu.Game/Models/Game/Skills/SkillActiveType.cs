namespace AAEmu.Game.Models.Game.Skills;

/// <summary>Client-defined visibility and activation state for a skill entry.</summary>
public enum SkillActiveType : byte
{
    None = 0,
    Active = 1,
    Nonactive = 2,
    Hide = 3,
    Unlock = 4
}
