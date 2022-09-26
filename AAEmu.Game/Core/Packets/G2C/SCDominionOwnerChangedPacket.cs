using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDominionOwnerChangedPacket : GamePacket
    {
        private readonly ushort _id;
        private readonly uint _unkId;
        private readonly ulong _rst;
        private readonly bool _bestowed;

        public SCDominionOwnerChangedPacket(ushort id, uint unkId, ulong rst, bool bestowed) : base(SCOffsets.SCDominionOwnerChangedPacket, 5)
        {
            _id = id;
            _unkId = unkId;
            _rst = rst;
            _bestowed = bestowed;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_unkId);
            stream.Write(_rst);
            stream.Write(_bestowed);
            return stream;
        }
    }
}
