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
            stream.Write(Helpers.ConvertLongX(_gimmick.Transform.World.Position.X)); // WorldPosition qx,qx,fz
            stream.Write(Helpers.ConvertLongY(_gimmick.Transform.World.Position.Y));
            stream.Write(_gimmick.Transform.World.Position.Z);
            stream.Write(_gimmick.Rot.X); // Quaternion Rotation
            stream.Write(_gimmick.Rot.Y);
            stream.Write(_gimmick.Rot.Z);
            stream.Write(_gimmick.Rot.W);
            stream.Write(_gimmick.Vel.X);    // vector3 vel
            stream.Write(_gimmick.Vel.Y);
            stream.Write(_gimmick.Vel.Z);
            stream.Write(_gimmick.AngVel.X); // vector3 angVel
            stream.Write(_gimmick.AngVel.Y);
            stream.Write(_gimmick.AngVel.Z);
            stream.Write(_gimmick.Scale);

            return stream;
        }
    }
}
