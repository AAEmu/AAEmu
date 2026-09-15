using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Justice;

public class ArrestRulesTests
{
    [Test]
    public async Task ArrestStates_AreTheThreeTheArrestSkillsApply()
    {
        // 체포 중.. (bot arrest 20387) and the player arrest 20323's pair: 제압당함 under 체포 중..2.
        await Assert.That(ArrestRules.IsArrestStateBuff(ArrestRules.UnderArrestBuff)).IsTrue();
        await Assert.That(ArrestRules.IsArrestStateBuff(ArrestRules.UnderArrestShortBuff)).IsTrue();
        await Assert.That(ArrestRules.IsArrestStateBuff(ArrestRules.SubduedBuff)).IsTrue();

        // The courthouse state, the prisoner buff and an unset id are not arrest states.
        await Assert.That(ArrestRules.IsArrestStateBuff(ArrestRules.ForcedMoveToCourtBuff)).IsFalse();
        await Assert.That(ArrestRules.IsArrestStateBuff(ArrestRules.PrisonerBuff)).IsFalse();
        await Assert.That(ArrestRules.IsArrestStateBuff(0)).IsFalse();
    }

    [Test]
    public async Task EscortWaitsForTheLongestArrestStateLeft()
    {
        // The player arrest stacks a five-second subdual under a one-second hold: the escort belongs at
        // the end of the five seconds, not at the end of the row somebody happened to hardcode.
        var held = new[]
        {
            CreateBuff(ArrestRules.SubduedBuff, milliseconds: 5000),
            CreateBuff(ArrestRules.UnderArrestShortBuff, milliseconds: 1000),
            CreateBuff(buffId: 25977, milliseconds: 60_000) // an unrelated buff must not set the pace
        };

        var left = ArrestRules.LongestArrestStateLeft(held);

        await Assert.That(left).IsGreaterThan(TimeSpan.FromSeconds(4.5));
        await Assert.That(left).IsLessThanOrEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task EscortDelay_IsZeroWithoutAnArrestState()
    {
        await Assert.That(ArrestRules.LongestArrestStateLeft([])).IsEqualTo(TimeSpan.Zero);
        await Assert.That(ArrestRules.LongestArrestStateLeft(null)).IsEqualTo(TimeSpan.Zero);
        await Assert.That(ArrestRules.LongestArrestStateLeft(
            [CreateBuff(ArrestRules.PrisonerBuff, milliseconds: 1_800_000)])).IsEqualTo(TimeSpan.Zero);
    }

    private static Buff CreateBuff(uint buffId, double milliseconds)
    {
        var owner = new BaseUnit();
        var template = new BuffTemplate { Id = buffId, Duration = (int)milliseconds };
        var buff = new Buff(owner, owner, new SkillCasterUnit(owner.ObjId), template, null, DateTime.UtcNow)
        {
            // AddBuff computes this from the template and its modifiers; a hand-built buff carries it here.
            Duration = (int)milliseconds
        };
        return buff;
    }
}
