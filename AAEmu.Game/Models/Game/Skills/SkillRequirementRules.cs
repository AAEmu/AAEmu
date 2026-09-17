namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// One <c>skill_reqs</c> row: a buff or buff tag on the caster or on the target that either forbids or
/// requires the cast.
/// </summary>
/// <param name="OnTarget">True for <c>target='t'</c> (check the skill's target), false for the caster.</param>
/// <param name="BuffId">The buff id the row names, or 0.</param>
/// <param name="BuffTagId">The buff tag the row names, or 0.</param>
/// <param name="Require">
/// <c>default_result='f'</c>: the unit must carry the buff or tag. <c>'t'</c>: it must not.
/// </param>
/// <param name="Message">The client string shown when the row fails.</param>
public readonly record struct SkillRequirement(bool OnTarget, uint BuffId, uint BuffTagId, bool Require, string Message);

/// <summary>
/// <c>skill_reqs</c> / <c>skill_req_skills</c> / <c>skill_req_skill_tags</c>: the buff and buff-tag gates a
/// skill carries, none of which were loaded.
/// </summary>
/// <remarks>
/// 338 requirement rows, 1,813 links to individual skills and 339 links to skill tags. The polarity is
/// <c>default_result</c>, and two pieces of authored content settle it:
/// <list type="bullet">
/// <item><description>Requirement 6 is a <c>'t'</c> row whose message is
/// <c>%s 상태가 아니어야 합니다</c> — "must <i>not</i> be in %s state". It is the only row in the table
/// that states a polarity in words rather than implying one, and it states this one.</description></item>
/// <item><description>Requirement 58 is <c>'f'</c> on tag 4981 구속 (기술 사용X), "restraint (skill use
/// disabled)". A skill-use-disabled tag is a state to refuse a cast in, so a row on it is a <i>forbid</i>
/// row, which is what <c>'f'</c> means under this reading. What it gates agrees: 자유 (Freedom, 20982),
/// 강인한 의지 (11429) and 생명력 발산 (10645) are break-free skills, and those have to be castable
/// precisely while restrained. Requirement 15 is the same shape from the other side — it gates the glider
/// skills (13430 폭탄 투척, 17657 날틀 접기, 21094 순간 이동) on tag 294 날틀 비행중, and I confirmed
/// all three really are glider-granted, so requiring the tag is what makes them work.</description></item>
/// </list>
///
/// The message text is a client string and does <i>not</i> discriminate: 87 of the 236 <c>'t'</c> rows
/// (37 %) and 46 of the 102 <c>'f'</c> rows (45 %) carry "cannot use while/on X" phrasing, and the
/// <c>'f'</c> group also carries peace-zone, prisoner and judge strings that read as restrictions. The
/// polarity is therefore read from the flag, never from the string.
///
/// Forbid rows combine with AND — any one of them present blocks the cast. Require rows combine with OR:
/// 강인한 의지 carries both 58 and 59, and reading those as AND would demand restraint <i>and</i> fear at
/// once, so the cast could never happen at all.
/// </remarks>
public static class SkillRequirementRules
{
    /// <summary>Whether one row fails for a unit in the given state.</summary>
    public static bool Fails(in SkillRequirement requirement, bool hasBuff, bool hasBuffTag)
    {
        // A row names either a buff id or a buff tag; one of them matching is enough.
        var carries = (requirement.BuffId > 0 && hasBuff) ||
                       (requirement.BuffTagId > 0 && hasBuffTag);

        return requirement.Require ? !carries : carries;
    }

    /// <summary>
    /// Whether the cast may proceed. <paramref name="message"/> receives the first failing row's client
    /// string, so the caller can show why.
    /// </summary>
    public static bool AllowsCast(
        IReadOnlyList<SkillRequirement> requirements,
        Func<SkillRequirement, bool> carriesBuff,
        Func<SkillRequirement, bool> carriesBuffTag,
        out string message)
    {
        message = null;
        if (requirements == null || requirements.Count == 0)
            return true;

        var sawRequireRow = false;
        var anyRequireRowPassed = false;

        foreach (var requirement in requirements)
        {
            var hasBuff = carriesBuff?.Invoke(requirement) ?? false;
            var hasTag = carriesBuffTag?.Invoke(requirement) ?? false;

            if (requirement.Require)
            {
                sawRequireRow = true;
                if (!Fails(requirement, hasBuff, hasTag))
                    anyRequireRowPassed = true;
                continue;
            }

            if (Fails(requirement, hasBuff, hasTag))
            {
                message = requirement.Message;
                return false;
            }
        }

        if (sawRequireRow && !anyRequireRowPassed)
        {
            // Report the first require row's own string: every row of one requirement group shares it.
            foreach (var requirement in requirements)
            {
                if (!requirement.Require)
                    continue;
                message = requirement.Message;
                break;
            }

            return false;
        }

        return true;
    }
}
