namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Raw enum_skill_target_relation ids. The enum used to stop at <see cref="Others"/>, so the five v10
/// members below fell through SkillTargetingUtil's permissive default and were not filtered at all —
/// 15 plot events and 2 skills in the shipped data.
/// </summary>
public enum SkillTargetRelation : byte
{
    Any = 0,
    Friendly = 1,
    Party = 2,
    Raid = 3,
    Hostile = 4,
    Others = 5,
    FriendlyForDebuff = 6,
    SiegeOffenseHqUser = 7,
    Family = 8,
    IgnoreProtected = 9,
    ExpeditionMember = 10
}
