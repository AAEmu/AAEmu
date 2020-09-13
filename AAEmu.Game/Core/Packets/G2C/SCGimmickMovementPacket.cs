using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGimmickMovementPacket : GamePacket
    {
        private readonly Gimmick _gimmick;

        public SCGimmickMovementPacket(Gimmick gimmick) : base(SCOffsets.SCGimmickMovementPacket, 1)
        {
            _gimmick = gimmick;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_gimmick.GimmickId);
            stream.Write(_gimmick.Time);
            stream.WriteWorldPosition(_gimmick.Position.X, _gimmick.Position.Y, _gimmick.Position.Z); // WorldPos
            stream.WriteQuaternionSingle(_gimmick.Rot, true); // Quaternion Rotation
            stream.WriteVector3Single(_gimmick.Vel);
            stream.WriteVector3Single(_gimmick.AngVel);
            stream.Write(_gimmick.Scale);

            return stream;
        }
    }
}
