namespace AAEmu.Game.Models.Game.Items;

public struct ItemIdAndLocation
{
    public ulong Id { get; set; }
    public SlotType SlotType { get; set; }
    public byte Slot { get; set; }
    
    public override string ToString()
    {
        return $"({SlotType} #{Slot}: {Id})";
    }
}
