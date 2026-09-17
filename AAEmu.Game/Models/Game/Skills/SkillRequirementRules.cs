namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// One <c>skill_reqs</c> row: a buff or buff tag on the caster or on the target that either forbids or
/// requires the cast.
/// </summary>
/// <param name="OnTarget">True for <c>target='t'</c> (check the skill's target), false for the caster.</param>
/// <param name="BuffId">The buff id the row names, or 0.</param>
/// <param name="BuffTagId">The buff tag the row names, or 0.</param>
/// <param name="Require">
/// The skill may only be cast <i>while</i> the unit carries the buff or tag, instead of being refused
/// while it does. See <see cref="SkillRequirementRules.IsRowForbidding"/> for when a row means this.
/// </param>
/// <param name="Message">The client string shown when the row fails.</param>
public readonly record struct SkillRequirement(bool OnTarget, uint BuffId, uint BuffTagId, bool Require, string Message);

/// <summary>
/// <c>skill_reqs</c> / <c>skill_req_skills</c> / <c>skill_req_skill_tags</c>: the buff and buff-tag gates a
/// skill carries, none of which were loaded.
/// </summary>
/// <remarks>
/// 338 requirement rows, 1,813 links to individual skills and 339 links to skill tags, all of which went
/// unenforced before this was loaded.
///
/// <para>Almost every row is a <i>forbid</i>: a state the player is told the cast is refused in, such as
/// 발묶임 (rooted) for requirement 1 or 결혼식 (wedding) for 42. Requirements combine with AND — any one
/// present blocks the cast.</para>
///
/// <para>The exception is four rows, <see cref="RequireRowsByDesign"/>, whose skill exists only <i>in</i>
/// the named state and which therefore mean the opposite: 날틀 비행중 (gliding, 15), 말 부상 넘어짐
/// (a downed mount, 37), 구속 (restraint, 58) and 공포 (fear, 59). Those have to be
/// castable exactly while the state holds — 자유 (Freedom, 20982) and 강인한 의지 (11429) are break-free
/// skills — so they combine with OR: the cast needs one of them to hold.</para>
///
/// <para><c>default_result</c> is not the polarity. Reading it as "must carry" inverts 97 of the 102
/// <c>'f'</c> rows into demands for a state that is usually absent, which blocks them nearly everywhere:
/// the mount-summon tag 358 (탈것 소환, 95 skills) demands a peace-zone buff (25, 115, 232, 286, 371),
/// a wedding (42), a courtroom (192, 193), intruder status (50), silence (74), purification akium (314),
/// an unknown force (318), prisoner status (359), a maze event (369) and a banquet (385) all at once;
/// requirement 58 is what that reading was built on, and it is one of the four exceptions rather than
/// the rule. Tag 402 평화 지역 가능, "skills usable at a resurrection point" (6,378 skills: 치유 물약
/// and 대 명상 물약 among them), carries the same twelve rows, so that reading also demands a peace
/// buff to drink a healing potion anywhere else.</para>
///
/// <para>The message text is a client string and carries no polarity of its own: "구속 상태에서는 기술을
/// 사용할 수 없습니다" (58, a require row) and "누이 여신 주변은 평화 지역 입니다" (25, a forbid row)
/// are both prohibitions, and only the first names a state the player cannot act in at all. The four
/// exceptions above are therefore recorded by id, next to the skills that justify them, rather than
/// inferred from wording.</para>
/// </remarks>
public static class SkillRequirementRules
{
    /// <summary>
    /// The rows whose skill is usable only <i>while</i> the row's buff or tag is present. Each is a state
    /// the player is otherwise barred from acting in, and the skills it gates exist to be used in it:
    /// <list type="bullet">
    /// <item><description>15 — tag 294 날틀 비행중 (gliding): the glider skills, e.g. 17657 날틀 접기,
    /// 13440 날틀 난사. Verified: tag 294 carries no skills of its own, so these 301 skills reach the row
    /// only through <c>skill_req_skills</c>.</description></item>
    /// <item><description>37 — tag 371 말 부상 넘어짐 (a downed mount): the skills that revive it.</description></item>
    /// <item><description>58 — tag 4981 구속 (기술 사용X): 자유 (Freedom, 20982), 강인한 의지 (11429),
    /// 생명력 발산 (10645) — break-free skills, castable only while restrained.</description></item>
    /// <item><description>59 — tag 12 공포 (fear): 강인한 의지 (11429) again, which carries 58 and 59
    /// together. Reading those as AND would demand restraint and fear at once.</description></item>
    /// </list>
    /// <para>Requirement 105 (tag 911 날틀 착지, landing) is deliberately <i>not</i> here. It rides on the
    /// mount-summon skill 32211 as well, and reading it as a require row allows summoning only while the
    /// player is landing — verified in game, that is the row the summon fails on once 25 is read
    /// correctly.</para>
    /// </summary>
    public static readonly IReadOnlySet<uint> RequireRowsByDesign = new HashSet<uint> { 15, 37, 58, 59 };

    /// <summary>
    /// Whether one <c>skill_reqs</c> row refuses the cast while its buff or tag is present, as opposed to
    /// requiring it. Rows that are not forbids are the handful in <see cref="RequireRowsByDesign"/>.
    /// </summary>
    /// <param name="rowId">The <c>skill_reqs.id</c>.</param>
    /// <param name="defaultResult">The row's <c>default_result</c> column.</param>
    public static bool IsRowForbidding(uint rowId, bool defaultResult)
    {
        return defaultResult || !RequireRowsByDesign.Contains(rowId);
    }

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
