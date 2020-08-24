using System;
using System.Numerics;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class TransferMoveType : MoveType
    {
        public new short RotationX { get; set; }
        public new short RotationY { get; set; }
        public new ushort RotationZ { get; set; }
        public float AngVelX { get; set; }
        public float AngVelY { get; set; }
        public float AngVelZ { get; set; }
        //public Vector3 Vel { get; set; }
        //public Vector3 AngVel { get; set; }
        //public Quaternion Rotation { get; set; }
        public int Steering { get; set; }
        public int PathPointIndex { get; set; }
        public float Speed { get; set; }
        public bool Reverse { get; set; }
        public float RotationDegrees { get; set; }
        public float RotSpeed { get; set; }  // ?
        public sbyte Throttle { get; set; } // ?

        public void UseTransferBase(Transfer transfer)
        {
            X = transfer.Position.X;
            Y = transfer.Position.Y;
            Z = transfer.Position.Z;
            RotationX = transfer.RotationX;
            RotationY = transfer.RotationY;
            RotationZ = transfer.RotationZ;
            RotSpeed = transfer.RotSpeed;
            RotationDegrees = transfer.RotationDegrees;
            VelX = transfer.VelX;
            VelY = transfer.VelY;
            VelZ = transfer.VelZ;
            AngVelX = transfer.AngVelX;
            AngVelY = transfer.AngVelY;
            AngVelZ = transfer.AngVelZ;
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

            VelX = stream.ReadInt16();
            VelY = stream.ReadInt16();
            VelZ = stream.ReadInt16();
            RotationX = stream.ReadInt16();
            RotationY = stream.ReadInt16();
            RotationZ = stream.ReadUInt16();

            AngVelX = stream.ReadSingle();
            AngVelY = stream.ReadSingle();
            AngVelZ = stream.ReadSingle();
            Steering = stream.ReadInt32();
            PathPointIndex = stream.ReadInt32();
            Speed = stream.ReadSingle();
            Reverse = stream.ReadBoolean();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.WritePosition(X, Y, Z);
            stream.Write(VelX);
            stream.Write(VelY);
            stream.Write(VelZ);
            stream.Write(RotationX);
            stream.Write(RotationY);
            stream.Write(RotationZ);

            stream.Write(AngVelX);
            stream.Write(AngVelY);
            stream.Write(AngVelZ);
            stream.Write(Steering);
            stream.Write(PathPointIndex);
            stream.Write(Speed);
            stream.Write(Reverse);

            return stream;
        }
    }
}
