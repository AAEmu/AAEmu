using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDuelStatePacket : GamePacket
    {
        private readonly uint _challengerObjId;
        private readonly uint _flagObjId;

        public SCDuelStatePacket(uint challengerObjId, uint flagObjId) : base(SCOffsets.SCDuelStatePacket, 1)
        {
            _challengerObjId = challengerObjId;
            _flagObjId = flagObjId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_challengerObjId);  // challengerObjId
            stream.WriteBc(_flagObjId);       // flagObjId

            return stream;
        }
    }
}
