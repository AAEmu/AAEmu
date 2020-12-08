using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTakedownTradeItemPacket : GamePacket
    {
        public CSTakedownTradeItemPacket() : base(CSOffsets.CSTakedownTradeItemPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var slotType = (SlotType)stream.ReadByte();
            var slot = stream.ReadByte();
            
            //_log.Warn("TakedownTradeItem, SlotType: {0}, Slot: {1}", slotType, slot);
            TradeManager.Instance.RemoveItem(Connection.ActiveChar, slotType, slot);
        }
    }
}
