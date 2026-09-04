using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public class SportFishCombatTests
{
    [Test]
    public async Task WithoutZoneAuthority_AllInCombatSkillsRunOnWorld()
    {
        await Assert.That(SportFishCombat.ShouldWorldApplyInCombatSkill(false, false, 21096)).IsTrue();
        await Assert.That(SportFishCombat.ShouldWorldApplyInCombatSkill(false, true, 21096)).IsTrue();
        await Assert.That(SportFishCombat.ShouldWorldApplyInCombatSkill(false, false, SportFishCombat.BiteSkillId)).IsTrue();
    }

    [Test]
    public async Task ZoneMirror_OnlyBiteRunsOnWorld()
    {
        await Assert.That(SportFishCombat.ShouldWorldApplyInCombatSkill(true, true, SportFishCombat.BiteSkillId)).IsTrue();
        await Assert.That(SportFishCombat.ShouldWorldApplyInCombatSkill(true, true, 21096)).IsFalse();
        await Assert.That(SportFishCombat.ShouldWorldApplyInCombatSkill(true, true, 21208)).IsFalse();
    }

    [Test]
    public async Task ZoneAuthorityNonMirror_KeepsFullWorldKit()
    {
        await Assert.That(SportFishCombat.ShouldWorldApplyInCombatSkill(true, false, 21096)).IsTrue();
    }

    [Test]
    public async Task HoldKit_RunsPlotGraphOnly()
    {
        uint[] fishing = [(uint)TagsEnum.FishingSkill];
        await Assert.That(SportFishCombat.ShouldRunPlotGraphOnly(true, true, false, fishing)).IsTrue();
        await Assert.That(SportFishCombat.ShouldRunPlotGraphOnly(true, true, true, [])).IsTrue();
        await Assert.That(SportFishCombat.ShouldRunPlotGraphOnly(true, true, false, [(uint)TagsEnum.Fish])).IsFalse();
        await Assert.That(SportFishCombat.ShouldRunPlotGraphOnly(false, true, false, fishing)).IsFalse();
        await Assert.That(SportFishCombat.ShouldRunPlotGraphOnly(true, false, false, fishing)).IsFalse();
    }

    [Test]
    public async Task HoldKit_IsHostileFishingTag()
    {
        uint[] fishing = [(uint)TagsEnum.FishingSkill];
        await Assert.That(SportFishCombat.IsFishingHoldSkill(SkillTargetType.Hostile, fishing)).IsTrue();
        await Assert.That(SportFishCombat.IsFishingHoldSkill(SkillTargetType.Pos, fishing)).IsFalse();
        await Assert.That(SportFishCombat.IsFishingHoldSkill(SkillTargetType.Hostile, [(uint)TagsEnum.Fish])).IsFalse();
    }

    [Test]
    public async Task HoldKit_BypassesSharedGcd()
    {
        uint[] fishing = [(uint)TagsEnum.FishingSkill];
        await Assert.That(SportFishCombat.ShouldBypassSharedGcd(0, SkillTargetType.Hostile, fishing)).IsTrue();
        await Assert.That(SportFishCombat.ShouldBypassSharedGcd(1500, SkillTargetType.Hostile, fishing)).IsFalse();
        await Assert.That(SportFishCombat.ShouldBypassSharedGcd(0, SkillTargetType.Pos, fishing)).IsFalse();
    }

    [Test]
    public async Task RodPlot_IgnoresClientStopCastingWhenSkillIsNotCancelable()
    {
        await Assert.That(SportFishCombat.IsRodPlot(SportFishCombat.BaitFishingPlotId)).IsTrue();
        await Assert.That(SportFishCombat.IsRodPlot(SportFishCombat.SportFishingPlotId)).IsTrue();
        await Assert.That(SportFishCombat.IsRodPlot(1)).IsFalse();
        await Assert.That(
            SportFishCombat.ShouldIgnoreClientStopCasting(
                SportFishCombat.BaitFishingPlotId,
                castingCancelable: false,
                channelingCancelable: false)).IsTrue();
        await Assert.That(
            SportFishCombat.ShouldIgnoreClientStopCasting(
                SportFishCombat.SportFishingPlotId,
                castingCancelable: false,
                channelingCancelable: false)).IsTrue();
        await Assert.That(
            SportFishCombat.ShouldIgnoreClientStopCasting(
                SportFishCombat.BaitFishingPlotId,
                castingCancelable: true,
                channelingCancelable: false)).IsFalse();
        await Assert.That(
            SportFishCombat.ShouldIgnoreClientStopCasting(
                1,
                castingCancelable: false,
                channelingCancelable: false)).IsFalse();
    }

    [Test]
    public async Task HoldSwitch_CancelsPreviousHold_KeepsRodChannel()
    {
        await Assert.That(SportFishCombat.ShouldCancelPreviousPlot(true, previousWasHold: true, incomingIsHold: true))
            .IsTrue();
        await Assert.That(SportFishCombat.ShouldCancelPreviousPlot(false, previousWasHold: true, incomingIsHold: true))
            .IsTrue();
        await Assert.That(SportFishCombat.ShouldCancelPreviousPlot(true, previousWasHold: false, incomingIsHold: true))
            .IsFalse();
        await Assert.That(SportFishCombat.ShouldCancelPreviousPlot(true, previousWasHold: false, incomingIsHold: false))
            .IsTrue();
        await Assert.That(SportFishCombat.ShouldCancelPreviousPlot(false, previousWasHold: false, incomingIsHold: false))
            .IsFalse();
    }

    [Test]
    public async Task DroppedLine_IsUnusableTarget()
    {
        await Assert.That(SportFishCombat.IsUnusableTarget(null)).IsFalse();
        await Assert.That(SportFishCombat.IsUnusableTarget(new Npc())).IsFalse();
        await Assert.That(SportFishCombat.IsUnusableTarget(new Npc { SportFishLineDropped = true })).IsTrue();
    }

    [Test]
    public async Task OnLineDropped_IsIdempotentAndClearsAggro()
    {
        var fish = new Npc { OwnerId = 0 };
        SportFishCombat.OnLineDropped(fish);
        await Assert.That(fish.SportFishLineDropped).IsTrue();
        await Assert.That(fish.IsInBattle).IsFalse();
        SportFishCombat.OnLineDropped(fish);
        await Assert.That(fish.SportFishLineDropped).IsTrue();
    }
}
