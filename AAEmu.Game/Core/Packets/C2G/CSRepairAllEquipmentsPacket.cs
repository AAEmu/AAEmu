using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRepairAllEquipmentsPacket : GamePacket
    {
        public CSRepairAllEquipmentsPacket() : base(0x03b, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("RepairAllEquipments");
        }
    }
}
