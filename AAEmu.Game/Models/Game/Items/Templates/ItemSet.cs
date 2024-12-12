using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Items.Templates;

public class ItemSet
{
    public uint Id { get; set; }
    public uint KindId { get; set; }
    public string Name { get; set; }
    public Dictionary<uint, ItemSetItem> Items { get; set; } = [];
}

public class ItemSetItem
{
    public uint Id { get; set; }
    public uint ItemSetId { get; set; }
    public uint ItemId { get; set; }
    public int Count { get; set; }
}
