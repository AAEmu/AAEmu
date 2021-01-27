using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailFailedPacket : GamePacket
    {
        private readonly byte _err;
        private readonly (SlotType slotType, byte slot)[] _items;
        private readonly bool _money;

        public SCMailFailedPacket(byte err, (SlotType slotType, byte slot)[] items, bool money) : base(SCOffsets.SCMailFailedPacket, 5)
        {
            _err = err;
            _items = items;
            _money = money;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_err); // ErrorMessageType
            foreach (var (slotType, slot) in _items) // TODO should be 10 items
            {
                stream.Write((byte)slotType); // type
                stream.Write(slot);           // index
            }
            stream.Write(_money);             // money

            return stream;
        }
    }
}
