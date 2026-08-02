using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Trading;

public sealed class TradeGood
{
    public uint Id { get; set; }
    public uint ItemId { get; set; }
    public uint Count { get; set; }
    public uint Ratio { get; set; }
    public uint Profit { get; set; }
    public uint TradeGoodCategoryId { get; set; }
    public int DisplayOrder { get; set; }
    public ItemTemplate Item { get; set; }
}
