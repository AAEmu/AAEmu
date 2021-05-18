using System;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class TransferData : MoveType
    {
        public Vector3 AngVel { get; set; }
        public float AngVelX { get; set; }
        public float AngVelY { get; set; }
        public float AngVelZ { get; set; }
        public int Steering { get; set; }
        public int PathPointIndex { get; set; }
        public float Speed { get; set; }
        public bool Reverse { get; set; }
        public float RotationDegrees { get; set; }
        public float RotSpeed { get; set; }  // ?
        public sbyte Throttle { get; set; } // ?

        public TransferData()
        {
            WorldPos = new WorldPos(Helpers.ConvertLongX(X), Helpers.ConvertLongY(Y), Z);
        }

        public void UseTransferBase(Transfer transfer)
        {
            X = transfer.Position.X;
            Y = transfer.Position.Y;
            Z = transfer.Position.Z;
            //WorldPos.X = transfer.WorldPos.X;
            //WorldPos.Y = transfer.WorldPos.Y;
            //WorldPos.Z = transfer.WorldPos.Z;
            WorldPos = transfer.WorldPos;
            Rot = transfer.Rot;
            //RotationX = transfer.RotationX;
            //RotationY = transfer.RotationY;
            //RotationZ = transfer.RotationZ;
            RotSpeed = transfer.RotSpeed;
            RotationDegrees = transfer.RotationDegrees;
            Velocity = transfer.Velocity;
            //VelX = transfer.VelX;
            //VelY = transfer.VelY;
            //VelZ = transfer.VelZ;
            AngVel = transfer.AngVel;
            //AngVelX = transfer.AngVelX;
            //AngVelY = transfer.AngVelY;
            //AngVelZ = transfer.AngVelZ;
            Steering = transfer.Steering;
            Throttle = transfer.Throttle;
            PathPointIndex = transfer.PathPointIndex;
            Speed = transfer.Speed;
            Reverse = transfer.Reverse;
            Time = (uint)(DateTime.Now - transfer.SpawnTime).TotalMilliseconds;
        }

        public override void Read(PacketStream stream)
        {
            base.Read(stream);
            (X, Y, Z) = stream.ReadPosition();
            WorldPos = new WorldPos(Helpers.ConvertLongX(X), Helpers.ConvertLongY(Y), Z);
            var tempVelocity = stream.ReadVector3Short();
            Velocity = new Vector3(tempVelocity.X * 50f, tempVelocity.Y * 50f, tempVelocity.Z * 50f);
            Rot = stream.ReadQuaternionShort();
            AngVel = stream.ReadVector3Single();
            Steering = stream.ReadInt32();
            PathPointIndex = stream.ReadInt32();
            Speed = stream.ReadSingle();
            Reverse = stream.ReadBoolean();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.WritePosition(X, Y, Z);
            //stream.WriteVector3Short(new Vector3(Velocity.X / 50f, Velocity.Y / 50f, Velocity.Z / 50f));
            stream.WriteVector3Short(new Vector3(Velocity.X * 0.02f, Velocity.Y * 0.02f, Velocity.Z * 0.02f));
            stream.WriteQuaternionShort(Rot);
            stream.WriteVector3Single(AngVel);
            stream.Write(Steering);
            stream.Write(PathPointIndex);
            stream.Write(Speed);
            stream.Write(Reverse);

            return stream;
        }
    }
}
