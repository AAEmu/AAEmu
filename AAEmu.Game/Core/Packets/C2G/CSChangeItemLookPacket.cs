using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeItemLookPacket : GamePacket
    {
        public CSChangeItemLookPacket() : base(CSOffsets.CSChangeItemLookPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            stream.ReadByte();
            var slotType1 = (SlotType)stream.ReadByte();
            stream.ReadByte();
            var slot1 = stream.ReadByte();

            stream.ReadByte();
            var slotType2 = (SlotType)stream.ReadByte();
            stream.ReadByte();
            var slot2 = stream.ReadByte();

            var itemId = stream.ReadUInt64();
            var lookId = stream.ReadUInt64();

            _log.Warn("ChangeItemLook, ItemId: {0}, LookId: {1}", itemId, lookId);
        }
    }
}
