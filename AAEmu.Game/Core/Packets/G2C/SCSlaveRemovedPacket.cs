using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveRemovedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly ushort _tl;

        public SCSlaveRemovedPacket(uint id, ushort tl) : base(SCOffsets.SCSlaveRemovedPacket, 5)
        {
            _id = id;
            _tl = tl;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_id);
            stream.Write(_tl);
            return stream;
        }
    }
}
