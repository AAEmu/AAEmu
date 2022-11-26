using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRepairSingleEquipmentPacket : GamePacket
    {
        public CSRepairSingleEquipmentPacket() : base(CSOffsets.CSRepairSingleEquipmentPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var slotType = (SlotType)stream.ReadByte();     // type
            var slot = stream.ReadByte();              // index
            var autoUseAAPoint = stream.ReadBoolean(); // autoUseAAPoint
            var inBag = stream.ReadBoolean();          // inBag

            _log.Debug($"RepairSingleEquipment, SlotType: {slotType}, Slot: {slot}, AutoUseAAPoint: {autoUseAAPoint}, InBag: {inBag}");
        }
    }
}
