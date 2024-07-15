namespace AAEmu.Game.Models.Game.Items.Slave;

public class ItemSlaveEquip
{
    public uint Id { get; set; }
    public uint ItemId { get; set; }
    public float DoodadScale { get; set; }
    public uint DoodadId { get; set; }
    public uint RequireItemId { get; set; }
    public uint SlaveEquipPackId { get; set; }
    public uint SlaveId { get; set; }
    public uint SlotPackId { get; set; }
}
