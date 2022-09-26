using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCNpcChatMessagePacket : GamePacket
    {
        private readonly ChatType _type;
        private readonly short _subType;
        private readonly uint _factionId;

        public SCNpcChatMessagePacket(ChatType type, short subType, uint factionId) : base(SCOffsets.SCNpcChatMessagePacket, 5)
        {
            _type = type;
            _subType = subType;
            _factionId = factionId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((short)_type);
            stream.Write(_subType);
            stream.Write(_factionId);
            stream.WriteBc(0); // bc // npcObjId?
            stream.Write("test"); // name
            stream.WriteBc(0); // bc
            stream.Write((byte)0); // kind
            // stream.Write(0); // type
            stream.Write("test"); // text

            return stream;
        }
    }
}
