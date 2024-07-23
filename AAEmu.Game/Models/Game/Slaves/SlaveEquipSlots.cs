using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Models.Game.Slaves;

public class SlaveEquipSlots
{
    public uint Id { get; set; }
    public AttachPointKind AttachPoint { get; set; }
    public uint EquipSlotId { get; set; }
    public int RequireSlotId { get; set; }
    public uint SlaveId { get; set; }
}
