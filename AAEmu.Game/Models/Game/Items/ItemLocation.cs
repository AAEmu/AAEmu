namespace AAEmu.Game.Models.Game.Items;

public struct ItemLocation
{
    public SlotType SlotType { get; set; }
    public byte Slot { get; set; }

    public override string ToString()
    {
        return $"({SlotType} #{Slot})";
    }
}
