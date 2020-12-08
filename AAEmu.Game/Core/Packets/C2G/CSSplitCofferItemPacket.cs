using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSplitCofferItemPacket : GamePacket
    {
        public CSSplitCofferItemPacket() : base(CSOffsets.CSSplitCofferItemPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var count = stream.ReadUInt32();
            var srcId = stream.ReadUInt64();
            var dstId = stream.ReadUInt64();

            stream.ReadByte();
            var srcSlotType = (SlotType)stream.ReadByte();
            stream.ReadByte();
            var srcSlot = stream.ReadByte();

            stream.ReadByte();
            var dstSlotType = (SlotType)stream.ReadByte();
            stream.ReadByte();
            var dstSlot = stream.ReadByte();

            var dbDoodadId = stream.ReadInt64();

            _log.Debug("SplitCofferItem");
        }
    }
}
