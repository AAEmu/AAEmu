using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGachaLootPackItemResultPacket : GamePacket
    {
        private readonly ErrorMessageType _errorMessage;
        private readonly int _count;
        private readonly int _itemCount;
        private readonly bool _finish;
        private readonly Item _item;

        public SCGachaLootPackItemResultPacket(ErrorMessageType errorMessage, int count, int itemCount, bool finish, Item item)
            : base(SCOffsets.SCGachaLootPackItemResultPacket, 5)
        {
            _errorMessage = errorMessage;
            _count = count;
            _itemCount = itemCount;
            _finish = finish;
            _item = item;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((short)_errorMessage);
            if (_errorMessage == 0)
            {
                stream.Write(_count);
                stream.Write(_itemCount);
                stream.Write(_finish);
                if (_itemCount > 0)
                {
                    for (var i = 0; i < _count; i++)
                    {
                        stream.Write(_item);
                    }
                }
            }

            return stream;
        }
    }
}
