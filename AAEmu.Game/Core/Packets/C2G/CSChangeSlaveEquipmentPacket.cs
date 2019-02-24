using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeSlaveEquipmentPacket : GamePacket
    {
        public CSChangeSlaveEquipmentPacket() : base(0x035, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO ... coming soon

            _log.Debug("ChangeSlaveEquipment");
        }
    }
}
