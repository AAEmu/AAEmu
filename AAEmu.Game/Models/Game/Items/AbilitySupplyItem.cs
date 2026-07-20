namespace AAEmu.Game.Models.Game.Items;

public class AbilitySupplyItem
{
    public uint Id { get; set; }

public uint ItemId { get; set; }

public uint AbilityId { get; set; }
    public int Amount { get; set; }
    public byte Grade { get; set; }
}