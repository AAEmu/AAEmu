using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Shares <see cref="PassiveTreeAutoGrantRules.OverrideEnabled"/> with
/// <see cref="PassiveBuffLevelRulesTests"/> — must not run beside that suite or the env-hook asserts race.
/// </summary>
[NotInParallel]
public class PassiveTreeAutoGrantRulesTests
{
    [After(Test)]
    public void ClearOverride() => PassiveTreeAutoGrantRules.OverrideEnabled = null;

    [Test]
    public async Task IsAutoGrantCandidate_ActiveNoReq_AlwaysTrue()
    {
        PassiveTreeAutoGrantRules.OverrideEnabled = false;
        var template = new PassiveBuffTemplate { Active = true, ReqPoints = 0 };

        await Assert.That(PassiveTreeAutoGrantRules.IsAutoGrantCandidate(template)).IsTrue();
    }

    [Test]
    public async Task IsAutoGrantCandidate_TreeReq_FollowsEnv()
    {
        var template = new PassiveBuffTemplate { Active = false, ReqPoints = 3 };

        PassiveTreeAutoGrantRules.OverrideEnabled = true;
        await Assert.That(PassiveTreeAutoGrantRules.IsAutoGrantCandidate(template)).IsTrue();

        PassiveTreeAutoGrantRules.OverrideEnabled = false;
        await Assert.That(PassiveTreeAutoGrantRules.IsAutoGrantCandidate(template)).IsFalse();
    }

    [Test]
    public async Task IsAutoGrantCandidate_PaidGeneral_NeverAuto()
    {
        PassiveTreeAutoGrantRules.OverrideEnabled = true;
        var template = new PassiveBuffTemplate { Active = false, ReqPoints = 0, SkillPoints = 1 };

        await Assert.That(PassiveTreeAutoGrantRules.IsAutoGrantCandidate(template)).IsFalse();
    }
}
