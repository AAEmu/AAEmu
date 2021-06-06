using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveEquipmentChangedPacket : GamePacket
    {
        private SlaveEquipment _slaveEquipment;
        private bool _success;

        public SCSlaveEquipmentChangedPacket(SlaveEquipment slaveEquipment, bool success) : base(SCOffsets.SCSlaveEquipmentChangedPacket, 1)
        {
            _slaveEquipment = slaveEquipment;
            _success = success;
        }

        public override PacketStream Write(PacketStream stream)
        {
            // TODO: Implement SCSlaveEquipmentChangedPacket.Write()
            return stream;
        }
    }
}
