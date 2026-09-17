namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Which buffs a BuffSteal (special type 16) row takes off the target and puts on the caster.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 14 rows, 12 of them reachable through <c>effects</c> and all 12
/// through <c>skill_effects</c> (no buff carries the type). <c>value3</c> is how many buffs are taken (1 on
/// eight rows, 2 on three, 3 on three) and matches the skills' own text: 소드락질 10104 says "적대상의
/// 이로운 효과 2개 탈취" (steal two beneficial effects) and 23707 / 39096 say "적대상의 이로운 효과 1개를
/// 빼앗아옵니다" (steal one). <c>value4</c> restricts the take to one buff tag — 229 on effect 34212 and 1229
/// on effect 44232, both real <c>tagged_buffs.tag_id</c> families with 10 and 1 members — and is 0 on the
/// other ten rows. <c>value1</c> and <c>value2</c> are 1 on the two 돌려주기 44205 rows (51347/51348, which
/// take 2 and 3) and 0 elsewhere, so neither slot tracks the count and neither is read.
/// </remarks>
public static class BuffStealRules
{
    /// <summary>One buff the target is holding, with the tags its template belongs to.</summary>
    public readonly record struct StealCandidate(
        int Index,
        uint BuffId,
        BuffKind Kind,
        bool Passive,
        bool System,
        IReadOnlyCollection<uint> Tags);

    /// <summary>
    /// The buffs to take, lowest slot first so a repeated cast always steals the same ones. Only 이로운 효과
    /// (beneficial effects) are taken — the three leech skills say so in their own text — and passives and
    /// system buffs are part of what the unit is rather than something a leech can take. A
    /// <paramref name="requiredTagId"/> of 0 takes anything; <paramref name="maxCount"/> below 1 takes
    /// nothing.
    /// </summary>
    public static List<StealCandidate> Select(IEnumerable<StealCandidate> candidates, int maxCount, uint requiredTagId)
    {
        if (candidates == null || maxCount < 1)
            return [];

        return candidates
            .Where(candidate => candidate.Kind == BuffKind.Good && !candidate.Passive && !candidate.System)
            .Where(candidate => requiredTagId == 0 || candidate.Tags?.Contains(requiredTagId) == true)
            .OrderBy(candidate => candidate.Index)
            .Take(maxCount)
            .ToList();
    }
}
