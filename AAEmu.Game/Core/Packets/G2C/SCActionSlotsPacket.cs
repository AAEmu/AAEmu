using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCActionSlotsPacket : GamePacket
    {
        private readonly ActionSlot[] _slots;

        public SCActionSlotsPacket(ActionSlot[] slots) : base(0x12b, 1) // 0x127
        {
            _slots = slots;
        }

        public override PacketStream Write(PacketStream stream)
        {
            foreach (var slot in _slots)
            {
                stream.Write((byte) slot.Type);
                if (slot.Type != ActionSlotType.None)
                    stream.Write(slot.ActionId);
            }

            return stream;
        }
    }
}
