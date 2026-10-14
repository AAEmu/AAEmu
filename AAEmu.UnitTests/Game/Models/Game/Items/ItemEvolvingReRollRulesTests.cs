using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemEvolvingReRollRulesTests
{
    [Test]
    public async Task LucidCast_ReadsSlotThenChosenGroup()
    {
        // Live skill 46234 / flag 9: values=[0, 0x4A8] → slot 0, group 1192 (Strength).
        int[] values = [0, 0x4A8];
        await Assert.That(ItemEvolvingReRollRules.TryReadSlotIndex(values, 2, usedLineCount: 3, out var slot))
            .IsTrue();
        await Assert.That(slot).IsEqualTo(0);
        await Assert.That(ItemEvolvingReRollRules.TryReadChosenGroupId(values, 2, out var groupId)).IsTrue();
        await Assert.That(groupId).IsEqualTo(1192u);
    }

    [Test]
    public async Task ChosenGroup_IsNotReadFromAThirdField()
    {
        // The old index (Values[2]) left lucid picks as zero and always rolled.
        int[] values = [0, 1192, 9999];
        await Assert.That(ItemEvolvingReRollRules.TryReadChosenGroupId(values, readCount: 2, out var groupId))
            .IsTrue();
        await Assert.That(groupId).IsEqualTo(1192u);
        // Values[2]=9999 must not be the pick — only the second field is.
        await Assert.That(groupId).IsNotEqualTo(9999u);
    }

    [Test]
    public async Task SlotIndex_RejectsOutOfRangeAndMissingExtras()
    {
        await Assert.That(ItemEvolvingReRollRules.TryReadSlotIndex([3], 1, usedLineCount: 2, out _))
            .IsFalse();
        await Assert.That(ItemEvolvingReRollRules.TryReadSlotIndex([], 0, usedLineCount: 2, out _))
            .IsFalse();
    }

    [Test]
    public async Task ChosenGroup_RejectsNonPositiveAndShortCast()
    {
        await Assert.That(ItemEvolvingReRollRules.TryReadChosenGroupId([0, 0], 2, out _)).IsFalse();
        await Assert.That(ItemEvolvingReRollRules.TryReadChosenGroupId([0], 1, out _)).IsFalse();
    }

    [Test]
    public async Task Payment_StoneIgnoresRemainingChanges()
    {
        // Serendipity / Lucent / lucid pay from the stone, not EvolveChance — even at 0.
        await Assert.That(ItemEvolvingReRollRules.ResolvePayment(hasStone: true, evolveChance: 0,
            requiresStone: false)).IsEqualTo(ItemEvolvingReRollPayment.LuckStone);
        await Assert.That(ItemEvolvingReRollRules.ResolvePayment(hasStone: true, evolveChance: 0,
            requiresStone: true)).IsEqualTo(ItemEvolvingReRollPayment.LuckStone);
        await Assert.That(ItemEvolvingReRollRules.ResolvePayment(hasStone: true, evolveChance: 3,
            requiresStone: true)).IsEqualTo(ItemEvolvingReRollPayment.LuckStone);
        await Assert.That(ItemEvolvingReRollRules.ResolvePayment(hasStone: true, evolveChance: 1,
            requiresStone: false)).IsEqualTo(ItemEvolvingReRollPayment.LuckStone);
    }

    [Test]
    public async Task Payment_FreeAttemptRequiresChanceAndNoStone()
    {
        await Assert.That(ItemEvolvingReRollRules.ResolvePayment(hasStone: false, evolveChance: 2,
            requiresStone: false)).IsEqualTo(ItemEvolvingReRollPayment.ChangeAttempt);
        await Assert.That(ItemEvolvingReRollRules.ResolvePayment(hasStone: false, evolveChance: 0,
            requiresStone: false)).IsEqualTo(ItemEvolvingReRollPayment.None);
    }

    [Test]
    public async Task Payment_LucidWithoutStoneIsRefused()
    {
        await Assert.That(ItemEvolvingReRollRules.ResolvePayment(hasStone: false, evolveChance: 5,
            requiresStone: true)).IsEqualTo(ItemEvolvingReRollPayment.None);
        await Assert.That(ItemEvolvingReRollRules.ResolvePayment(hasStone: false, evolveChance: 0,
            requiresStone: true)).IsEqualTo(ItemEvolvingReRollPayment.None);
    }

    [Test]
    public async Task IsStoneForSkill_RequiresMatchingUseSkillId()
    {
        await Assert.That(ItemEvolvingReRollRules.IsStoneForSkill(100, 100)).IsTrue();
        await Assert.That(ItemEvolvingReRollRules.IsStoneForSkill(100, 200)).IsFalse();
        await Assert.That(ItemEvolvingReRollRules.IsStoneForSkill(0, 100)).IsFalse();
        await Assert.That(ItemEvolvingReRollRules.IsStoneForSkill(100, 0)).IsFalse();
    }
}
