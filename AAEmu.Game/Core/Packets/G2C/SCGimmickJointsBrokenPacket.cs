using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGimmickJointsBrokenPacket : GamePacket
    {
        private readonly Gimmick[] _gimmick;
        private readonly int _jointId;
        private readonly int _epicentr;

        public SCGimmickJointsBrokenPacket(Gimmick[] gimmick) : base(SCOffsets.SCGimmickJointsBrokenPacket, 1)
        {
            _gimmick = gimmick;
            _jointId = 0;
            _epicentr = 0;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_gimmick.Length); // TODO max length 200
            foreach (var gimmick in _gimmick)
            {
                stream.Write(gimmick.GimmickId); // gimmickId
                stream.Write(_jointId);          // jointId
                stream.Write(_epicentr);         // epicentr
            }

            return stream;
        }
    }
}
