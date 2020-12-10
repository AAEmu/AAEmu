using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeMateEquipmentPacket : GamePacket
    {
        // TODO fix struct
        public CSChangeMateEquipmentPacket() : base(CSOffsets.CSChangeMateEquipmentPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var unkId = stream.ReadUInt32();
            var tl = stream.ReadUInt16();
            var unk2Id = stream.ReadUInt32();
            var bts = stream.ReadBoolean();
            var num = stream.ReadByte();
            // TODO read 2 items, and 2 slots (v)

            _log.Warn("ChangeMateEquipment, TlId: {0}, Id: {1}, Id2: {2}", tl, unkId, unk2Id);
        }
    }
}
