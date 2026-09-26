using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// The list-evaluator policy for a quest component's <c>unit_reqs</c> rows, and the wire form of the
/// row that refused an accept. Kept free of unit state so the decision can be tested on plain results.
/// </summary>
public static class QuestStartRequirementRules
{
    /// <summary>
    /// Folds per-row results the way an OR group does. An empty list passes. With
    /// <c>or_unit_reqs</c> false the first failing row is returned as it is, display gate included, and
    /// the rows after it are never evaluated. With it true the first passing row ends the walk with a
    /// fresh success, and a list that exhausts its rows answers UNIT_REQS_OR_FAIL (0x31) with zero
    /// details and the gate left on.
    /// </summary>
    public static UnitReqsValidationResult Evaluate(bool orUnitReqs, IEnumerable<UnitReqsValidationResult> results)
    {
        var anyRow = false;
        foreach (var result in results)
        {
            anyRow = true;
            var passed = Passes(result);
            if (orUnitReqs && passed)
                return Success();
            if (!orUnitReqs && !passed)
                return result;
        }

        return orUnitReqs && anyRow ? OrGroupExhausted() : Success();
    }

    public static bool Passes(UnitReqsValidationResult result) =>
        result != null && result.ResultKey == SkillResultKeys.ok;

    /// <summary>
    /// The result byte SCQuestUnitReqFailed carries: the byte the client's own evaluator writes when the
    /// key table has no member for it (<see cref="UnitReqsValidationResult.NativeResult"/>), else the
    /// key's mapping.
    /// </summary>
    public static SkillResult WireResult(UnitReqsValidationResult result) =>
        result.NativeResult ?? SkillResultHelper.SkillResultErrorKeyToId(result.ResultKey);

    private static UnitReqsValidationResult Success() => new(SkillResultKeys.ok, 0, 0);

    private static UnitReqsValidationResult OrGroupExhausted() => new(SkillResultKeys.skill_unit_reqs_or_fail, 0, 0);
}
