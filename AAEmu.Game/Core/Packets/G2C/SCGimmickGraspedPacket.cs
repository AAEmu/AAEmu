using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGimmickGraspedPacket : GamePacket
    {
        private readonly Gimmick _gimmick;

        public SCGimmickGraspedPacket(Gimmick gimmick) : base(SCOffsets.SCGimmickGraspedPacket, 1)
        {
            _gimmick = gimmick;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_gimmick.GimmickId);
            stream.Write(_gimmick.GrasperUnitId);
            stream.Write(true); // grasped

            return stream;
        }
    }
}
