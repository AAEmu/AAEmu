using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.UnitTests.Game.Models.Game.Rankings;

public class RankingRulesTests
{
    [Test]
    public async Task ItemBoards_MeasureTheWeaponKindsTheTableShips()
    {
        // 24 one-hand (holdable slots 14/15/17), 25 two-hand (16), 26 ranged (18)
        await Assert.That(RankingRules.ItemBoardSlots(24)).IsEquivalentTo(new byte[] { 14, 15, 17 });
        await Assert.That(RankingRules.ItemBoardSlots(25)).IsEquivalentTo(new byte[] { 16 });
        await Assert.That(RankingRules.ItemBoardSlots(26)).IsEquivalentTo(new byte[] { 18 });

        // the gear score board and anything else is not an item board
        await Assert.That(RankingRules.ItemBoardSlots(23)).IsNull();
        await Assert.That(RankingRules.ItemBoardSlots(999)).IsNull();
    }

    [Test]
    public async Task BestItem_TakesTheHighestScoreOfTheBoardsKind()
    {
        var weapons = new[]
        {
            (ItemId: 100UL, SlotTypeId: (byte)17, Score: 500), // one-hand, weaker
            (ItemId: 200UL, SlotTypeId: (byte)14, Score: 900), // one-hand caster, stronger
            (ItemId: 300UL, SlotTypeId: (byte)18, Score: 700)  // ranged, not this board
        };

        var best = RankingRules.BestItem(weapons, RankingRules.ItemBoardSlots(24));

        await Assert.That(best).IsEqualTo((200UL, 900));
    }

    [Test]
    public async Task BestItem_IsNullWhenNothingOfThatKindIsWorn()
    {
        var weapons = new[] { (ItemId: 100UL, SlotTypeId: (byte)17, Score: 500) };

        // a one-hand user has nothing to show on the two-hand board
        await Assert.That(RankingRules.BestItem(weapons, RankingRules.ItemBoardSlots(25))).IsNull();
        await Assert.That(RankingRules.BestItem([], RankingRules.ItemBoardSlots(24))).IsNull();
        await Assert.That(RankingRules.BestItem(null, RankingRules.ItemBoardSlots(24))).IsNull();
        await Assert.That(RankingRules.BestItem(weapons, null)).IsNull();
    }
}
