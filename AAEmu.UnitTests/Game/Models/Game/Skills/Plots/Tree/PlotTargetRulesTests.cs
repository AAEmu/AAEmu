using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

public class PlotTargetRulesTests
{
    private static PlotTargetRules.UnitFacts Facts(
        bool dead = false, bool pet = false, bool petOwner = false, bool slave = false, bool ownsPet = false) =>
        new(dead, pet, petOwner, slave, ownsPet);

    [Test]
    public async Task NoFlagsSet_KeepsEveryUnit()
    {
        // Only 136 of the 51,178 plot_events rows set any of the four flags; the rest must not change.
        PlotTargetRules.UnitFacts[] units =
        [
            Facts(),
            Facts(dead: true),
            Facts(pet: true),
            Facts(petOwner: true),
            Facts(slave: true),
            Facts(ownsPet: true)
        ];

        foreach (var unit in units)
        {
            await Assert.That(PlotTargetRules.PassesEventFilters(false, false, false, false, unit)).IsTrue();
        }
    }

    [Test]
    public async Task OnlyDieUnit_KeepsCorpsesOnly()
    {
        // 12 events, e.g. plot 2506's "6버블 대상 검색".
        await Assert.That(PlotTargetRules.PassesEventFilters(true, false, false, false, Facts(dead: true))).IsTrue();
        await Assert.That(PlotTargetRules.PassesEventFilters(true, false, false, false, Facts())).IsFalse();
    }

    [Test]
    public async Task OnlyMyPet_KeepsTheCastersPetOnly()
    {
        // Plot 3005, 사람이 자신의 펫한테 기술 사용 ("a player uses a skill on their own pet").
        await Assert.That(PlotTargetRules.PassesEventFilters(false, true, false, false, Facts(pet: true))).IsTrue();
        await Assert.That(PlotTargetRules.PassesEventFilters(false, true, false, false, Facts(slave: true))).IsFalse();
        await Assert.That(PlotTargetRules.PassesEventFilters(false, true, false, false, Facts())).IsFalse();
    }

    [Test]
    public async Task OnlyPetOwner_KeepsAPetsOwnerOnly()
    {
        // Plot 3004, 펫이 자기 주인에게 기술 사용 ("a pet uses a skill on its own owner"): the caster is the
        // pet, so the owner is recognised by the caster. Plot 3666 "PC 대상 지정" instead asks whether the
        // candidate owns a pet.
        await Assert.That(PlotTargetRules.PassesEventFilters(false, false, true, false, Facts(petOwner: true))).IsTrue();
        await Assert.That(PlotTargetRules.PassesEventFilters(false, false, true, false, Facts(ownsPet: true))).IsTrue();
        await Assert.That(PlotTargetRules.PassesEventFilters(false, false, true, false, Facts(pet: true))).IsFalse();
        await Assert.That(PlotTargetRules.PassesEventFilters(false, false, true, false, Facts())).IsFalse();
    }

    [Test]
    public async Task OnlyMySlave_KeepsTheCastersSlavesOnly()
    {
        // Plot 2706 대포 타겟: the ship's cannon search over its own slaves.
        await Assert.That(PlotTargetRules.PassesEventFilters(false, false, false, true, Facts(slave: true))).IsTrue();
        await Assert.That(PlotTargetRules.PassesEventFilters(false, false, false, true, Facts(pet: true))).IsFalse();
        await Assert.That(PlotTargetRules.PassesEventFilters(false, false, false, true, Facts())).IsFalse();
    }

    [Test]
    public async Task FlagsCombineAsAnd()
    {
        // Plot 3290 sets only_my_pet and only_pet_owner on the same event; nothing can be both, so the
        // search is empty — which is what its two sibling events ("Dispel effect_Pet" / "Dispel effect_PC")
        // exist to split.
        await Assert.That(PlotTargetRules.PassesEventFilters(false, true, true, false, Facts(pet: true))).IsFalse();
        await Assert.That(PlotTargetRules.PassesEventFilters(false, true, true, false, Facts(petOwner: true))).IsFalse();
    }

    [Test]
    public async Task NeutralUnits_StayOnlyWhenTheRowNamesARelation()
    {
        // "any" (4,715 areas) keeps the historical exclusion of Neutral units; everything else lets the
        // relation filter decide, which is what un-empties the 64 Area and 16 RandomUnit searches that ask
        // for relation 5 "others" — SkillTargetingUtil defines "others" as the Neutral units.
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any)).IsFalse();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Hostile)).IsTrue();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Others)).IsTrue();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Friendly)).IsTrue();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Raid)).IsTrue();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.IgnoreProtected)).IsTrue();
    }

    private static PlotCondition Tag(int tagId, bool not = false) =>
        new() { Kind = PlotConditionType.BuffTag, Param1 = tagId, NotCondition = not };

    [Test]
    public async Task AnyRelation_KeepsNeutralsWhenTheSearchRequiresABuffTag()
    {
        // Hereafter soul rescue (plot 4186, event 37541): a 1000 m "any" search for the neutral gathering
        // point, narrowed to units carrying tag 4181. Dropping Neutrals left it nothing to credit.
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, [Tag(4181)])).IsTrue();
        // The soul search pairs a required tag with an excluded one; the required tag is what counts.
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, [Tag(4203), Tag(4204, not: true)])).IsTrue();
    }

    [Test]
    public async Task AnyRelation_StillDropsNeutralsWithoutARequiredTag()
    {
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, null)).IsFalse();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, [])).IsFalse();
        // Excluding a tag says what NOT to hit; it does not name the target.
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, [Tag(4204, not: true)])).IsFalse();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any,
            [new PlotCondition { Kind = PlotConditionType.Chance, Param1 = 50 }])).IsFalse();
    }

    private static PlotCondition Reqs(uint id, bool not = false) =>
        new() { Id = id, Kind = PlotConditionType.UnitReqs, NotCondition = not };

    private static Func<uint, IEnumerable<UnitReqsKindType>> Kinds(uint id, params UnitReqsKindType[] kinds) =>
        conditionId => conditionId == id ? kinds : [];

    [Test]
    public async Task AnyRelation_KeepsNeutralsWhenAUnitReqsConditionNamesAMarker()
    {
        // Hereafter hellhound (plot 4500, event 40504): an 80 m "any" search for the defenders hunting it,
        // narrowed by condition 16409's unit_reqs row buff 25741. The hellhound is Neutral.
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, [Reqs(16409)],
            Kinds(16409, UnitReqsKindType.Buff))).IsTrue();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, [Reqs(1)],
            Kinds(1, UnitReqsKindType.TargetNpc))).IsTrue();
    }

    [Test]
    public async Task AnyRelation_DropsNeutralsWhenTheUnitReqsNameNoMarker()
    {
        // "no buff" excludes; it does not name the target. Neither does a negated condition.
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, [Reqs(2)],
            Kinds(2, UnitReqsKindType.NoBuff))).IsFalse();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, [Reqs(3, not: true)],
            Kinds(3, UnitReqsKindType.Buff))).IsFalse();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, [Reqs(4)],
            Kinds(5, UnitReqsKindType.Buff))).IsFalse();
        await Assert.That(PlotTargetRules.AllowsNeutral(SkillTargetRelation.Any, [Reqs(4)], null)).IsFalse();
    }

    [Test]
    public async Task MarkedVictims_AreAnAnyRelationSearchThatNamesAMarker()
    {
        // Hereafter hellhound (plot 4500, event 40505): the 30 m kill search keeps condition 16409.
        await Assert.That(PlotTargetRules.SelectsMarkedVictims(SkillTargetRelation.Any, [Reqs(16409)],
            Kinds(16409, UnitReqsKindType.Buff))).IsTrue();
        await Assert.That(PlotTargetRules.SelectsMarkedVictims(SkillTargetRelation.Any, [Tag(894)], null)).IsTrue();
    }

    [Test]
    public async Task MarkedVictims_NotForHostileSearchesOrUnmarkedOnes()
    {
        await Assert.That(PlotTargetRules.SelectsMarkedVictims(SkillTargetRelation.Hostile, [Reqs(16409)],
            Kinds(16409, UnitReqsKindType.Buff))).IsFalse();
        await Assert.That(PlotTargetRules.SelectsMarkedVictims(SkillTargetRelation.Any, [Reqs(2)],
            Kinds(2, UnitReqsKindType.NoBuff))).IsFalse();
        await Assert.That(PlotTargetRules.SelectsMarkedVictims(SkillTargetRelation.Any, [], null)).IsFalse();
        await Assert.That(PlotTargetRules.SelectsMarkedVictims(SkillTargetRelation.Any, null, null)).IsFalse();
    }

    [Test]
    public async Task AoeUnitReqs_AreJudgedOnTheCandidate()
    {
        const string caster = "caster", candidate = "candidate";
        await Assert.That(PlotTargetRules.AoeConditionOwner(PlotConditionType.UnitReqs, caster, candidate))
            .IsEqualTo(candidate);
        await Assert.That(PlotTargetRules.AoeConditionOwner(PlotConditionType.BuffTag, caster, candidate))
            .IsEqualTo(caster);
        await Assert.That(PlotTargetRules.AoeConditionOwner(PlotConditionType.Range, caster, candidate))
            .IsEqualTo(caster);
    }
}
