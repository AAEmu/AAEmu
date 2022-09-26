using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTradeItemTookdownPacket : GamePacket
    {
        private readonly SlotType _slotType;
        private readonly byte _slot;

        public SCTradeItemTookdownPacket(SlotType slotType, byte slot) : base(SCOffsets.SCTradeItemTookdownPacket, 5)
        {
            _slotType = slotType;
            _slot = slot;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_slotType);
            stream.Write(_slot);
            return stream;
        }
    }
}
