using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
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
}