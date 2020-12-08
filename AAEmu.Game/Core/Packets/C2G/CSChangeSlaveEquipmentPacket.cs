using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeSlaveEquipmentPacket : GamePacket
    {
        public CSChangeSlaveEquipmentPacket() : base(CSOffsets.CSChangeSlaveEquipmentPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO ... coming soon
            var id = stream.ReadUInt32(); // type (id)
            var tl = stream.ReadUInt16();
            var dbSlaveId = stream.ReadUInt32();
            var bts = stream.ReadBoolean();

            _log.Debug("ChangeSlaveEquipment, Id: {0}, Tl: {1}, DbSlaveId: {2}, Bts: {3}", id, tl, dbSlaveId, bts);
        }
    }
}
