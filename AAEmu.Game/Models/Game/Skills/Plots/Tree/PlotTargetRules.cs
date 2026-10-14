using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units.Static;

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

    /// <summary>
    /// <see cref="AllowsNeutral(SkillTargetRelation)"/>, widened for a search whose
    /// <c>plot_aoe_conditions</c> require the candidate to carry a named buff tag.
    /// </summary>
    /// <remarks>
    /// Such a search names its target by a marker the data put on it, and the marker NPCs are usually
    /// neutral: the Hereafter soul gathering point (tag 4181), the souls themselves, cannon and spawn
    /// reference points. Dropping Neutrals there emptied the search, so a soul rescue found no gathering
    /// point and credited nothing. A tag requirement is already the narrowest filter the event can state,
    /// so the blanket Neutral drop adds nothing but the miss.
    /// </remarks>
    public static bool AllowsNeutral(SkillTargetRelation relation, IEnumerable<PlotCondition> aoeConditions) =>
        AllowsNeutral(relation) || RequiresTaggedTarget(aoeConditions);

    /// <summary>
    /// <see cref="AllowsNeutral(SkillTargetRelation, IEnumerable{PlotCondition})"/>, also widened for a
    /// unit_reqs condition whose rows name a marker on the candidate.
    /// </summary>
    /// <remarks>
    /// A unit_reqs condition (kind 20) marks its target through rows of its own: the Hereafter hellhound's
    /// "find the defenders hunting the hellhound" search requires buff 25741 of that exact name, and the
    /// hellhound is itself Neutral, so dropping Neutrals emptied every one of its searches.
    /// </remarks>
    /// <param name="requirementKinds">The unit_reqs kinds a plot condition owns, by condition id.</param>
    public static bool AllowsNeutral(SkillTargetRelation relation, IEnumerable<PlotCondition> aoeConditions,
        Func<uint, IEnumerable<UnitReqsKindType>> requirementKinds) =>
        AllowsNeutral(relation, aoeConditions) || RequiresMarkedTarget(aoeConditions, requirementKinds);

    /// <summary>
    /// Whether a search names its victims rather than looks for enemies: it accepts any relation and its
    /// conditions demand a marker the data put on the candidate.
    /// </summary>
    /// <remarks>
    /// The Hereafter hellhound leaps on the defenders "hunting the hellhound" (buff 25741) and kills them
    /// with a fixed 99,999 hit. They share its faction, so the relation test refused every hit. A search
    /// that picks units by name has already decided they are the victims.
    /// </remarks>
    public static bool SelectsMarkedVictims(SkillTargetRelation relation, IEnumerable<PlotCondition> aoeConditions,
        Func<uint, IEnumerable<UnitReqsKindType>> requirementKinds) =>
        relation == SkillTargetRelation.Any &&
        (RequiresTaggedTarget(aoeConditions) || RequiresMarkedTarget(aoeConditions, requirementKinds));

    /// <summary>True when one of <paramref name="aoeConditions"/> demands a buff tag on the candidate.</summary>
    public static bool RequiresTaggedTarget(IEnumerable<PlotCondition> aoeConditions) =>
        aoeConditions?.Any(c => c is { Kind: PlotConditionType.BuffTag, NotCondition: false }) ?? false;

    /// <summary>
    /// True when a unit_reqs condition among <paramref name="aoeConditions"/> demands a buff, buff tag or
    /// template on the candidate.
    /// </summary>
    public static bool RequiresMarkedTarget(IEnumerable<PlotCondition> aoeConditions,
        Func<uint, IEnumerable<UnitReqsKindType>> requirementKinds) =>
        requirementKinds != null &&
        (aoeConditions?.Any(c => c is { Kind: PlotConditionType.UnitReqs, NotCondition: false } &&
                                 (requirementKinds(c.Id)?.Any(IsMarkerRequirement) ?? false)) ?? false);

    private static bool IsMarkerRequirement(UnitReqsKindType kind) => kind is
        UnitReqsKindType.Buff or UnitReqsKindType.TargetBuff or UnitReqsKindType.BuffTag or
        UnitReqsKindType.TargetBuffTag or UnitReqsKindType.TargetNpc;

    /// <summary>
    /// The unit a plot_aoe_conditions row is judged as the owner of: the candidate for a unit_reqs
    /// condition, the caster for every other kind.
    /// </summary>
    /// <remarks>
    /// An area condition filters candidates, and its unit_reqs rows describe the candidate whether the
    /// row is spelled as a target kind or not: "find the defenders hunting the hellhound" is buff 25741,
    /// "PC and summons within 40m" is a mother-faction row, "not Michaela's autocannon" is no-buff 24926.
    /// Judged on the caster, those asked whether the caster carried the marker and passed or failed every
    /// candidate at once. The other condition kinds already take the candidate as their target.
    /// </remarks>
    public static T AoeConditionOwner<T>(PlotConditionType kind, T caster, T candidate) =>
        kind == PlotConditionType.UnitReqs ? candidate : caster;
}
