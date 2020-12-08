using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSThisTimeUnpackItemPacket : GamePacket
    {
        public CSThisTimeUnpackItemPacket() : base(CSOffsets.CSThisTimeUnpackItemPacket, 1)
        {

        }

        public override void Read(PacketStream stream)
        {
            var slotType = (SlotType)stream.ReadByte();
            var slot = stream.ReadByte();
            var itemId = stream.ReadUInt64();
            _log.Warn("CSThisTimeUnpackItemPacket, slotType: {0}, slot: {1}, itemId: {2}", slotType, slot, itemId);
        }
    }
}
