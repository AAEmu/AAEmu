using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillComboRulesTests
{
    private const uint Root = 1;
    private const uint Second = 2;
    private const uint Third = 3;

    private static uint? NextOf(uint id) => id switch
    {
        Root => Second,
        Second => Third,
        _ => null
    };

    [Test]
    public async Task TryGetComboFollowUp_ReadsComboSpecialOnly()
    {
        var template = new SkillTemplate
        {
            Effects =
            [
                new SkillEffect
                {
                    Template = new SpecialEffect
                    {
                        SpecialEffectTypeId = SpecialType.Cooldown,
                        Value1 = 1000
                    }
                },
                new SkillEffect
                {
                    Template = new SpecialEffect
                    {
                        SpecialEffectTypeId = SpecialType.Combo,
                        Value1 = 22,
                        Value2 = 1000
                    }
                }
            ]
        };

        await Assert.That(SkillComboRules.TryGetComboFollowUp(template, out var next, out var delay)).IsTrue();
        await Assert.That(next).IsEqualTo(22u);
        await Assert.That(delay).IsEqualTo(1000);
        await Assert.That(SkillComboRules.TryGetComboFollowUp(new SkillTemplate(), out _, out _)).IsFalse();
    }

    [Test]
    public async Task WalkChain_FollowsComboLinks()
    {
        var chain = SkillComboRules.WalkChain(Second, NextOf);
        await Assert.That(chain).IsEquivalentTo(new uint[] { Second, Third });
    }

    [Test]
    public async Task IsComboFollowUpOf_FindsSecondAndThirdHit()
    {
        await Assert.That(SkillComboRules.IsComboFollowUpOf(Root, Second, NextOf)).IsTrue();
        await Assert.That(SkillComboRules.IsComboFollowUpOf(Root, Third, NextOf)).IsTrue();
        await Assert.That(SkillComboRules.IsComboFollowUpOf(Root, Root, NextOf)).IsFalse();
        await Assert.That(SkillComboRules.IsComboFollowUpOf(Second, Root, NextOf)).IsFalse();
    }

    [Test]
    public async Task IsHeldOrComboFollowUp_AllowsLaterHitsOfAHeldRoot()
    {
        // Triple Slash / Quake heir / Whirlwind Slash: only the first hit is held; the bar swaps to 2 then 3.
        uint[] held = [Root];

        await Assert.That(SkillComboRules.IsHeldOrComboFollowUp(Root, held, NextOf)).IsTrue();
        await Assert.That(SkillComboRules.IsHeldOrComboFollowUp(Second, held, NextOf)).IsTrue();
        await Assert.That(SkillComboRules.IsHeldOrComboFollowUp(Third, held, NextOf)).IsTrue();
        await Assert.That(SkillComboRules.IsHeldOrComboFollowUp(99, held, NextOf)).IsFalse();
        await Assert.That(SkillComboRules.IsHeldOrComboFollowUp(Second, [], NextOf)).IsFalse();
    }

    [Test]
    public async Task IsNextComboWithinWindow_AllowsOnlyImmediateNextInsideWindow()
    {
        var firedAt = DateTime.UtcNow;
        int Window(uint id) => id == Root || id == Second ? 1000 : 0;

        await Assert.That(SkillComboRules.IsNextComboWithinWindow(
            Second, Root, firedAt, firedAt.AddMilliseconds(500), NextOf, Window)).IsTrue();
        await Assert.That(SkillComboRules.IsNextComboWithinWindow(
            Third, Root, firedAt, firedAt.AddMilliseconds(500), NextOf, Window)).IsFalse();
        await Assert.That(SkillComboRules.IsNextComboWithinWindow(
            Second, Root, firedAt, firedAt.AddMilliseconds(1500), NextOf, Window)).IsFalse();
        await Assert.That(SkillComboRules.IsNextComboWithinWindow(
            Second, 0, firedAt, firedAt, NextOf, Window)).IsFalse();
    }

    [Test]
    public async Task ApplyAcceptedCast_KeepsPriorWindowsWhenAnotherSkillFires()
    {
        var now = DateTime.UtcNow;
        var expires = new Dictionary<uint, DateTime>();
        int Window(uint id) => id == Root || id == Second ? 1000 : 0;

        SkillComboRules.ApplyAcceptedCast(Root, now, expires, NextOf, Window);
        await Assert.That(SkillComboRules.IsOpenComboFollowUp(Second, expires, now)).IsTrue();

        SkillComboRules.ApplyAcceptedCast(99, now, expires, NextOf, Window);
        await Assert.That(SkillComboRules.IsOpenComboFollowUp(Second, expires, now)).IsTrue();
    }

    [Test]
    public async Task ApplyAcceptedCast_ConsumesFollowUpAndOpensNext()
    {
        var now = DateTime.UtcNow;
        var expires = new Dictionary<uint, DateTime>();
        int Window(uint id) => id == Root || id == Second ? 1000 : 0;

        SkillComboRules.ApplyAcceptedCast(Root, now, expires, NextOf, Window);
        SkillComboRules.ApplyAcceptedCast(Second, now, expires, NextOf, Window);

        await Assert.That(SkillComboRules.IsOpenComboFollowUp(Second, expires, now)).IsFalse();
        await Assert.That(SkillComboRules.IsOpenComboFollowUp(Third, expires, now)).IsTrue();
    }

    [Test]
    public async Task PruneExpiredFollowUps_DropsPastWindows()
    {
        var now = DateTime.UtcNow;
        var expires = new Dictionary<uint, DateTime>
        {
            [Second] = now.AddMilliseconds(-1),
            [Third] = now.AddMilliseconds(500)
        };

        SkillComboRules.PruneExpiredFollowUps(expires, now);
        await Assert.That(expires.ContainsKey(Second)).IsFalse();
        await Assert.That(expires.ContainsKey(Third)).IsTrue();
    }

    [Test]
    public async Task CloseFollowUpOpenedBy_DropsNextWindowOnly()
    {
        var now = DateTime.UtcNow;
        var expires = new Dictionary<uint, DateTime>();
        int Window(uint id) => id == Root || id == Second ? 1000 : 0;

        SkillComboRules.ApplyAcceptedCast(Root, now, expires, NextOf, Window);
        SkillComboRules.ApplyAcceptedCast(99, now, expires, _ => 88u, _ => 1000);
        await Assert.That(expires.ContainsKey(Second)).IsTrue();
        await Assert.That(expires.ContainsKey(88u)).IsTrue();

        SkillComboRules.CloseFollowUpOpenedBy(Root, expires, NextOf);
        await Assert.That(expires.ContainsKey(Second)).IsFalse();
        await Assert.That(expires.ContainsKey(88u)).IsTrue();
    }

    [Test]
    public async Task RemapHoldRepeat_RootPressAdvancesToOpenFollowUp()
    {
        var now = DateTime.UtcNow;
        var expires = new Dictionary<uint, DateTime>();
        int Window(uint id) => id == Root || id == Second ? 1000 : 0;
        SkillComboRules.ApplyAcceptedCast(Root, now, expires, NextOf, Window);

        await Assert.That(SkillComboRules.RemapHoldRepeatToOpenFollowUp(
            Root, startAutoAttack: true, NextOf, expires, now)).IsEqualTo(Second);
        await Assert.That(SkillComboRules.RemapHoldRepeatToOpenFollowUp(
            Root, startAutoAttack: false, NextOf, expires, now)).IsEqualTo(Root);
        await Assert.That(SkillComboRules.RemapHoldRepeatToOpenFollowUp(
            Second, startAutoAttack: true, NextOf, expires, now)).IsEqualTo(Second);
    }

    [Test]
    public async Task RemapHoldRepeat_SkipsConsumedLinkToLaterOpen()
    {
        var now = DateTime.UtcNow;
        var expires = new Dictionary<uint, DateTime>();
        int Window(uint id) => id == Root || id == Second ? 1000 : 0;
        SkillComboRules.ApplyAcceptedCast(Root, now, expires, NextOf, Window);
        SkillComboRules.ApplyAcceptedCast(Second, now, expires, NextOf, Window);

        await Assert.That(SkillComboRules.RemapHoldRepeatToOpenFollowUp(
            Root, startAutoAttack: true, NextOf, expires, now)).IsEqualTo(Third);
    }

    [Test]
    public async Task RemapHoldRepeat_NoOpenWindow_LeavesRoot()
    {
        var now = DateTime.UtcNow;
        var expires = new Dictionary<uint, DateTime>();

        await Assert.That(SkillComboRules.RemapHoldRepeatToOpenFollowUp(
            Root, startAutoAttack: true, NextOf, expires, now)).IsEqualTo(Root);
    }
}
