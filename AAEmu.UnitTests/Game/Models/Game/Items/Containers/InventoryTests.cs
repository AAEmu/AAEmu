using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Containers;

public class InventoryTests
{
    [Test]
    public async Task InventoryAddsItem()
    {
        // ItemIdManager.Instance.Initialize();

        var mockCharacter = new CharacterMock();
        var container = new ItemContainer(mockCharacter.Id, SlotType.Inventory, false, mockCharacter);
        var item = InventoryTestUtils.MockItem(1, 1);

        await Assert.That(container.AddOrMoveExistingItem(ItemTaskType.Gm, item, 1)).IsTrue();

        var i = container.Items.SingleOrDefault(it => it.TemplateId == 1);

        await Assert.That(i).IsNotNull();
    }

    [Test]
    public async Task AddOrMoveExistingItem_DoesNotMergeDifferentDetails()
    {
        var character = new CharacterMock();
        var container = new ItemContainer(character.Id, SlotType.Inventory, false, character);
        var template = new ItemTemplate { Id = 1, MaxCount = 100, BindType = ItemBindType.Normal };
        var existing = new ItemMock(1, template)
        {
            DetailType = ItemDetailType.Unknown14,
            Detail = Enumerable.Repeat((byte)1, 8).ToArray()
        };
        var incoming = new ItemMock(2, template)
        {
            DetailType = ItemDetailType.Unknown14,
            Detail = Enumerable.Repeat((byte)2, 8).ToArray()
        };
        container.AddOrMoveExistingItem(ItemTaskType.Gm, existing, 1);

        var added = container.AddOrMoveExistingItem(ItemTaskType.Gm, incoming, 1);

        await Assert.That(added).IsTrue();
        await Assert.That(container.Items).Count().IsEqualTo(2);
        await Assert.That(existing.Count).IsEqualTo(1);
        await Assert.That(incoming.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddOrMoveExistingItem_MergesIdenticalDetails()
    {
        var character = new CharacterMock();
        var container = new ItemContainer(character.Id, SlotType.Inventory, false, character);
        var template = new ItemTemplate { Id = 1, MaxCount = 100, BindType = ItemBindType.Normal };
        var detail = Enumerable.Repeat((byte)1, 8).ToArray();
        var existing = new ItemMock(1, template)
        {
            DetailType = ItemDetailType.Unknown14,
            Detail = detail
        };
        var incoming = new ItemMock(2, template)
        {
            DetailType = ItemDetailType.Unknown14,
            Detail = detail.ToArray()
        };
        container.AddOrMoveExistingItem(ItemTaskType.Gm, existing, 1);

        var added = container.AddOrMoveExistingItem(ItemTaskType.Gm, incoming, 1);

        await Assert.That(added).IsTrue();
        await Assert.That(container.Items).Count().IsEqualTo(1);
        await Assert.That(existing.Count).IsEqualTo(2);
    }
}
