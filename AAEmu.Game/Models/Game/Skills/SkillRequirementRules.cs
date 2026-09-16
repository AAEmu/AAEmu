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
/// <c>default_result</c>, and the content settles it:
/// <list type="bullet">
/// <item><description><b>Forbid</b> (<c>'t'</c>, 236 rows) — 발묶임/기절/수면/공포/넘어짐 (root, stun, sleep,
/// fear, knockdown) tag rows on movement and combat skills, and the target-side ones ("cannot be used on
/// a refusing Biam", "cannot be used on an angry wild horse"). All 236 messages read "you cannot use
/// this while/on X".</description></item>
/// <item><description><b>Require</b> (<c>'f'</c>, 102 rows) — requirement 15 is tag 294 날틀 비행중
/// (gliding) and it gates the glider skills (13430 폭탄 투척, 17657 날틀 접기 "fold the glider", 21094 순간
/// 이동); requirement 58/59 are 구속 (restraint) and 공포 (fear) and they gate 자유 (Freedom, 20982) and
/// 강인한 의지 (11429). Those are unusable unless the state is present, in both directions.</description></item>
/// </list>
///
/// Some <c>'f'</c> rows still carry a "cannot use while X" message (requirement 37 for a downed summon,
/// 140 for fish), which is why the polarity is read from the flag and not from the string; the messages
/// are client strings and 20 of the 102 are inconsistent with their own row.
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
