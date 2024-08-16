namespace AAEmu.Game.Models.Game.Items;

public struct ItemAndLocation
{
    public Item Item { get; set; }
    public SlotType SlotType { get; set; }
    public byte SlotNumber { get; set; }

    public override string ToString()
    {
        return $"({SlotType} #{SlotNumber}: {Item?.Id ?? 0})";
    }
}
