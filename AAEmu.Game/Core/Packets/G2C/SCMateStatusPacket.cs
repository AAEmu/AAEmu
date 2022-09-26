using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMateStatusPacket : GamePacket
    {
        private readonly uint _objId;

        public SCMateStatusPacket(uint objId) : base(SCOffsets.SCMateStatusPacket, 5)
        {
            _objId = objId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(0); // skillCount
            stream.Write(0); // tagCount
            return stream;
        }
    }
}
