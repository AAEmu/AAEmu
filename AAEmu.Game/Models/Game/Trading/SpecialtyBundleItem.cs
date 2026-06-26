using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Trading;

public class SpecialtyBundleItem
{
    public uint Id { get; set; }
    public uint ItemId { get; set; }
    public uint SpecialtyBundleId { get; set; }
    public uint Profit { get; set; }
    // Signed: specialty_bundle_items.ratio can be negative (-10000 in 10.0.2.13); consumed as Ratio/1000f
    // in the refund formula, so reading unsigned would turn -10000 into ~4.29e9 and break the math.
    public int Ratio { get; set; }

    public ItemTemplate Item { get; set; }
}
