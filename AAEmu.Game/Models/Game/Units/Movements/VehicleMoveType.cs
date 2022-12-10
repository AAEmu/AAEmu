using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class VehicleMoveType : MoveType
    {
        public new short RotationX { get; set; }
        public new short RotationY { get; set; }
        public new short RotationZ { get; set; }
        public float AngVelX { get; set; }
        public float AngVelY { get; set; }
        public float AngVelZ { get; set; }
        public float Steering { get; set; }
        public List<float> WheelAngVel { get; set; }

        public VehicleMoveType()
        {
            WheelAngVel = new List<float>();
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
            RotationZ = stream.ReadInt16();

            AngVelX = stream.ReadSingle();
            AngVelY = stream.ReadSingle();
            AngVelZ = stream.ReadSingle();
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
            stream.Write((byte)WheelAngVel.Count);
            foreach (var f in WheelAngVel)
            {
                stream.Write(f);
            }

            return stream;
        }
    }
}
