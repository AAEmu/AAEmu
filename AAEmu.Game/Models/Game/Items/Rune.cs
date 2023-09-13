using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class Rune : Item
    {
        public Rune()
        {
        }

        public Rune(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
        }
    }
}
