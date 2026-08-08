namespace AAEmu.Game.Models.Game.Items;

public enum SlotType : byte
{
    None = 0,
    Equipment = 1,
    Inventory = 2,
    Bank = 3,
    Trade = 4,
    Mail = 5,
    Auction = 6,
    EquipmentSlave = 0xF2,
    EquipmentMate = 237,
    System = 0xFF
}
