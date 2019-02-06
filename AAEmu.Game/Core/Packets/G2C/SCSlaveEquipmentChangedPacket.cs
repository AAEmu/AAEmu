using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveEquipmentChangedPacket : GamePacket
    {
        private SlaveEquipment slaveEquipment;
        private bool success;

        public SCSlaveEquipmentChangedPacket(SlaveEquipment slaveEquipment, bool success) : base(0x063, 1)
        {

        }

        public override PacketStream Write(PacketStream stream)
        {
            // TODO coming soon!
            return stream;
        }
    }
}
