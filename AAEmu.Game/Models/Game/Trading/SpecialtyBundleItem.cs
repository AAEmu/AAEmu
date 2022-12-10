using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Trading
{
    public class SpecialtyBundleItem
    {
        public uint Id { get; set; }
        public uint ItemId { get; set; }
        public uint SpecialtyBundleId { get; set; }
        public uint Profit { get; set; }
        public uint Ratio { get; set; }
        
        public ItemTemplate Item { get; set; }
    }
}
