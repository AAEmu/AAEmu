using System.Collections.Generic;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class Vehicle : UnitMovement
    {

        public Vector3 AngVel { get; set; }
        // ---
        //public float AngVelX { get; set; }
        //public float AngVelY { get; set; }
        //public float AngVelZ { get; set; }
        // ---
        public float Steering { get; set; }
        public List<float> WheelAngVel { get; set; } // float wheelAngVel[18]

        // ---
        //public new short RotationX { get; set; }
        //public new short RotationY { get; set; }
        //public new short RotationZ { get; set; }
        // ---
        public Vehicle()
        {
            WheelAngVel = new List<float>();
        }

        public override void Read(PacketStream stream)
        {
            base.Read(stream);
            (X, Y, Z) = stream.ReadPositionBc();
            //var (x, y, z) = stream.ReadWorldPosition();
            WorldPos = new WorldPos(Helpers.ConvertLongX(X), Helpers.ConvertLongX(Y), Z);

            //VelX = stream.ReadInt16(); // TODO убрать
            //VelY = stream.ReadInt16(); // TODO убрать
            //VelZ = stream.ReadInt16(); // TODO убрать
            var tempVelocity = stream.ReadVector3Short();
            Velocity = new Vector3(tempVelocity.X * 30f, tempVelocity.Y * 30f, tempVelocity.Z * 30f);

            //RotationX = stream.ReadInt16(); // TODO убрать
            //RotationY = stream.ReadInt16(); // TODO убрать
            //RotationZ = stream.ReadInt16(); // TODO убрать
            Rot = stream.ReadQuaternionShort();

            //AngVelX = stream.ReadSingle(); // TODO убрать
            //AngVelY = stream.ReadSingle(); // TODO убрать
            //AngVelZ = stream.ReadSingle(); // TODO убрать
            AngVel = stream.ReadVector3Single();

            Steering = stream.ReadSingle();
            var wheelAngs = stream.ReadByte();
            for (var i = 0; i < wheelAngs; i++)
            {
                WheelAngVel.Add(stream.ReadSingle());
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.WritePositionBc(X, Y, Z);
            //stream.WriteWorldPosition(WorldPos.X, WorldPos.Y, WorldPos.Z);

            //stream.Write(VelX); // TODO убрать
            //stream.Write(VelY); // TODO убрать
            //stream.Write(VelZ); // TODO убрать
            var tempVelocity = new Vector3(Velocity.X / 30f, Velocity.Y / 30f, Velocity.Z / 30f);
            stream.WriteVector3Short(tempVelocity);

            //stream.Write(RotationX); // TODO убрать
            //stream.Write(RotationY); // TODO убрать
            //stream.Write(RotationZ); // TODO убрать
            stream.WriteQuaternionShort(Rot);

            //stream.Write(AngVelX); // TODO убрать
            //stream.Write(AngVelY); // TODO убрать
            //stream.Write(AngVelZ); // TODO убрать
            stream.WriteVector3Single(AngVel);


            stream.Write(Steering);
            stream.Write((byte)WheelAngVel.Count);
            foreach (var f in WheelAngVel)
            {
                stream.Write(f);
            }

            return stream;
        }
    }
}
