using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class ActorData : UnitMovement
    {
        public Vector3 DeltaMovement { get; set; }
        // ---
        //public sbyte[] DeltaMovement { get; set; }
        // ---
        public ushort FallVel { get; set; }
        public sbyte Stance { get; set; }
        public sbyte Alertness { get; set; }
        public byte GcFlags { get; set; }
        public ushort GcPartId { get; set; }
        public uint GcId { get; set; }
        public WorldPos GcWorldPos { get; set; }
        // +++
        public float X2 { get; set; }
        public float Y2 { get; set; }
        public float Z2 { get; set; }
        // +++
        public Quaternion GcWorldRot { get; set; }
        // ---
        //public sbyte RotationX2 { get; set; }
        //public sbyte RotationY2 { get; set; }
        //public sbyte RotationZ2 { get; set; }
        // ---
        public uint ClimbData { get; set; }

        public ActorData()
        {
            WorldPos = new WorldPos(Helpers.ConvertLongX(X), Helpers.ConvertLongX(Y), Z);
            GcWorldPos = new WorldPos(Helpers.ConvertLongX(X2), Helpers.ConvertLongX(Y2), Z2);
        }

        public override void Read(PacketStream stream)
        {
            base.Read(stream);
            (X, Y, Z) = stream.ReadPositionBc();
            WorldPos = new WorldPos(Helpers.ConvertLongX(X), Helpers.ConvertLongX(Y), Z);

            //VelX = stream.ReadInt16();
            //VelY = stream.ReadInt16();
            //VelZ = stream.ReadInt16();
            var tempVelocity = stream.ReadVector3Short();
            Velocity = new Vector3(tempVelocity.X * 60f, tempVelocity.Y * 60f, tempVelocity.Z * 60f);

            //RotationX = stream.ReadSByte();
            //RotationY = stream.ReadSByte();
            //RotationZ = stream.ReadSByte();
            Rot = stream.ReadQuaternionSbyte();

            //DeltaMovement = new sbyte[3];
            //DeltaMovement[0] = stream.ReadSByte();
            //DeltaMovement[1] = stream.ReadSByte();
            //DeltaMovement[2] = stream.ReadSByte();
            DeltaMovement = stream.ReadVector3Sbyte();

            Stance = stream.ReadSByte();
            Alertness = stream.ReadSByte();
            Flags = stream.ReadByte();
            if ((Flags & 0x80) == 0x80)
            {
                FallVel = stream.ReadUInt16(); // actor.fallVel
            }

            if ((Flags & 0x20) == 0x20)
            {
                GcFlags = stream.ReadByte();    // actor.gcFlags
                GcPartId = stream.ReadUInt16(); // actor.gcPartId
                (X2, Y2, Z2) = stream.ReadPositionBc(); // ix, iy, iz
                GcWorldPos = new WorldPos(Helpers.ConvertLongX(X2), Helpers.ConvertLongX(Y2), Z2);
                //var (x2, y2, z2) = stream.ReadWorldPosition();
                //GcWorldPos = new WorldPos(x2, y2, z2);

                //RotationX2 = stream.ReadSByte();
                //RotationY2 = stream.ReadSByte(); 
                //RotationZ2 = stream.ReadSByte();
                GcWorldRot = stream.ReadQuaternionSbyte();
            }
            if ((Flags & 0x60) == 0x60)
            {
                GcId = stream.ReadUInt32(); // actor.gcId
            }

            if ((Flags & 0x40) == 0x40)
            {
                ClimbData = stream.ReadUInt32(); // actor.climbData
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.WritePositionBc(X, Y, Z);
            WorldPos = new WorldPos(Helpers.ConvertLongX(X), Helpers.ConvertLongX(Y), Z);
            //stream.WriteWorldPosition(WorldPos.X, WorldPos.Y, WorldPos.Z);

            //stream.Write(VelX);
            //stream.Write(VelY);
            //stream.Write(VelZ);
            var tempVelocity = new Vector3(Velocity.X / 60f, Velocity.Y / 60f, Velocity.Z / 60f);
            stream.WriteVector3Short(tempVelocity);

            //stream.Write(RotationX);
            //stream.Write(RotationY);
            //stream.Write(RotationZ);
            stream.WriteQuaternionSbyte(Rot);

            //stream.Write(DeltaMovement[0]);
            //stream.Write(DeltaMovement[1]);
            //stream.Write(DeltaMovement[2]);
            stream.WriteVector3Sbyte(DeltaMovement);

            stream.Write(Stance);
            stream.Write(Alertness);
            stream.Write(Flags);
            if ((Flags & 0x80) == 0x80)
            {
                stream.Write(FallVel);
            }

            if ((Flags & 0x20) == 0x20)
            {
                stream.Write(GcFlags);
                stream.Write(GcPartId);

                stream.WritePositionBc(X2, Y2, Z2);
                GcWorldPos = new WorldPos(Helpers.ConvertLongX(X2), Helpers.ConvertLongX(Y2), Z2);
                //stream.WriteWorldPosition(GcWorldPos.X, GcWorldPos.Y, GcWorldPos.Z);

                //stream.Write(RotationX2);
                //stream.Write(RotationY2);
                //stream.Write(RotationZ2);
                stream.WriteQuaternionSbyte(GcWorldRot);

            }
            if ((Flags & 0x60) == 0x60)
            {
                stream.Write(GcId);
            }

            if ((Flags & 0x40) == 0x40)
            {
                stream.Write(ClimbData);
            }

            return stream;
        }
    }
}
