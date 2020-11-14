using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class DefaultUnitMovement : UnitMovement
    {
        public override void Read(PacketStream stream)
        {
            base.Read(stream);
            (X, Y, Z) = stream.ReadPositionBc();
            WorldPos = new WorldPos(Helpers.ConvertLongX(X), Helpers.ConvertLongY(Y), Z);

            //VelX = stream.ReadInt16();
            //VelY = stream.ReadInt16();
            //VelZ = stream.ReadInt16();
            var vx = stream.ReadInt16();
            var vy = stream.ReadInt16();
            var vz = stream.ReadInt16();
            Velocity = new Vector3(vx, vy, vz);

            //RotationX = (sbyte)stream.ReadInt16();
            //RotationY = (sbyte)stream.ReadInt16();
            //RotationZ = (sbyte)stream.ReadInt16();
            //var rx = stream.ReadSByte();
            //var ry = stream.ReadSByte();
            //var rz = stream.ReadSByte();
            //Rot = new Quaternion(Helpers.ConvertDirectionToRadian(rx), Helpers.ConvertDirectionToRadian(ry), Helpers.ConvertDirectionToRadian(rz), 1f);
            Rot = stream.ReadQuaternionShort();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.WritePositionBc(X, Y, Z);
            //stream.WritePosition(Helpers.ConvertLongX(WorldPos.X), Helpers.ConvertLongX(WorldPos.Y), WorldPos.Z);

            //stream.Write(VelX);
            //stream.Write(VelY);
            //stream.Write(VelZ);
            stream.WriteVector3Short(Velocity);

            //stream.Write((short)RotationX);
            //stream.Write((short)RotationY);
            //stream.Write((short)RotationZ);
            stream.WriteQuaternionShort(Rot);

            return stream;
        }
    }
}
