using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestSellBackpackGoodRulesTests
{
    // quest_act_obj_sell_backpack_goods 6: quest 4665, Item 43323, group 865, count 1.
    [Test]
    public async Task ContentMatches_ItemRowNeedsThePackTemplate()
    {
        await Assert.That(QuestSellBackpackGoodRules.ContentMatches("Item", 43323, 43323, null)).IsTrue();
        await Assert.That(QuestSellBackpackGoodRules.ContentMatches("Item", 43323, 43324, null)).IsFalse();
    }

    // Row 9: test quest 9011, Tag 3197, group 795, count 1; the pack carries the tag through item_tags.
    [Test]
    public async Task ContentMatches_TagRowAsksTheTagTable()
    {
        static bool HasTag(uint itemId, uint tagId) => itemId == 31835 && tagId == 3197;
        await Assert.That(QuestSellBackpackGoodRules.ContentMatches("Tag", 3197, 31835, HasTag)).IsTrue();
        await Assert.That(QuestSellBackpackGoodRules.ContentMatches("Tag", 3197, 43323, HasTag)).IsFalse();
        await Assert.That(QuestSellBackpackGoodRules.ContentMatches("Tag", 3197, 31835, null)).IsFalse();
    }

    [Test]
    public async Task ContentMatches_FailsClosedOnUnknownTypeOrMissingIds()
    {
        await Assert.That(QuestSellBackpackGoodRules.ContentMatches("Other", 1, 1, (_, _) => true)).IsFalse();
        await Assert.That(QuestSellBackpackGoodRules.ContentMatches("Item", 0, 0, null)).IsFalse();
        await Assert.That(QuestSellBackpackGoodRules.ContentMatches("Item", 43323, 0, null)).IsFalse();
    }

    [Test]
    public async Task OutletMatches_NeedsTheGroupWhenTheRowNamesOne()
    {
        await Assert.That(QuestSellBackpackGoodRules.OutletMatches(865, true)).IsTrue();
        await Assert.That(QuestSellBackpackGoodRules.OutletMatches(865, false)).IsFalse();
        await Assert.That(QuestSellBackpackGoodRules.OutletMatches(0, false)).IsTrue();
    }
}
