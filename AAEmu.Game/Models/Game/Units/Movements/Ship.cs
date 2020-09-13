using System;
using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class Ship : UnitMovement
    {
        public Vector3 AngVel { get; set; }
        // ---
        //public float AngVelX { get; set; }
        //public float AngVelY { get; set; }
        //public float AngVelZ { get; set; }
        // ---
        public sbyte Steering { get; set; }
        public sbyte Throttle { get; set; }
        public ushort ZoneId { get; set; }
        public bool Stuck { get; set; } // stucked

        // ---
        //public new short RotationX { get; set; }
        //public new short RotationY { get; set; }
        //public new short RotationZ { get; set; }
        // ---

        public void UseSlaveBase(Slave slave)
        {
            X = slave.Position.X;
            Y = slave.Position.Y;
            Z = slave.Position.Z;

            //RotationX = slave.Position.RotationX;
            //RotationY = slave.Position.RotationY;
            //RotationZ = slave.RotationZ;
            Rot = slave.Rot;

            //VelX = slave.VelX;
            //VelY = slave.VelY;
            //VelZ = slave.VelZ;
            Velocity = slave.Velocity;

            //AngVelX = slave.AngVelX;
            //AngVelY = slave.AngVelY;
            //AngVelZ = slave.AngVelZ;
            AngVel = slave.AngVel;

            ZoneId = (ushort) slave.Position.ZoneId;
            Time = (uint)(DateTime.Now - slave.SpawnTime).TotalMilliseconds;
            Stuck = slave.Stuck;
            Steering = slave.Steering;
        }

        public override void Read(PacketStream stream)
        {
            base.Read(stream);
            (X, Y, Z) = stream.ReadPositionBc();
            //var (x, y, z) = stream.ReadWorldPosition();
            WorldPos = new WorldPos(Helpers.ConvertLongX(X), Helpers.ConvertLongX(Y), Z);

            //VelX = stream.ReadInt16();
            //VelY = stream.ReadInt16();
            //VelZ = stream.ReadInt16();
            var tempVelocity = stream.ReadVector3Short();
            Velocity = new Vector3(tempVelocity.X * 30f, tempVelocity.Y * 30f, tempVelocity.Z * 30f);

            //RotationX = stream.ReadInt16();
            //RotationY = stream.ReadInt16();
            //RotationZ = stream.ReadInt16();
            Rot = stream.ReadQuaternionShort();

            //AngVelX = stream.ReadSingle();
            //AngVelY = stream.ReadSingle();
            //AngVelZ = stream.ReadSingle();
            AngVel = stream.ReadVector3Single();

            Steering = stream.ReadSByte();
            Throttle = stream.ReadSByte();
            ZoneId = stream.ReadUInt16();
            Stuck = stream.ReadBoolean();
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.WritePositionBc(X, Y, Z);
            //stream.WriteWorldPosition(WorldPos.X, WorldPos.Y, WorldPos.Z);

            //stream.Write(VelX);
            //stream.Write(VelY);
            //stream.Write(VelZ);
            var tempVelocity = new Vector3(Velocity.X / 30f, Velocity.Y / 30f, Velocity.Z / 30f);
            stream.WriteVector3Short(tempVelocity);

            //stream.Write(RotationX);
            //stream.Write(RotationY);
            //stream.Write(RotationZ);
            stream.WriteQuaternionShort(Rot);


            //stream.Write(AngVelX);
            //stream.Write(AngVelY);
            //stream.Write(AngVelZ);
            stream.WriteVector3Single(AngVel);

            stream.Write(Steering);
            stream.Write(Throttle);
            
            stream.Write(ZoneId);
            stream.Write(Stuck);

            return stream;
        }
    }
}
