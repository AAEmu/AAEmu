using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class Backpack : Item
    {
        public Backpack()
        {
        }

        public Backpack(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
        }
    }
}
