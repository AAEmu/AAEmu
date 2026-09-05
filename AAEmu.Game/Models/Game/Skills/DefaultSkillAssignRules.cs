using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Which <c>default_skills</c> a character may receive or cast.
/// Rows named by <c>character_default_skills</c> belong to one race/gender
/// template; every other row is shared (basic attack, labor, and the rest).
/// </summary>
public static class DefaultSkillAssignRules
{
    /// <summary>
    /// A skill listed on any character template is racial. An unlisted duplicate
    /// row of the same skill id does not make that skill universal.
    /// </summary>
    public static bool AppliesToCharacter(
        uint skillId,
        IReadOnlySet<uint> raceAssignedSkillIds,
        IReadOnlySet<uint> skillIdsForThisRaceGender)
    {
        if (raceAssignedSkillIds == null || !raceAssignedSkillIds.Contains(skillId))
            return true;
        return skillIdsForThisRaceGender != null && skillIdsForThisRaceGender.Contains(skillId);
    }
}
