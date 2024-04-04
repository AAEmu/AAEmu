using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRepairSingleEquipmentPacket : GamePacket
{
    public CSRepairSingleEquipmentPacket() : base(CSOffsets.CSRepairSingleEquipmentPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var slotType = (SlotType)stream.ReadByte();
        var slot = stream.ReadByte();
        var autoUseAAPoint = stream.ReadBoolean();
        var inBag = stream.ReadBoolean();

        Logger.Debug($"RepairSingleEquipment, SlotType={slotType}, Slot={slot}, AutoUseAAPoint={autoUseAAPoint}, inBag={inBag}");

        var item = Connection.ActiveChar.Inventory.GetItem(slotType, slot);
        Connection.ActiveChar.DoRepair([item]);
    }
}
