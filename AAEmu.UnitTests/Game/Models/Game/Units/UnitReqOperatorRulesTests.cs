using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class UnitReqOperatorRulesTests
{
    // --- PassesBound: kinds 44, 45, 46, 47, 50, 77, 78, 127 ---------------------------------------------

    [Test]
    public async Task Bound_ModeZeroIsAtLeastValue2()
    {
        // HonorPoint row (0, 100): 100 honor passes, 99 does not.
        await Assert.That(UnitReqOperatorRules.PassesBound(0, 100, 100)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesBound(0, 101, 100)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesBound(0, 99, 100)).IsFalse();
    }

    [Test]
    public async Task Bound_ModeNonZeroIsAtMostValue2()
    {
        // CrimeRecord row (1, 9): a clean record passes, ten infractions do not.
        await Assert.That(UnitReqOperatorRules.PassesBound(1, 0, 9)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesBound(1, 9, 9)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesBound(1, 10, 9)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesBound(7, 10, 9)).IsFalse();
    }

    [Test]
    public async Task Bound_LeadershipPeriodRowNeedsThousandPoints()
    {
        // Kind 127 rows (0, 1000) on components 41095 and siblings.
        await Assert.That(UnitReqOperatorRules.PassesBound(0, 0, 1000)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesBound(0, 999, 1000)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesBound(0, 1000, 1000)).IsTrue();
    }

    // --- PassesPoolCompare: kinds 26, 95, 96, 97, 138, 139 --------------------------------------------

    [Test]
    public async Task PoolCompare_ModeZeroIsAbsolute()
    {
        // TargetHealthLessThan (0, 50): 50 HP passes regardless of the maximum, 51 does not.
        await Assert.That(UnitReqOperatorRules.PassesPoolCompare(0, 50, 10000, 50, lessThan: true)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesPoolCompare(0, 51, 10000, 50, lessThan: true)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesPoolCompare(0, 50, 10000, 50, lessThan: false)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesPoolCompare(0, 49, 10000, 50, lessThan: false)).IsFalse();
    }

    [Test]
    public async Task PoolCompare_ModeOneIsIntegerPercent()
    {
        // 499 of 1000 is 49 percent after integer division, so "less than 49" passes and "more than 50" fails.
        await Assert.That(UnitReqOperatorRules.PassesPoolCompare(1, 499, 1000, 49, lessThan: true)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesPoolCompare(1, 500, 1000, 49, lessThan: true)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesPoolCompare(1, 500, 1000, 50, lessThan: false)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesPoolCompare(1, 499, 1000, 50, lessThan: false)).IsFalse();
    }

    [Test]
    public async Task PoolCompare_PercentWithoutMaximumFails()
    {
        await Assert.That(UnitReqOperatorRules.PassesPoolCompare(1, 10, 0, 0, lessThan: true)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesPoolCompare(1, 10, 0, 0, lessThan: false)).IsFalse();
    }

    // --- Margins: kinds 65, 66, 67, 99, 107 -------------------------------------------------------------

    [Test]
    public async Task Margin_NeedsRoomOfAtLeastValue1()
    {
        await Assert.That(UnitReqOperatorRules.PassesMargin(5000, 4000, 1000)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesMargin(5000, 4001, 1000)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesMargin(5000, 5000, 0)).IsTrue();
    }

    [Test]
    public async Task FullRecharged_PassesOnlyWhileBelowTheCap()
    {
        // Labor potions are refused at the cap and accepted one point below it.
        await Assert.That(UnitReqOperatorRules.PassesNotFullyRecharged(4999, 5000)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesNotFullyRecharged(5000, 5000)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesNotFullyRecharged(5001, 5000)).IsFalse();
    }

    // --- GearScore: kind 122 -------------------------------------------------------------------------

    [Test]
    public async Task GearScore_ModeZeroIsAtLeastAndModeOneAtMost()
    {
        // Content pairs (0, 3000) with (1, 2999): 3000 satisfies the first, 2999 the second, never both.
        await Assert.That(UnitReqOperatorRules.PassesGearScore(0, 3000, 3000)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesGearScore(0, 2999, 3000)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesGearScore(1, 2999, 2999)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesGearScore(1, 3000, 2999)).IsFalse();
    }

    [Test]
    public async Task GearScore_DetailIsValue2NegatedForTheUpperBound()
    {
        await Assert.That(UnitReqOperatorRules.GearScoreDetail(0, 3000)).IsEqualTo(3000u);
        await Assert.That(UnitReqOperatorRules.GearScoreDetail(1, 2999)).IsEqualTo(unchecked((uint)-2999));
    }

    // --- Factions: kinds 40, 42, 56, 59 ---------------------------------------------------------------

    [Test]
    public async Task FactionMatch_AcceptsOwnOrMotherFaction()
    {
        // A Nuian (101, mother 148) matches both 101 and 148, not 149.
        await Assert.That(UnitReqOperatorRules.PassesFactionMatch(101, 148, 101)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesFactionMatch(101, 148, 148)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesFactionMatch(101, 148, 149)).IsFalse();
    }

    [Test]
    public async Task RootFaction_ComparesRootsOfBothSides()
    {
        // MotherFaction value1 161 (pirates, mother 114) accepts an outlaw (114, no mother) and a pirate.
        await Assert.That(UnitReqOperatorRules.SharesRootFaction(114, 0, 161, 114)).IsTrue();
        await Assert.That(UnitReqOperatorRules.SharesRootFaction(161, 114, 161, 114)).IsTrue();
        // value1 101 (Nuian) accepts an Elf (104, mother 148) and refuses a Firran (109, mother 149).
        await Assert.That(UnitReqOperatorRules.SharesRootFaction(104, 148, 101, 148)).IsTrue();
        await Assert.That(UnitReqOperatorRules.SharesRootFaction(109, 149, 101, 148)).IsFalse();
    }

    // --- Details: kind 75 ----------------------------------------------------------------------------

    [Test]
    public async Task LessActAbility_PacksValue1AboveValue2()
    {
        await Assert.That(UnitReqOperatorRules.LessActAbilityDetail(29, 20000)).IsEqualTo(29u * 0x2000000 + 20000);
    }

    // --- ExpeditionLevel: kind 90 ---------------------------------------------------------------------

    [Test]
    public async Task ExpeditionLevel_IsInclusiveEitherOrder()
    {
        await Assert.That(UnitReqOperatorRules.PassesExpeditionLevel(3, 3, 3)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesExpeditionLevel(3, 3, 2)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesExpeditionLevel(3, 3, 4)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesExpeditionLevel(5, 2, 2)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesExpeditionLevel(5, 2, 5)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesExpeditionLevel(5, 2, 6)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesExpeditionLevel(1, 1, 0)).IsFalse();
    }

    // --- ExpeditionMember: kind 71 --------------------------------------------------------------------

    [Test]
    public async Task ExpeditionMember_NoExpeditionIsDetail328()
    {
        await Assert.That(UnitReqOperatorRules.ExpeditionMemberDetail(null, 0))
            .IsEqualTo(UnitReqOperatorRules.ExpeditionMemberNoExpeditionDetail);
        await Assert.That(UnitReqOperatorRules.ExpeditionMemberNoExpeditionDetail).IsEqualTo((ushort)0x328);
    }

    [Test]
    public async Task ExpeditionMember_RoleBelowValue1IsDetail506()
    {
        // The 255 rows are owner-only: an officer (role 3) fails them and passes the role 3 rows.
        await Assert.That(UnitReqOperatorRules.ExpeditionMemberDetail(3, 255))
            .IsEqualTo(UnitReqOperatorRules.ExpeditionMemberRoleDetail);
        await Assert.That(UnitReqOperatorRules.ExpeditionMemberRoleDetail).IsEqualTo((ushort)0x506);
        await Assert.That(UnitReqOperatorRules.ExpeditionMemberDetail(255, 255)).IsEqualTo((ushort)0);
        await Assert.That(UnitReqOperatorRules.ExpeditionMemberDetail(3, 3)).IsEqualTo((ushort)0);
        await Assert.That(UnitReqOperatorRules.ExpeditionMemberDetail(1, 0)).IsEqualTo((ushort)0);
    }

    // --- Hero: kind 79 ---------------------------------------------------------------------------------

    [Test]
    public async Task Hero_ValueZeroIsAnySeatAndOtherwiseTheGrade()
    {
        await Assert.That(UnitReqOperatorRules.PassesHero(0, true, 0)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesHero(0, false, 0)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesHero(4, false, 4)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesHero(4, true, 3)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesHero(4, false, 0)).IsFalse();
    }

    // --- State gates: kinds 116 (duel) and 117 (expedition battle) ---------------------------------------

    [Test]
    public async Task StateGate_ZeroNeedsTheStateOneNeedsItsAbsence()
    {
        await Assert.That(UnitReqOperatorRules.PassesStateGate(0, true)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesStateGate(0, false)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesStateGate(1, false)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesStateGate(1, true)).IsFalse();
    }

    [Test]
    public async Task StateGate_OtherValuesPass()
    {
        await Assert.That(UnitReqOperatorRules.PassesStateGate(2, true)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesStateGate(2, false)).IsTrue();
    }

    // --- Dominions: kinds 62, 63, 86, 87, 120 -----------------------------------------------------------

    [Test]
    public async Task DominionOwnerKey_PrefersTheExpedition()
    {
        await Assert.That(UnitReqOperatorRules.DominionOwnerKey(5000, 101, 148)).IsEqualTo(5000u);
        await Assert.That(UnitReqOperatorRules.DominionOwnerKey(0, 101, 148)).IsEqualTo(148u);
        await Assert.That(UnitReqOperatorRules.DominionOwnerKey(0, 148, 0)).IsEqualTo(148u);
    }

    [Test]
    public async Task DominionOwnership_GuildClaimsMatchTheExpeditionOnly()
    {
        // A guild claim (expedition 5000, faction 0) belongs to that guild, not to the guild's alliance.
        await Assert.That(UnitReqOperatorRules.IsDominionOwnedBy(5000, 0, 5000)).IsTrue();
        await Assert.That(UnitReqOperatorRules.IsDominionOwnedBy(5000, 0, 148)).IsFalse();
        // A faction claim (expedition 0, faction 148) belongs to every alliance member without a guild.
        await Assert.That(UnitReqOperatorRules.IsDominionOwnedBy(0, 148, 148)).IsTrue();
        await Assert.That(UnitReqOperatorRules.IsDominionOwnedBy(0, 148, 5000)).IsFalse();
        await Assert.That(UnitReqOperatorRules.IsDominionOwnedBy(0, 0, 0)).IsFalse();
    }

    [Test]
    public async Task DominionCount_ModeZeroIsAtLeastValue1()
    {
        // PlotCondition rows (3, 0): three or more territories.
        await Assert.That(UnitReqOperatorRules.PassesDominionCount(0, 3, 3)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesDominionCount(0, 2, 3)).IsFalse();
    }

    [Test]
    public async Task DominionCount_ModeNonZeroIsAtMostValue1()
    {
        // PlotCondition rows (0, 1): no territory at all; (1, 1): at most one.
        await Assert.That(UnitReqOperatorRules.PassesDominionCount(1, 0, 0)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesDominionCount(1, 1, 0)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesDominionCount(1, 1, 1)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesDominionCount(1, 2, 1)).IsFalse();
    }

    // --- ConflictZoneState: kind 129 --------------------------------------------------------------------

    [Test]
    public async Task ConflictZoneState_ValueZeroTestsWarOrPeace()
    {
        // BuffTickEffect rows (0, 0): the group must be in neither war nor peace.
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(0, 0, ZoneConflictType.Tension)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(0, 0, ZoneConflictType.Crisis)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(0, 0, ZoneConflictType.War)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(0, 0, ZoneConflictType.Peace)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(0, 1, ZoneConflictType.War)).IsTrue();
    }

    [Test]
    public async Task ConflictZoneState_ValueOneTestsNotPeaceAndValueTwoNotWar()
    {
        // QuestComponent row (1, 0): peace only. PlotCondition rows (1, 1): anything but peace.
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(1, 0, ZoneConflictType.Peace)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(1, 0, ZoneConflictType.War)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(1, 1, ZoneConflictType.War)).IsTrue();
        // BuffTickEffect rows (2, 0): war only.
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(2, 0, ZoneConflictType.War)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(2, 0, ZoneConflictType.Peace)).IsFalse();
    }

    [Test]
    public async Task ConflictZoneState_UnknownSelectorFails()
    {
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(3, 0, ZoneConflictType.War)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesConflictZoneState(3, 1, ZoneConflictType.War)).IsFalse();
    }

    // --- Housing: kinds 64 and 83 ----------------------------------------------------------------------

    [Test]
    public async Task Housing_ValueTwoOneNeedsTheMatch()
    {
        await Assert.That(UnitReqOperatorRules.PassesHousing(1, true)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesHousing(1, false)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesHousing(0, false)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesHousing(0, true)).IsFalse();
    }

    // --- TowerDefStep: kind 137 -----------------------------------------------------------------------

    [Test]
    public async Task TowerDefStep_MatchesTheZeroBasedStepOfAOneBasedIndex()
    {
        // Rows (140, 165, 1..3) against tower 165's four progs.
        await Assert.That(UnitReqOperatorRules.PassesTowerDefStep(0, 1, 4)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesTowerDefStep(1, 2, 4)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesTowerDefStep(2, 2, 4)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesTowerDefStep(null, 1, 4)).IsFalse();
    }

    [Test]
    public async Task TowerDefStep_IndexOutsideTheProgsFails()
    {
        await Assert.That(UnitReqOperatorRules.PassesTowerDefStep(0, 0, 4)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesTowerDefStep(4, 5, 4)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesTowerDefStep(3, 4, 4)).IsTrue();
    }

    // --- EnableArchePass: kind 132 --------------------------------------------------------------------

    [Test]
    public async Task EnableArchePass_ValueZeroAcceptsAnyPassInProgress()
    {
        await Assert.That(UnitReqOperatorRules.PassesEnableArchePass(0, true, false)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesEnableArchePass(0, false, false)).IsFalse();
    }

    [Test]
    public async Task EnableArchePass_OtherValuesNeedThatPass()
    {
        await Assert.That(UnitReqOperatorRules.PassesEnableArchePass(9, true, true)).IsTrue();
        await Assert.That(UnitReqOperatorRules.PassesEnableArchePass(9, true, false)).IsFalse();
        await Assert.That(UnitReqOperatorRules.PassesEnableArchePass(9, false, false)).IsFalse();
    }

    // --- Result object --------------------------------------------------------------------------------

    [Test]
    public async Task ValidationResult_DisplayGateDefaultsOnAndNativeByteDefaultsOff()
    {
        var result = new UnitReqsValidationResult(SkillResultKeys.skill_failure, 0, 0);
        await Assert.That(result.DisplayMessage).IsTrue();
        await Assert.That(result.NativeResult).IsNull();
    }
}
