using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSItemUnsecurePacket : GamePacket
    {
        public CSItemUnsecurePacket() : base(CSOffsets.CSItemUnsecurePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            stream.ReadByte();
            var slotType = (SlotType)stream.ReadByte();
            stream.ReadByte();
            var slot = stream.ReadByte();

            var itemId = stream.ReadUInt64();

            _log.Warn("ItemUnsecure, ItemId: {0}, SlotType: {1}, Slot: {2}", itemId, slotType, slot);
        }
    }
}
