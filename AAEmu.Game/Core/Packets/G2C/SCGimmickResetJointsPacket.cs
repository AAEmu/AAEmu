using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGimmickResetJointsPacket : GamePacket
    {
        private readonly Gimmick _gimmick;

        public SCGimmickResetJointsPacket(Gimmick gimmick) : base(SCOffsets.SCGimmickResetJointsPacket, 1)
        {
            _gimmick = gimmick;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_gimmick.GimmickId);

            return stream;
        }
    }
}
