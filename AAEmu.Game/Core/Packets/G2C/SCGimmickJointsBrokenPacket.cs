using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGimmickJointsBrokenPacket : GamePacket
    {
        private readonly Gimmick[] _gimmick;

        public SCGimmickJointsBrokenPacket(Gimmick[] gimmick) : base(SCOffsets.SCGimmickJointsBrokenPacket, 1)
        {
            _gimmick = gimmick;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_gimmick.Length); // TODO max length 200
            foreach (var gimmick in _gimmick)
            {
                stream.Write(gimmick.GimmickId); // gimmickId
                stream.Write(0);                 // jointId
                stream.Write(0);                 // epicentr
            }

            return stream;
        }
    }
}
