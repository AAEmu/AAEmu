using AAEmu.Game.Models.Game.Skills.Utils;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// What the plot area search may keep, from the event's own flags and its relation.
/// </summary>
/// <remarks>
/// The four booleans are <c>plot_events.only_die_unit</c> (12 events), <c>only_my_pet</c> (14),
/// <c>only_pet_owner</c> (55) and <c>only_my_slave</c> (55), none of which the loader read. Their meanings
/// are named by the skills that use them: plot 3005 is 사람이 자신의 펫한테 기술 사용 ("a player uses a
/// skill on their own pet") and carries only_my_pet; plot 3004 is 펫이 자기 주인에게 기술 사용 ("a pet uses
/// a skill on its own owner") and carries only_pet_owner; plot 3290 splits 발동 into "Dispel effect_Pet"
/// (only_my_pet) and "Dispel effect_PC" (only_pet_owner); plot 2706's 대포 타겟 ("cannon target") searches
/// for the ship's own cannons and carries only_my_slave.
/// </remarks>
public static class PlotTargetRules
{
    /// <summary>What the search knows about one candidate unit.</summary>
    public readonly record struct UnitFacts(
        bool IsDead,
        bool IsCasterPet,
        bool IsCasterPetOwner,
        bool IsCasterSlave,
        bool OwnsAPet);

    /// <summary>
    /// Whether <paramref name="facts"/> survives the event's four flags. An event with none of them set —
    /// 51,178 plot_events rows, of which 33,578 spell all four 'f' and the rest leave them NULL — keeps
    /// everything, exactly as before.
    /// </summary>
    public static bool PassesEventFilters(bool onlyDieUnit, bool onlyMyPet, bool onlyPetOwner,
        bool onlyMySlave, UnitFacts facts)
    {
        if (onlyDieUnit && !facts.IsDead)
            return false;
        if (onlyMyPet && !facts.IsCasterPet)
            return false;
        if (onlyPetOwner && !(facts.IsCasterPetOwner || facts.OwnsAPet))
            return false;
        if (onlyMySlave && !facts.IsCasterSlave)
            return false;
        return true;
    }

    /// <summary>
    /// Whether a unit whose relation to the caster is Neutral may stay in the candidate list for this
    /// relation id.
    /// </summary>
    /// <remarks>
    /// Every plot area dropped Neutrals before the relation filter ran. For the 4,715 areas that name no
    /// relation (id 0 "any") that is the historical answer and stays. For the 64 Areas and 16 RandomUnit
    /// searches that name relation 5 "others" it was fatal: <c>SkillTargetingUtil</c> defines "others" as
    /// exactly the Neutral units, so the two filters together emptied the search — those plots applied
    /// their effects to nobody. A row that names a relation gets that relation's answer.
    /// </remarks>
    public static bool AllowsNeutral(SkillTargetRelation relation) => relation != SkillTargetRelation.Any;
}
