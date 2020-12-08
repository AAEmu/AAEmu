using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRepairAllEquipmentsPacket : GamePacket
    {
        public CSRepairAllEquipmentsPacket() : base(CSOffsets.CSRepairAllEquipmentsPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var autoUseAAPoint = stream.ReadBoolean();
            _log.Debug("RepairAllEquipments, AutoUseAAPoint: {0}", autoUseAAPoint);
        }
    }
}
