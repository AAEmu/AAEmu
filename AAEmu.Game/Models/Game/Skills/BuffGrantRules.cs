namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// How several simultaneous buff grants combine. Pure decision logic: no content lookup and no state,
/// so the ledger in <c>CharacterSkills</c> only has to store each active buff's <see cref="BuffGrantSet"/>
/// and re-run these rules.
/// </summary>
/// <remarks>
/// The tables behind this (10.0.2.13, 2,316 + 849 + 71 + 35 rows) overlap in both directions, so the
/// combinations are real and not hypothetical:
/// <list type="bullet">
/// <item>813 buffs in <c>buff_skills</c> and 302 in <c>buff_mount_skills</c> (1,105 in total) may be up
/// together, and a skill granted by two of them must survive the first one ending - the nine glider buffs
/// (실험형 날틀 1029, 개량형 3528, 강화형 3529, 완성형 3530 …) all grant 날틀 접기 17657.</item>
/// <item>Several buffs swap the same action-bar entry and only one can own it: 21 rows swap 그림자 거울
/// (40335), 9 swap 발묶음 (12133), 4 swap 폭탄 발사 준비 (35351). <c>priority</c> is the tie-break the
/// data provides - two of the 64 loaded rows carry 1, 망치 태세 18383 among them.</item>
/// </list>
/// </remarks>
public static class BuffGrantRules
{
    /// <summary>
    /// Whether <paramref name="candidate"/> takes over an origin already claimed by
    /// <paramref name="current"/>. Higher <c>priority</c> wins; equal priorities fall back to the lower
    /// <c>buff_swap_skills.id</c>, so the outcome does not depend on which buff happened to be applied
    /// first.
    /// </summary>
    public static bool SwapWins(BuffSkillSwap candidate, BuffSkillSwap current)
    {
        if (candidate.Priority != current.Priority)
            return candidate.Priority > current.Priority;
        return candidate.Id < current.Id;
    }

    /// <summary>
    /// The swap that owns each replaced origin while <paramref name="holders"/> are active - exactly one
    /// per distinct <see cref="BuffSkillSwap.OriginSkillId"/>.
    /// </summary>
    public static IReadOnlyList<BuffSkillSwap> WinningSwaps(IEnumerable<BuffGrantSet> holders)
    {
        var winners = new List<BuffSkillSwap>();
        foreach (var set in holders ?? [])
        {
            if (set == null)
                continue;

            foreach (var swap in set.Swaps)
            {
                var index = winners.FindIndex(w => w.OriginSkillId == swap.OriginSkillId);
                if (index < 0)
                    winners.Add(swap);
                else if (SwapWins(swap, winners[index]))
                    winners[index] = swap;
            }
        }

        return winners;
    }

    /// <summary>
    /// Every skill the owner holds temporarily while <paramref name="holders"/> are active: the union of
    /// their grants plus each winning swap's replacement skill. A skill granted by several holders
    /// appears once.
    /// </summary>
    public static IReadOnlyList<uint> HeldSkills(IEnumerable<BuffGrantSet> holders)
    {
        var sets = (holders ?? []).Where(set => set != null).ToList();
        var held = new List<uint>();
        var seen = new HashSet<uint>();

        foreach (var set in sets)
            foreach (var skillId in set.GrantedSkills)
                if (skillId != 0 && seen.Add(skillId))
                    held.Add(skillId);

        foreach (var swap in WinningSwaps(sets))
            if (swap.NewSkillId != 0 && seen.Add(swap.NewSkillId))
                held.Add(swap.NewSkillId);

        // A replaced origin is off the bar even when another holder names it directly: the swap is what
        // the player is meant to be holding, and both cannot occupy the same entry.
        foreach (var replaced in ReplacedOrigins(sets))
            if (seen.Remove(replaced))
                held.Remove(replaced);

        return held;
    }

    /// <summary>Skill ids a winning swap takes off the bar while it is up.</summary>
    public static IReadOnlyList<uint> ReplacedOrigins(IEnumerable<BuffGrantSet> holders) =>
        WinningSwaps(holders).Select(swap => swap.OriginSkillId).Where(id => id != 0).Distinct().ToList();

    /// <summary>Union of the passives the active holders grant.</summary>
    public static IReadOnlyList<uint> HeldPassives(IEnumerable<BuffGrantSet> holders)
    {
        var passives = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var set in holders ?? [])
        {
            if (set == null)
                continue;

            foreach (var passiveId in set.PassiveBuffIds)
                if (passiveId != 0 && seen.Add(passiveId))
                    passives.Add(passiveId);
        }

        return passives;
    }

    /// <summary>
    /// Skills to hand over when the holders change: what the new holder set holds that
    /// <paramref name="heldNow"/> does not.
    /// </summary>
    public static IReadOnlyList<uint> AddedSkills(IReadOnlyList<uint> heldNow, IEnumerable<BuffGrantSet> holders)
    {
        var held = new HashSet<uint>(heldNow ?? []);
        return HeldSkills(holders).Where(skillId => !held.Contains(skillId)).ToList();
    }

    /// <summary>
    /// Skills to take away when the holders change: what <paramref name="heldNow"/> holds that no
    /// remaining holder grants. A skill two buffs grant is released only by the second one, which is why
    /// this is a set difference over the remaining holders rather than "everything the ending buff
    /// named".
    /// </summary>
    public static IReadOnlyList<uint> ReleasedSkills(IReadOnlyList<uint> heldNow, IEnumerable<BuffGrantSet> holders)
    {
        var wanted = new HashSet<uint>(HeldSkills(holders));
        return (heldNow ?? []).Where(skillId => !wanted.Contains(skillId)).Distinct().ToList();
    }
}
