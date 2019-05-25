using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDuelStartedPacket : GamePacket
    {
        private readonly uint _challengerObjId;
        private readonly uint _challengedObjId;

        public SCDuelStartedPacket(uint challengerObjId, uint challengedObjId) : base(SCOffsets.SCDuelStartedPacket, 1)
        {
            _challengerObjId = challengerObjId;
            _challengedObjId = challengedObjId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_challengerObjId);  // challengerObjId
            stream.WriteBc(_challengedObjId); // challengedObjId

            return stream;
        }
    }
}
