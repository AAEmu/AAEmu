using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitInvisiblePacket : GamePacket
    {
        private readonly uint _objId;
        private readonly bool _invisible;

        public SCUnitInvisiblePacket(uint objId, bool invisible) : base(SCOffsets.SCUnitInvisiblePacket, 5)
        {
            _objId = objId;
            _invisible = invisible;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_invisible);
            return stream;
        }
    }
}
