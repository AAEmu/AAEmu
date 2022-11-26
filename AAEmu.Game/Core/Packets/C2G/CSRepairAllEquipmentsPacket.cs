using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRepairAllEquipmentsPacket : GamePacket
    {
        public CSRepairAllEquipmentsPacket() : base(CSOffsets.CSRepairAllEquipmentsPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var autoUseAAPoint = stream.ReadBoolean();
            var inBag = stream.ReadBoolean();          // inBag

            _log.Debug($"RepairAllEquipments, AutoUseAAPoint: {autoUseAAPoint}, inBag: {inBag}");
        }
    }
}
