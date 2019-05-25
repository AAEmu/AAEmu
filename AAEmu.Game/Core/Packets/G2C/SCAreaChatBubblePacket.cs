using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAreaChatBubblePacket : GamePacket
    {
        private readonly bool _enter;
        private readonly uint _unitObjId;
        private readonly uint _type;

        public SCAreaChatBubblePacket(bool enter, uint unitObjId, uint type) : base(SCOffsets.SCAreaChatBubblePacket, 1)
        {
            _enter = enter;
            _unitObjId = unitObjId;
            _type = type;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_enter);        // enter
            stream.WriteBc(_unitObjId); // ObjId
            stream.Write(_type);       // type

            return stream;
        }
    }
}
