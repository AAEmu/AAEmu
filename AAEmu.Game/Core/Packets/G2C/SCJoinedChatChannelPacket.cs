using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCJoinedChatChannelPacket : GamePacket
    {
        private readonly ChatType _type;
        private readonly short _subType;
        private readonly uint _factionId;

        public SCJoinedChatChannelPacket(ChatType type, short subType, uint factionId) : base(SCOffsets.SCJoinedChatChannelPacket, 1)
        {
            _type = type;
            _subType = subType;
            _factionId = factionId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((short) _type);
            stream.Write(_subType);
            stream.Write(_factionId);
            // -------------
            stream.Write(""); // name
            return stream;
        }
    }
}
