using System.Linq;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Containers;

public class InventoryTests
{
    [Fact]
    public void InventoryAddsItem()
    {
        // ItemIdManager.Instance.Initialize();

        var mockCharacter = new CharacterMock();
        var container = new ItemContainer(mockCharacter.Id, SlotType.Bag, false, mockCharacter);
        var item = InventoryTestUtils.MockItem(1, 1);

        Assert.True(container.AddOrMoveExistingItem(ItemTaskType.Gm, item, 1));

        var i = container.Items.SingleOrDefault(it => it.TemplateId == 1);

        Assert.NotNull(i);
    }
}
