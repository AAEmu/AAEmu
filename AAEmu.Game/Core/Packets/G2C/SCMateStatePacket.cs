using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMateStatePacket : GamePacket
    {
        private readonly uint _objId;

        public SCMateStatePacket(uint objId) : base(SCOffsets.SCMateStatePacket, 5)
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
