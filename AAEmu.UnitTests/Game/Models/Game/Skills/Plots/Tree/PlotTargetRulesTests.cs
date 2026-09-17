using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;

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
}
