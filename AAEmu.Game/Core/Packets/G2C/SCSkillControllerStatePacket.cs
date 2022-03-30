using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSkillControllerStatePacket : GamePacket
    {
        private readonly uint _objId;
        private readonly byte _scType;
        private readonly float _len;
        private readonly bool _teared;
        private readonly bool _cutouted;

        public SCSkillControllerStatePacket(uint objId, byte scType, float len, bool teared, bool cutouted) : base(SCOffsets.SCSkillControllerStatePacket, 5)
        {
            _objId = objId;
            _scType = scType;
            _len = len;
            _teared = teared;
            _cutouted = cutouted;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_scType);
            stream.Write(_len);
            stream.Write(_teared);
            stream.Write(_cutouted);
            return stream;
        }
    }
}
