using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPutupTradeItemPacket : GamePacket
    {
        public CSPutupTradeItemPacket() : base(0x0f0, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            stream.ReadByte();
            var slotType = (SlotType)stream.ReadByte();
            stream.ReadByte();
            var slot = stream.ReadByte();

            var amount = stream.ReadInt32();

            _log.Warn("PutupTradeItem, SlotType: {0}, Slot: {1}, Amount: {2}", slotType, slot, amount);
        }
    }
}
