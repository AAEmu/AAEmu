using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Utils;

public class InventoryTestUtils
{
    public static ItemMock MockItem(uint id, uint templateId)
    {
        // var id = ItemIdManager.Instance.GetNextId();
        var template = new ItemTemplate()
        {
            Id = templateId,
            BindType = ItemBindType.Normal
        };

        var item = new ItemMock(id, template);
        return item;
    }
}
