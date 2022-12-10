using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Tests.Unit.Utils.Mocks;

namespace AAEmu.Tests.Unit.Utils
{
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
}
