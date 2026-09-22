using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// Requirement kinds whose client evaluator fills the 16-bit detail field must fail with that detail, because
/// the display path (x2game-dev.dll FUN_397EBE90) shows the detail's enum_error_messages text instead of the
/// result byte's symbol whenever the detail is set.
/// </summary>
public class UnitReqsFailureDetailTests
{
    private static Character Player(int periodPoints) =>
        new(new UnitCustomModelParams()) { LeadershipPeriodPoint = periodPoints };

    private static UnitReqs LeadershipPeriod(uint required) =>
        new() { KindType = UnitReqsKindType.LeadershipPeriod, Value1 = required };

    [Test]
    public async Task LeadershipPeriod_PassesAtTheThreshold()
    {
        var player = Player(5);

        var result = LeadershipPeriod(5).Validate(player, player);

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.ok);
        await Assert.That(result.ResultUShort).IsEqualTo((ushort)0);
        await Assert.That(result.ResultUInt).IsEqualTo(0u);
    }

    [Test]
    public async Task LeadershipPeriod_FailsWithTheClientDetailAndValue1()
    {
        // FUN_39795810: result 0x90, detail 0x355 (853 WRONG_LEADERSHIP_POINT), u32 = the row's value1.
        var player = Player(4);

        var result = LeadershipPeriod(5).Validate(player, player);

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.skill_urk_leadership_period);
        await Assert.That(result.ResultUShort).IsEqualTo((ushort)0x355);
        await Assert.That(result.ResultUInt).IsEqualTo(5u);
        await Assert.That(SkillResultHelper.SkillResultErrorKeyToId(result.ResultKey)).IsEqualTo(SkillResult.UrkLeadershipTotal);
    }

    [Test]
    public async Task LeadershipPeriod_NonPlayerOwner_FailsTheSameWay()
    {
        var npc = new Unit();

        var result = LeadershipPeriod(5).Validate(npc, npc);

        await Assert.That(result.ResultKey).IsEqualTo(SkillResultKeys.skill_urk_leadership_period);
        await Assert.That(result.ResultUShort).IsEqualTo((ushort)0x355);
        await Assert.That(result.ResultUInt).IsEqualTo(5u);
    }
}
