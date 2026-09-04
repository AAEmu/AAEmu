using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class EquipmentBuffRulesTests
{
    private const uint BasicEngine = 16149;
    private const uint MythicEngine = 16159;

    [Test]
    public async Task KeepWithdrawnBuff_DropsTheBuffWhenNoCopiesRemain()
    {
        // The piece-count lookup floors zero to one, so "still earned" would otherwise
        // equal the buff that just left. Zero copies must always clear it.
        await Assert.That(EquipmentBuffRules.KeepWithdrawnBuff(0, BasicEngine, BasicEngine)).IsFalse();
    }

    [Test]
    public async Task KeepWithdrawnBuff_KeepsTheBuffWhenACopyStillEarnsIt()
    {
        await Assert.That(EquipmentBuffRules.KeepWithdrawnBuff(1, BasicEngine, BasicEngine)).IsTrue();
    }

    [Test]
    public async Task KeepWithdrawnBuff_DropsTheOldGradeWhenTheRemainingCopyEarnsAnother()
    {
        // Two-piece → one-piece sail: the leftover copy now earns the other tier.
        await Assert.That(EquipmentBuffRules.KeepWithdrawnBuff(1, BasicEngine, MythicEngine)).IsFalse();
    }

    [Test]
    public async Task KeepWithdrawnBuff_DropsWhenNothingIsEarned()
    {
        await Assert.That(EquipmentBuffRules.KeepWithdrawnBuff(1, 0, BasicEngine)).IsFalse();
    }

    [Test]
    public async Task StripOtherGrade_RemovesADifferentGradeWhenNoCopyEarnsIt()
    {
        await Assert.That(EquipmentBuffRules.StripOtherGrade(MythicEngine, BasicEngine, 0)).IsTrue();
    }

    [Test]
    public async Task StripOtherGrade_KeepsTheIncomingBuff()
    {
        await Assert.That(EquipmentBuffRules.StripOtherGrade(MythicEngine, MythicEngine, 0)).IsFalse();
    }

    [Test]
    public async Task StripOtherGrade_KeepsAGradeAnotherCopyStillEarns()
    {
        await Assert.That(EquipmentBuffRules.StripOtherGrade(MythicEngine, BasicEngine, 1)).IsFalse();
    }

    [Test]
    public async Task StripOtherGrade_IgnoresAMissingBuff()
    {
        await Assert.That(EquipmentBuffRules.StripOtherGrade(MythicEngine, 0, 0)).IsFalse();
    }
}
